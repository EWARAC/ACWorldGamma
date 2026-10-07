using System;
using System.Runtime.InteropServices;

namespace ACWorldGamma
{
    /// <summary>
    /// Independent AC World Gamma Direct3D9 light hook.
    ///
    /// This implementation is written specifically for AC World Gamma and uses
    /// the documented IDirect3DDevice9 vtable layout. It does not use the
    /// SkunkVision RenderHook implementation or interfaces.
    /// </summary>
    internal sealed class Direct3DLightHook : IDisposable
    {
        // IDirect3DDevice9::SetLight is vtable slot 51, including IUnknown.
        private const int SetLightVtableIndex = 51;
        private const uint PageExecuteReadWrite = 0x40;

        private IntPtr _vtableEntry = IntPtr.Zero;
        private IntPtr _originalFunction = IntPtr.Zero;
        private IntPtr _replacementFunction = IntPtr.Zero;

        private SetLightDelegate _originalSetLight;
        private SetLightDelegate _replacementSetLight;

        private volatile bool _enabled;
        private volatile int _level;
        private bool _installed;

        // When Decal does not expose its live device, we create and keep a tiny
        // private D3D9 device alive. D3D9 device objects from the system runtime
        // use the runtime's shared method table; patching SetLight there lets us
        // intercept AC's SetLight calls without needing AC's device pointer.
        private IntPtr _ownedD3D9 = IntPtr.Zero;
        private IntPtr _ownedDevice = IntPtr.Zero;
        private IntPtr _dummyWindow = IntPtr.Zero;

        private const uint D3DSdkVersion = 32;
        private const uint D3DAdapterDefault = 0;
        private const int D3DDevTypeHal = 1;
        private const uint D3DCreateSoftwareVertexProcessing = 0x20;
        private const int D3DSwapEffectDiscard = 1;
        private const uint WsPopup = 0x80000000;

        [DllImport("d3d9.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern IntPtr Direct3DCreate9(uint sdkVersion);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
            uint exStyle,
            string className,
            string windowName,
            uint style,
            int x,
            int y,
            int width,
            int height,
            IntPtr parent,
            IntPtr menu,
            IntPtr instance,
            IntPtr param);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtect(
            IntPtr lpAddress,
            UIntPtr dwSize,
            uint flNewProtect,
            out uint lpflOldProtect);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateDeviceDelegate(
            IntPtr direct3D9,
            uint adapter,
            int deviceType,
            IntPtr focusWindow,
            uint behaviorFlags,
            ref D3DPresentParameters presentationParameters,
            out IntPtr returnedDevice);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint ReleaseDelegate(IntPtr unknown);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetLightDelegate(
            IntPtr device,
            uint index,
            ref D3DLight9 light);

        [StructLayout(LayoutKind.Sequential)]
        private struct D3DPresentParameters
        {
            public uint BackBufferWidth;
            public uint BackBufferHeight;
            public int BackBufferFormat;
            public uint BackBufferCount;
            public int MultiSampleType;
            public uint MultiSampleQuality;
            public int SwapEffect;
            public IntPtr DeviceWindow;
            public int Windowed;
            public int EnableAutoDepthStencil;
            public int AutoDepthStencilFormat;
            public uint Flags;
            public uint FullScreenRefreshRateInHz;
            public uint PresentationInterval;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3DColorValue
        {
            public float R;
            public float G;
            public float B;
            public float A;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3DVector
        {
            public float X;
            public float Y;
            public float Z;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3DLight9
        {
            public int Type;
            public D3DColorValue Diffuse;
            public D3DColorValue Specular;
            public D3DColorValue Ambient;
            public D3DVector Position;
            public D3DVector Direction;
            public float Range;
            public float Falloff;
            public float Attenuation0;
            public float Attenuation1;
            public float Attenuation2;
            public float Theta;
            public float Phi;
        }

        public bool Installed
        {
            get { return _installed; }
        }

        public void Install(object direct3DDevice)
        {
            if (direct3DDevice == null)
                throw new ArgumentNullException("direct3DDevice");

            Uninstall();

            IntPtr unknown = IntPtr.Zero;

            try
            {
                unknown = Marshal.GetIUnknownForObject(direct3DDevice);
                if (unknown == IntPtr.Zero)
                    throw new InvalidOperationException("Could not obtain the Direct3D device IUnknown pointer.");

                InstallFromDevicePointer(unknown);
            }
            finally
            {
                if (unknown != IntPtr.Zero)
                {
                    try { Marshal.Release(unknown); } catch { }
                }
            }
        }

        public void InstallUsingPrivateDevice()
        {
            Uninstall();

            try
            {
                _dummyWindow = CreateWindowEx(
                    0,
                    "STATIC",
                    "ACWorldGammaD3DProbe",
                    WsPopup,
                    0, 0, 1, 1,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero);

                if (_dummyWindow == IntPtr.Zero)
                    throw new System.ComponentModel.Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "Could not create the private Direct3D probe window.");

                _ownedD3D9 = Direct3DCreate9(D3DSdkVersion);
                if (_ownedD3D9 == IntPtr.Zero)
                    throw new InvalidOperationException("Direct3DCreate9 returned null.");

                IntPtr d3dVtable = Marshal.ReadIntPtr(_ownedD3D9);
                IntPtr createDevicePointer = Marshal.ReadIntPtr(
                    d3dVtable,
                    16 * IntPtr.Size);

                CreateDeviceDelegate createDevice =
                    (CreateDeviceDelegate)Marshal.GetDelegateForFunctionPointer(
                        createDevicePointer,
                        typeof(CreateDeviceDelegate));

                D3DPresentParameters pp = new D3DPresentParameters();
                pp.BackBufferWidth = 1;
                pp.BackBufferHeight = 1;
                pp.BackBufferFormat = 0; // D3DFMT_UNKNOWN for windowed mode
                pp.BackBufferCount = 1;
                pp.MultiSampleType = 0;
                pp.MultiSampleQuality = 0;
                pp.SwapEffect = D3DSwapEffectDiscard;
                pp.DeviceWindow = _dummyWindow;
                pp.Windowed = 1;
                pp.EnableAutoDepthStencil = 0;
                pp.AutoDepthStencilFormat = 0;
                pp.Flags = 0;
                pp.FullScreenRefreshRateInHz = 0;
                pp.PresentationInterval = 0;

                int hr = createDevice(
                    _ownedD3D9,
                    D3DAdapterDefault,
                    D3DDevTypeHal,
                    _dummyWindow,
                    D3DCreateSoftwareVertexProcessing,
                    ref pp,
                    out _ownedDevice);

                if (hr < 0 || _ownedDevice == IntPtr.Zero)
                    Marshal.ThrowExceptionForHR(hr);

                InstallFromDevicePointer(_ownedDevice);
            }
            catch
            {
                CleanupOwnedDevice();
                throw;
            }
        }

        private void InstallFromDevicePointer(IntPtr device)
        {
            IntPtr vtable = Marshal.ReadIntPtr(device);
            if (vtable == IntPtr.Zero)
                throw new InvalidOperationException("Direct3D device vtable pointer is null.");

            _vtableEntry = IntPtr.Add(
                vtable,
                SetLightVtableIndex * IntPtr.Size);

            _originalFunction = Marshal.ReadIntPtr(_vtableEntry);
            if (_originalFunction == IntPtr.Zero)
                throw new InvalidOperationException("IDirect3DDevice9::SetLight pointer is null.");

            _originalSetLight =
                (SetLightDelegate)Marshal.GetDelegateForFunctionPointer(
                    _originalFunction,
                    typeof(SetLightDelegate));

            _replacementSetLight = new SetLightDelegate(HookedSetLight);
            _replacementFunction =
                Marshal.GetFunctionPointerForDelegate(_replacementSetLight);

            WriteFunctionPointer(_vtableEntry, _replacementFunction);
            _installed = true;
        }

        public void SetLevel(bool enabled, int level)
        {
            if (level < 0)
                level = 0;
            if (level > 25)
                level = 25;

            _level = level;
            _enabled = enabled && level > 0;
        }

        public void Uninstall()
        {
            try
            {
                if (_installed &&
                    _vtableEntry != IntPtr.Zero &&
                    _originalFunction != IntPtr.Zero)
                {
                    // Restore only if our function is still installed. If another
                    // component has replaced the slot after us, do not overwrite it.
                    IntPtr current = Marshal.ReadIntPtr(_vtableEntry);
                    if (current == _replacementFunction)
                        WriteFunctionPointer(_vtableEntry, _originalFunction);
                }
            }
            finally
            {
                _installed = false;
                _vtableEntry = IntPtr.Zero;
                _originalFunction = IntPtr.Zero;
                _replacementFunction = IntPtr.Zero;
                _originalSetLight = null;
                _replacementSetLight = null;

                CleanupOwnedDevice();
            }
        }

        private void CleanupOwnedDevice()
        {
            if (_ownedDevice != IntPtr.Zero)
            {
                try { ReleaseComPointer(_ownedDevice); } catch { }
                _ownedDevice = IntPtr.Zero;
            }

            if (_ownedD3D9 != IntPtr.Zero)
            {
                try { ReleaseComPointer(_ownedD3D9); } catch { }
                _ownedD3D9 = IntPtr.Zero;
            }

            if (_dummyWindow != IntPtr.Zero)
            {
                try { DestroyWindow(_dummyWindow); } catch { }
                _dummyWindow = IntPtr.Zero;
            }
        }

        private static void ReleaseComPointer(IntPtr unknown)
        {
            IntPtr vtable = Marshal.ReadIntPtr(unknown);
            IntPtr releasePointer = Marshal.ReadIntPtr(
                vtable,
                2 * IntPtr.Size);

            ReleaseDelegate release =
                (ReleaseDelegate)Marshal.GetDelegateForFunctionPointer(
                    releasePointer,
                    typeof(ReleaseDelegate));

            release(unknown);
        }

        private int HookedSetLight(
            IntPtr device,
            uint index,
            ref D3DLight9 light)
        {
            SetLightDelegate original = _originalSetLight;
            if (original == null)
                return unchecked((int)0x80004005); // E_FAIL

            try
            {
                if (!_enabled || _level <= 0)
                    return original(device, index, ref light);

                D3DLight9 adjusted = light;
                float amount = _level / 25.0f;

                // Brighten the light colours by blending them toward white.
                // This preserves the source light's hue at lower levels and
                // avoids changing geometry, UI rendering, textures or desktop gamma.
                Brighten(ref adjusted.Diffuse, amount);
                Brighten(ref adjusted.Ambient, amount);

                return original(device, index, ref adjusted);
            }
            catch
            {
                // Never allow an exception to escape through the unmanaged D3D call.
                return original(device, index, ref light);
            }
        }

        private static void Brighten(ref D3DColorValue color, float amount)
        {
            color.R = BlendToWhite(color.R, amount);
            color.G = BlendToWhite(color.G, amount);
            color.B = BlendToWhite(color.B, amount);
        }

        private static float BlendToWhite(float value, float amount)
        {
            if (value < 0.0f)
                value = 0.0f;
            if (value > 1.0f)
                value = 1.0f;

            float result = value + ((1.0f - value) * amount);

            if (result < 0.0f)
                return 0.0f;
            if (result > 1.0f)
                return 1.0f;

            return result;
        }

        private static void WriteFunctionPointer(IntPtr address, IntPtr value)
        {
            uint oldProtect;
            if (!VirtualProtect(
                address,
                (UIntPtr)IntPtr.Size,
                PageExecuteReadWrite,
                out oldProtect))
            {
                throw new System.ComponentModel.Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "VirtualProtect failed while installing the Direct3D light hook.");
            }

            try
            {
                Marshal.WriteIntPtr(address, value);
            }
            finally
            {
                uint ignored;
                VirtualProtect(
                    address,
                    (UIntPtr)IntPtr.Size,
                    oldProtect,
                    out ignored);
            }
        }

        public void Dispose()
        {
            Uninstall();
        }
    }
}

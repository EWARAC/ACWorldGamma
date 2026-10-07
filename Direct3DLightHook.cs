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

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtect(
            IntPtr lpAddress,
            UIntPtr dwSize,
            uint flNewProtect,
            out uint lpflOldProtect);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetLightDelegate(
            IntPtr device,
            uint index,
            ref D3DLight9 light);

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

                IntPtr vtable = Marshal.ReadIntPtr(unknown);
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
            finally
            {
                if (unknown != IntPtr.Zero)
                {
                    try { Marshal.Release(unknown); } catch { }
                }
            }
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
            if (!_installed)
                return;

            try
            {
                if (_vtableEntry != IntPtr.Zero &&
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
            }
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

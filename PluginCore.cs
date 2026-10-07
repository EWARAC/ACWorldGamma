using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Decal.Adapter;
using Decal.Interop.Core;
using Decal.Interop.Inject;

namespace ACWorldGamma
{
    [FriendlyName("AC World Gamma")]
    [Guid("A9D7C4AA-2A2E-4D2D-9F83-7B728C37E8D4")]
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    [ComDefaultInterface(typeof(IRender3DSink))]
    public sealed class PluginCore : PluginBase, IRender3DSink
    {
        private const string Version = "0.4.0";
        private static readonly Guid IidInjectService =
            new Guid("47761792-2520-4802-8548-5CA580697614");
        private static readonly Guid IidDirect3DDevice9 =
            new Guid("D0223B96-BF7A-43FD-92BD-A43B0D82B9EB");

        private IInjectService _injectService;
        private bool _registered;
        private bool _preUiSubscribed;
        private bool _enabled;
        private int _level = 1;
        private string _renderError = "";

        private readonly object _renderLock = new object();
        private IntPtr _device9 = IntPtr.Zero;
        private IntPtr _quadMemory = IntPtr.Zero;

        private GetViewportDelegate _getViewport;
        private CreateStateBlockDelegate _createStateBlock;
        private SetRenderStateDelegate _setRenderState;
        private SetTextureDelegate _setTexture;
        private SetTextureStageStateDelegate _setTextureStageState;
        private DrawPrimitiveUPDelegate _drawPrimitiveUP;
        private SetFVFDelegate _setFVF;

        private string SettingsDirectory
        {
            get
            {
                return System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    @"Decal Plugins\AC World Gamma");
            }
        }

        private string SettingsFile
        {
            get { return System.IO.Path.Combine(SettingsDirectory, "Settings.txt"); }
        }

        protected override void Startup()
        {
            try
            {
                CoreManager.Current.CommandLineText += Current_CommandLineText;

                _quadMemory = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(OverlayQuad)));

                RegisterRenderSink();
                if (!_registered)
                {
                    Chat("v" + Version + " could not register the Decal render sink.");
                    return;
                }

                Host.Underlying.Hooks.RenderPreUI += Hooks_RenderPreUI;
                _preUiSubscribed = true;

                LoadSettings();
                if (_enabled)
                    NormalizeSetting();

                Chat("v" + Version + " loaded. " + StatusText());
            }
            catch (Exception ex)
            {
                Fail("Startup", ex);
            }
        }

        protected override void Shutdown()
        {
            try
            {
                if (CoreManager.Current != null)
                    CoreManager.Current.CommandLineText -= Current_CommandLineText;
            }
            catch { }

            try
            {
                if (_preUiSubscribed &&
                    Host != null &&
                    Host.Underlying != null &&
                    Host.Underlying.Hooks != null)
                {
                    Host.Underlying.Hooks.RenderPreUI -= Hooks_RenderPreUI;
                }
            }
            catch { }

            _preUiSubscribed = false;
            _registered = false;
            _injectService = null;

            lock (_renderLock)
            {
                ReleaseDeviceLocked();

                if (_quadMemory != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_quadMemory);
                    _quadMemory = IntPtr.Zero;
                }
            }
        }

        private void RegisterRenderSink()
        {
            object serviceObject = Host.Decal.GetObject(
                @"services\DecalPlugins.InjectService",
                IidInjectService);

            if (serviceObject == null)
                throw new InvalidOperationException("Decal InjectService returned null.");

            _injectService = serviceObject as IInjectService;
            if (_injectService == null)
                throw new InvalidCastException(
                    "Decal InjectService does not expose IInjectService.");

            _injectService.InitPlugin(this);
            _registered = true;
        }

        public void PreBeginScene(object direct3D)
        {
            CaptureDevice(direct3D);
        }

        public void PostBeginScene(object direct3D)
        {
        }

        public void PreEndScene(object direct3D)
        {
        }

        public void PostEndScene(object direct3D)
        {
        }

        private void CaptureDevice(object direct3D)
        {
            if (direct3D == null)
                return;

            IntPtr pUnk = IntPtr.Zero;
            IntPtr pDevice9 = IntPtr.Zero;

            try
            {
                pUnk = Marshal.GetIUnknownForObject(direct3D);
                Guid iidDevice9 = IidDirect3DDevice9;
                int hr = Marshal.QueryInterface(
                    pUnk, ref iidDevice9, out pDevice9);

                if (hr != 0 || pDevice9 == IntPtr.Zero)
                    return;

                lock (_renderLock)
                {
                    if (_device9 == pDevice9)
                    {
                        return;
                    }

                    ReleaseDeviceLocked();

                    _device9 = pDevice9;
                    pDevice9 = IntPtr.Zero;
                    CacheDeviceMethodsLocked();
                    _renderError = "";
                }
            }
            catch
            {
                lock (_renderLock)
                {
                    ReleaseDeviceLocked();
                }
            }
            finally
            {
                if (pDevice9 != IntPtr.Zero)
                    Marshal.Release(pDevice9);

                if (pUnk != IntPtr.Zero)
                    Marshal.Release(pUnk);
            }
        }

        private void CacheDeviceMethodsLocked()
        {
            IntPtr vtable = Marshal.ReadIntPtr(_device9);

            _getViewport = GetDelegate<GetViewportDelegate>(vtable, 48);
            _setRenderState = GetDelegate<SetRenderStateDelegate>(vtable, 57);
            _createStateBlock = GetDelegate<CreateStateBlockDelegate>(vtable, 59);
            _setTexture = GetDelegate<SetTextureDelegate>(vtable, 65);
            _setTextureStageState = GetDelegate<SetTextureStageStateDelegate>(vtable, 67);
            _drawPrimitiveUP = GetDelegate<DrawPrimitiveUPDelegate>(vtable, 83);
            _setFVF = GetDelegate<SetFVFDelegate>(vtable, 89);
        }

        private static T GetDelegate<T>(IntPtr vtable, int slot) where T : class
        {
            IntPtr function = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
            return Marshal.GetDelegateForFunctionPointer(
                function, typeof(T)) as T;
        }

        private void ReleaseDeviceLocked()
        {
            if (_device9 != IntPtr.Zero)
            {
                Marshal.Release(_device9);
                _device9 = IntPtr.Zero;
            }

            _getViewport = null;
            _createStateBlock = null;
            _setRenderState = null;
            _setTexture = null;
            _setTextureStageState = null;
            _drawPrimitiveUP = null;
            _setFVF = null;
        }

        private void Hooks_RenderPreUI()
        {
            if (!_enabled || _level <= 0)
                return;

            DrawBrightnessOverlay();
        }

        private void DrawBrightnessOverlay()
        {
            lock (_renderLock)
            {
                if (_device9 == IntPtr.Zero ||
                    _quadMemory == IntPtr.Zero ||
                    _getViewport == null ||
                    _setRenderState == null ||
                    _setTexture == null ||
                    _setTextureStageState == null ||
                    _drawPrimitiveUP == null ||
                    _setFVF == null ||
                    _createStateBlock == null)
                {
                    return;
                }

                IntPtr stateBlock = IntPtr.Zero;

                try
                {
                    D3DViewport9 viewport;
                    int hr = _getViewport(_device9, out viewport);
                    if (hr != 0 || viewport.Width == 0 || viewport.Height == 0)
                        return;

                    hr = _createStateBlock(_device9, 1, out stateBlock); // D3DSBT_ALL
                    if (hr != 0 || stateBlock == IntPtr.Zero)
                        return;

                    _setTexture(_device9, 0, IntPtr.Zero);

                    _setRenderState(_device9, 7, 0);    // ZENABLE
                    _setRenderState(_device9, 14, 0);   // ZWRITEENABLE
                    _setRenderState(_device9, 15, 0);   // ALPHATESTENABLE
                    _setRenderState(_device9, 19, 5);   // SRCBLEND = SRCALPHA
                    _setRenderState(_device9, 20, 6);   // DESTBLEND = INVSRCALPHA
                    _setRenderState(_device9, 22, 1);   // CULLMODE = NONE
                    _setRenderState(_device9, 27, 1);   // ALPHABLENDENABLE
                    _setRenderState(_device9, 28, 0);   // FOGENABLE
                    _setRenderState(_device9, 52, 0);   // STENCILENABLE
                    _setRenderState(_device9, 137, 0);  // LIGHTING
                    _setRenderState(_device9, 141, 1);  // COLORVERTEX

                    _setTextureStageState(_device9, 0, 1, 2); // COLOROP = SELECTARG1
                    _setTextureStageState(_device9, 0, 2, 0); // COLORARG1 = DIFFUSE
                    _setTextureStageState(_device9, 0, 4, 2); // ALPHAOP = SELECTARG1
                    _setTextureStageState(_device9, 0, 5, 0); // ALPHAARG1 = DIFFUSE
                    _setTextureStageState(_device9, 1, 1, 1); // stage 1 COLOROP = DISABLE

                    const uint D3DFVF_XYZRHW_DIFFUSE = 0x00000044;
                    _setFVF(_device9, D3DFVF_XYZRHW_DIFFUSE);

                    uint alpha = (uint)((_level * 255 + 50) / 100);
                    uint color = (alpha << 24) | 0x00FFFFFFu;

                    float left = viewport.X - 0.5f;
                    float top = viewport.Y - 0.5f;
                    float right = viewport.X + viewport.Width - 0.5f;
                    float bottom = viewport.Y + viewport.Height - 0.5f;

                    OverlayQuad quad = new OverlayQuad(
                        new OverlayVertex(left,  top,    color),
                        new OverlayVertex(right, top,    color),
                        new OverlayVertex(left,  bottom, color),
                        new OverlayVertex(right, bottom, color));

                    Marshal.StructureToPtr(quad, _quadMemory, false);

                    hr = _drawPrimitiveUP(
                        _device9,
                        5, // D3DPT_TRIANGLESTRIP
                        2,
                        _quadMemory,
                        (uint)Marshal.SizeOf(typeof(OverlayVertex)));

                    if (hr == 0)
                        _renderError = "";
                    else
                        _renderError = "DrawPrimitiveUP HRESULT 0x" + hr.ToString("X8");
                }
                catch (Exception ex)
                {
                    _renderError = ex.GetType().Name + ": " + ex.Message;
                }
                finally
                {
                    if (stateBlock != IntPtr.Zero)
                    {
                        try
                        {
                            IntPtr sbVtable = Marshal.ReadIntPtr(stateBlock);
                            StateBlockApplyDelegate apply =
                                GetDelegate<StateBlockApplyDelegate>(sbVtable, 5);

                            if (apply != null)
                                apply(stateBlock);
                        }
                        catch { }

                        try { Marshal.Release(stateBlock); } catch { }
                    }
                }
            }
        }

        private void Current_CommandLineText(object sender, ChatParserInterceptEventArgs e)
        {
            try
            {
                if (e == null || String.IsNullOrWhiteSpace(e.Text))
                    return;

                string raw = e.Text.Trim();

                if (!raw.Equals("/acgamma", StringComparison.OrdinalIgnoreCase) &&
                    !raw.StartsWith("/acgamma ", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                e.Eat = true;
                string rest = raw.Length > 8 ? raw.Substring(8).Trim() : "";

                if (rest.Length == 0 ||
                    String.Equals(rest, "help", StringComparison.OrdinalIgnoreCase))
                {
                    Chat("Commands: /acgamma on | off | 0-25 | up | down | reset | status");
                    return;
                }

                if (String.Equals(rest, "status", StringComparison.OrdinalIgnoreCase))
                {
                    Chat(StatusText());
                    return;
                }

                if (String.Equals(rest, "on", StringComparison.OrdinalIgnoreCase))
                {
                    _enabled = true;
                    NormalizeSetting();
                    SaveSettings();
                    Chat(StatusText());
                    return;
                }

                if (String.Equals(rest, "off", StringComparison.OrdinalIgnoreCase))
                {
                    _enabled = false;
                    SaveSettings();
                    Chat(StatusText());
                    return;
                }

                if (String.Equals(rest, "reset", StringComparison.OrdinalIgnoreCase))
                {
                    _enabled = false;
                    _level = 1;
                    SaveSettings();
                    Chat("reset to normal world lighting.");
                    return;
                }

                if (String.Equals(rest, "up", StringComparison.OrdinalIgnoreCase))
                {
                    _level = Math.Min(25, _level + 1);
                    _enabled = true;
                    SaveSettings();
                    Chat(StatusText());
                    return;
                }

                if (String.Equals(rest, "down", StringComparison.OrdinalIgnoreCase))
                {
                    _level = Math.Max(0, _level - 1);
                    _enabled = _level > 0;
                    SaveSettings();
                    Chat(StatusText());
                    return;
                }

                int requested;
                if (Int32.TryParse(
                    rest,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out requested))
                {
                    if (requested < 0 || requested > 25)
                    {
                        Chat("level must be from 0 to 25.");
                        return;
                    }

                    _level = requested;
                    _enabled = requested > 0;
                    SaveSettings();
                    Chat(StatusText());
                    return;
                }

                Chat("Commands: /acgamma on | off | 0-25 | up | down | reset | status");
            }
            catch (Exception ex)
            {
                Fail("Command", ex);
            }
        }

        private void NormalizeSetting()
        {
            if (_level < 1)
                _level = 1;
            if (_level > 25)
                _level = 25;
        }

        private string StatusText()
        {
            if (!_registered)
                return "render service unavailable.";

            if (!_enabled || _level <= 0)
                return "OFF (normal AC world lighting).";

            string text = "ON, world brightness level " +
                _level.ToString(CultureInfo.InvariantCulture) + " of 25.";

            if (!String.IsNullOrEmpty(_renderError))
                text += " Last render error: " + _renderError;

            return text;
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsFile))
                    return;

                string[] lines = File.ReadAllLines(SettingsFile);

                if (lines.Length >= 1)
                {
                    bool enabled;
                    if (Boolean.TryParse(lines[0], out enabled))
                        _enabled = enabled;
                }

                if (lines.Length >= 2)
                {
                    int level;
                    if (Int32.TryParse(
                        lines[1],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out level))
                    {
                        _level = Math.Max(0, Math.Min(25, level));
                    }
                }
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(SettingsDirectory);
                File.WriteAllLines(SettingsFile, new string[]
                {
                    _enabled.ToString(),
                    _level.ToString(CultureInfo.InvariantCulture)
                });
            }
            catch { }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3DViewport9
        {
            public uint X;
            public uint Y;
            public uint Width;
            public uint Height;
            public float MinZ;
            public float MaxZ;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct OverlayVertex
        {
            public float X;
            public float Y;
            public float Z;
            public float Rhw;
            public uint Color;

            public OverlayVertex(float x, float y, uint color)
            {
                X = x;
                Y = y;
                Z = 0.0f;
                Rhw = 1.0f;
                Color = color;
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct OverlayQuad
        {
            public OverlayVertex V0;
            public OverlayVertex V1;
            public OverlayVertex V2;
            public OverlayVertex V3;

            public OverlayQuad(
                OverlayVertex v0,
                OverlayVertex v1,
                OverlayVertex v2,
                OverlayVertex v3)
            {
                V0 = v0;
                V1 = v1;
                V2 = v2;
                V3 = v3;
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetViewportDelegate(
            IntPtr device, out D3DViewport9 viewport);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateStateBlockDelegate(
            IntPtr device, int type, out IntPtr stateBlock);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetRenderStateDelegate(
            IntPtr device, int state, uint value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetTextureDelegate(
            IntPtr device, uint stage, IntPtr texture);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetTextureStageStateDelegate(
            IntPtr device, uint stage, int type, uint value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int DrawPrimitiveUPDelegate(
            IntPtr device,
            int primitiveType,
            uint primitiveCount,
            IntPtr vertexData,
            uint vertexStride);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetFVFDelegate(
            IntPtr device, uint fvf);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int StateBlockApplyDelegate(
            IntPtr stateBlock);

        private static void Chat(string text)
        {
            try
            {
                CoreManager.Current.Actions.AddChatText(
                    "[AC Gamma] " + text, 5);
            }
            catch { }
        }

        private static void Fail(string where, Exception ex)
        {
            Chat(where + " error: " + ex.Message);
        }
    }
}

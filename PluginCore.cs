using System;
using System.Runtime.InteropServices;
using System.Threading;
using Decal.Adapter;
using Decal.Interop.Core;
using Decal.Interop.Inject;

namespace ACWorldGamma
{
    [FriendlyName("AC World Gamma Render Sink Diagnostic")]
    [Guid("A9D7C4AA-2A2E-4D2D-9F83-7B728C37E8D4")]
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    [ComDefaultInterface(typeof(IRender3DSink))]
    public sealed class PluginCore : PluginBase, IRender3DSink
    {
        private const string Version = "0.4.0-alpha5";

        private IInjectService _injectService;
        private bool _registered;
        private string _registrationError = "";

        private long _preBeginCount;
        private long _postBeginCount;
        private long _preEndCount;
        private long _postEndCount;

        private readonly object _deviceLock = new object();
        private string _deviceRuntimeType = "(none)";
        private string _deviceIUnknown = "(none)";
        private string _device9Query = "(not queried)";
        private string _postBeginAmbient = "(not read)";
        private string _postBeginLighting = "(not read)";
        private string _preEndAmbient = "(not read)";
        private string _preEndLighting = "(not read)";
        private string _postBeginLight0 = "(not sampled)";
        private string _preEndLight0 = "(not sampled)";
        private volatile bool _sampleLightRequested = true;
        private volatile bool _postBeginLightCaptured;
        private long _renderPreUICount;

        private readonly object _renderDeviceLock = new object();
        private IntPtr _renderDevice9 = IntPtr.Zero;
        private volatile bool _overlayEnabled;
        private int _overlayPercent = 12;
        private string _overlayStatus = "OFF";

        protected override void Startup()
        {
            try
            {
                CoreManager.Current.CommandLineText += Current_CommandLineText;
                Host.Underlying.Hooks.RenderPreUI += Hooks_RenderPreUI;
                RegisterRenderSink();

                if (_registered)
                {
                    Chat("v" + Version + " render-sink diagnostic loaded. No lighting changes are made.");
                }
                else
                {
                    Chat("v" + Version + " diagnostic could not register: " + _registrationError);
                }
            }
            catch (Exception ex)
            {
                _registrationError = ex.GetType().Name + ": " + ex.Message;
                Chat("Startup error: " + _registrationError);
            }
        }

        protected override void Shutdown()
        {
            try
            {
                if (CoreManager.Current != null)
                    CoreManager.Current.CommandLineText -= Current_CommandLineText;

                if (Host != null && Host.Underlying != null && Host.Underlying.Hooks != null)
                    Host.Underlying.Hooks.RenderPreUI -= Hooks_RenderPreUI;
            }
            catch { }

            // Decal owns the InjectService lifetime and plugin teardown.
            // Do not manually ReleaseComObject here; just drop our managed reference.
            _injectService = null;
            _registered = false;

            lock (_renderDeviceLock)
            {
                if (_renderDevice9 != IntPtr.Zero)
                {
                    Marshal.Release(_renderDevice9);
                    _renderDevice9 = IntPtr.Zero;
                }
            }
        }

        private void RegisterRenderSink()
        {
            try
            {
                _registrationError = "";

                object serviceObject = Host.Decal.GetObject(
                    @"services\DecalPlugins.InjectService",
                    new Guid("47761792-2520-4802-8548-5CA580697614"));
                if (serviceObject == null)
                    throw new InvalidOperationException("Decal InjectService returned null.");

                _injectService = serviceObject as IInjectService;
                if (_injectService == null)
                    throw new InvalidCastException(
                        "InjectService does not expose Decal.Interop.Core.IInjectService.");

                _injectService.InitPlugin(this);
                _registered = true;
            }
            catch (Exception ex)
            {
                _registered = false;
                _injectService = null;
                _registrationError = ex.GetType().Name + ": " + ex.Message;
            }
        }

        public void PreBeginScene(object direct3D)
        {
            Interlocked.Increment(ref _preBeginCount);
            ObserveDevice(direct3D);
            CaptureRenderDevice(direct3D);
        }

        public void PostBeginScene(object direct3D)
        {
            Interlocked.Increment(ref _postBeginCount);
            ObserveDevice(direct3D);
            ReadRenderStatesOnce(direct3D, true);
            if (_sampleLightRequested && !_postBeginLightCaptured)
            {
                _postBeginLight0 = ReadLight0(direct3D);
                _postBeginLightCaptured = true;
            }
        }

        public void PreEndScene(object direct3D)
        {
            Interlocked.Increment(ref _preEndCount);
            ObserveDevice(direct3D);
            ReadRenderStatesOnce(direct3D, false);
            if (_sampleLightRequested && _postBeginLightCaptured)
            {
                _preEndLight0 = ReadLight0(direct3D);
                _sampleLightRequested = false;
                _postBeginLightCaptured = false;
            }
        }

        public void PostEndScene(object direct3D)
        {
            Interlocked.Increment(ref _postEndCount);
            ObserveDevice(direct3D);
        }

        private void ObserveDevice(object direct3D)
        {
            if (direct3D == null)
                return;

            lock (_deviceLock)
            {
                if (_deviceRuntimeType != "(none)")
                    return;

                try
                {
                    _deviceRuntimeType = direct3D.GetType().FullName ?? direct3D.GetType().Name;

                    IntPtr pUnk = IntPtr.Zero;
                    IntPtr pDevice9 = IntPtr.Zero;
                    try
                    {
                        pUnk = Marshal.GetIUnknownForObject(direct3D);
                        if (pUnk != IntPtr.Zero)
                        {
                            _deviceIUnknown = "0x" + pUnk.ToInt64().ToString("X");

                            Guid iidDevice9 = new Guid("D0223B96-BF7A-43FD-92BD-A43B0D82B9EB");
                            int hr = Marshal.QueryInterface(pUnk, ref iidDevice9, out pDevice9);

                            if (hr == 0 && pDevice9 != IntPtr.Zero)
                                _device9Query = "SUCCESS, ptr=0x" + pDevice9.ToInt64().ToString("X");
                            else
                                _device9Query = "FAILED, HRESULT=0x" + hr.ToString("X8");
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
                catch (Exception ex)
                {
                    _deviceRuntimeType = "ERROR: " + ex.GetType().Name + ": " + ex.Message;
                }
            }
        }

        private void Hooks_RenderPreUI()
        {
            Interlocked.Increment(ref _renderPreUICount);

            if (_overlayEnabled)
                DrawBrightnessOverlay();
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

            public OverlayVertex(float x, float y, float z, float rhw, uint color)
            {
                X = x;
                Y = y;
                Z = z;
                Rhw = rhw;
                Color = color;
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetViewportDelegate(IntPtr device, out D3DViewport9 viewport);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateStateBlockDelegate(IntPtr device, int type, out IntPtr stateBlock);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetRenderStateDelegate(IntPtr device, int state, uint value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetTextureDelegate(IntPtr device, uint stage, IntPtr texture);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetTextureStageStateDelegate(IntPtr device, uint stage, int type, uint value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int DrawPrimitiveUPDelegate(
            IntPtr device, int primitiveType, uint primitiveCount,
            IntPtr vertexData, uint vertexStride);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetFVFDelegate(IntPtr device, uint fvf);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int StateBlockApplyDelegate(IntPtr stateBlock);

        private void CaptureRenderDevice(object direct3D)
        {
            if (direct3D == null)
                return;

            IntPtr pUnk = IntPtr.Zero;
            IntPtr pDevice9 = IntPtr.Zero;

            try
            {
                pUnk = Marshal.GetIUnknownForObject(direct3D);
                Guid iidDevice9 = new Guid("D0223B96-BF7A-43FD-92BD-A43B0D82B9EB");
                int hr = Marshal.QueryInterface(pUnk, ref iidDevice9, out pDevice9);

                if (hr != 0 || pDevice9 == IntPtr.Zero)
                    return;

                lock (_renderDeviceLock)
                {
                    if (_renderDevice9 == pDevice9)
                    {
                        Marshal.Release(pDevice9);
                        pDevice9 = IntPtr.Zero;
                    }
                    else
                    {
                        if (_renderDevice9 != IntPtr.Zero)
                            Marshal.Release(_renderDevice9);

                        _renderDevice9 = pDevice9;
                        pDevice9 = IntPtr.Zero;
                    }
                }
            }
            catch
            {
            }
            finally
            {
                if (pDevice9 != IntPtr.Zero)
                    Marshal.Release(pDevice9);
                if (pUnk != IntPtr.Zero)
                    Marshal.Release(pUnk);
            }
        }

        private void DrawBrightnessOverlay()
        {
            IntPtr device = IntPtr.Zero;
            IntPtr stateBlock = IntPtr.Zero;
            GCHandle pinned = new GCHandle();

            try
            {
                lock (_renderDeviceLock)
                {
                    if (_renderDevice9 == IntPtr.Zero)
                    {
                        _overlayStatus = "waiting for D3D9 device";
                        return;
                    }

                    device = _renderDevice9;
                    Marshal.AddRef(device);
                }

                IntPtr vtable = Marshal.ReadIntPtr(device);

                GetViewportDelegate getViewport =
                    (GetViewportDelegate)Marshal.GetDelegateForFunctionPointer(
                        Marshal.ReadIntPtr(vtable, 48 * IntPtr.Size), typeof(GetViewportDelegate));
                CreateStateBlockDelegate createStateBlock =
                    (CreateStateBlockDelegate)Marshal.GetDelegateForFunctionPointer(
                        Marshal.ReadIntPtr(vtable, 59 * IntPtr.Size), typeof(CreateStateBlockDelegate));
                SetRenderStateDelegate setRenderState =
                    (SetRenderStateDelegate)Marshal.GetDelegateForFunctionPointer(
                        Marshal.ReadIntPtr(vtable, 57 * IntPtr.Size), typeof(SetRenderStateDelegate));
                SetTextureDelegate setTexture =
                    (SetTextureDelegate)Marshal.GetDelegateForFunctionPointer(
                        Marshal.ReadIntPtr(vtable, 65 * IntPtr.Size), typeof(SetTextureDelegate));
                SetTextureStageStateDelegate setTextureStageState =
                    (SetTextureStageStateDelegate)Marshal.GetDelegateForFunctionPointer(
                        Marshal.ReadIntPtr(vtable, 67 * IntPtr.Size), typeof(SetTextureStageStateDelegate));
                DrawPrimitiveUPDelegate drawPrimitiveUP =
                    (DrawPrimitiveUPDelegate)Marshal.GetDelegateForFunctionPointer(
                        Marshal.ReadIntPtr(vtable, 83 * IntPtr.Size), typeof(DrawPrimitiveUPDelegate));
                SetFVFDelegate setFVF =
                    (SetFVFDelegate)Marshal.GetDelegateForFunctionPointer(
                        Marshal.ReadIntPtr(vtable, 89 * IntPtr.Size), typeof(SetFVFDelegate));

                D3DViewport9 viewport;
                int hr = getViewport(device, out viewport);
                if (hr != 0 || viewport.Width == 0 || viewport.Height == 0)
                {
                    _overlayStatus = "GetViewport failed 0x" + hr.ToString("X8");
                    return;
                }

                hr = createStateBlock(device, 1, out stateBlock); // D3DSBT_ALL
                if (hr != 0 || stateBlock == IntPtr.Zero)
                {
                    _overlayStatus = "CreateStateBlock failed 0x" + hr.ToString("X8");
                    return;
                }

                setTexture(device, 0, IntPtr.Zero);
                setRenderState(device, 7, 0);    // D3DRS_ZENABLE
                setRenderState(device, 14, 0);   // D3DRS_ZWRITEENABLE
                setRenderState(device, 15, 0);   // D3DRS_ALPHATESTENABLE
                setRenderState(device, 19, 5);   // D3DRS_SRCBLEND = SRCALPHA
                setRenderState(device, 20, 6);   // D3DRS_DESTBLEND = INVSRCALPHA
                setRenderState(device, 22, 1);   // D3DRS_CULLMODE = NONE
                setRenderState(device, 27, 1);   // D3DRS_ALPHABLENDENABLE
                setRenderState(device, 28, 0);   // D3DRS_FOGENABLE
                setRenderState(device, 52, 0);   // D3DRS_STENCILENABLE
                setRenderState(device, 137, 0);  // D3DRS_LIGHTING
                setRenderState(device, 141, 1);  // D3DRS_COLORVERTEX

                setTextureStageState(device, 0, 1, 2); // COLOROP = SELECTARG1
                setTextureStageState(device, 0, 2, 0); // COLORARG1 = DIFFUSE
                setTextureStageState(device, 0, 4, 2); // ALPHAOP = SELECTARG1
                setTextureStageState(device, 0, 5, 0); // ALPHAARG1 = DIFFUSE
                setTextureStageState(device, 1, 1, 1); // stage 1 COLOROP = DISABLE

                const uint D3DFVF_XYZRHW_DIFFUSE = 0x00000044;
                setFVF(device, D3DFVF_XYZRHW_DIFFUSE);

                int pct = _overlayPercent;
                if (pct < 0) pct = 0;
                if (pct > 100) pct = 100;
                uint alpha = (uint)((pct * 255 + 50) / 100);
                uint color = (alpha << 24) | 0x00FFFFFFu;

                float left = viewport.X - 0.5f;
                float top = viewport.Y - 0.5f;
                float right = viewport.X + viewport.Width - 0.5f;
                float bottom = viewport.Y + viewport.Height - 0.5f;

                OverlayVertex[] vertices = new OverlayVertex[]
                {
                    new OverlayVertex(left,  top,    0.0f, 1.0f, color),
                    new OverlayVertex(right, top,    0.0f, 1.0f, color),
                    new OverlayVertex(left,  bottom, 0.0f, 1.0f, color),
                    new OverlayVertex(right, bottom, 0.0f, 1.0f, color)
                };

                pinned = GCHandle.Alloc(vertices, GCHandleType.Pinned);
                hr = drawPrimitiveUP(
                    device, 5, 2, pinned.AddrOfPinnedObject(),
                    (uint)Marshal.SizeOf(typeof(OverlayVertex)));

                if (hr == 0)
                    _overlayStatus = "ON at " + pct + "%";
                else
                    _overlayStatus = "DrawPrimitiveUP failed 0x" + hr.ToString("X8");
            }
            catch (Exception ex)
            {
                _overlayStatus = ex.GetType().Name + ": " + ex.Message;
                _overlayEnabled = false;
            }
            finally
            {
                if (pinned.IsAllocated)
                    pinned.Free();

                if (stateBlock != IntPtr.Zero)
                {
                    try
                    {
                        IntPtr sbVtable = Marshal.ReadIntPtr(stateBlock);
                        StateBlockApplyDelegate apply =
                            (StateBlockApplyDelegate)Marshal.GetDelegateForFunctionPointer(
                                Marshal.ReadIntPtr(sbVtable, 5 * IntPtr.Size),
                                typeof(StateBlockApplyDelegate));
                        apply(stateBlock);
                    }
                    catch
                    {
                    }

                    Marshal.Release(stateBlock);
                }

                if (device != IntPtr.Zero)
                    Marshal.Release(device);
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetRenderStateDelegate(IntPtr device, int state, out uint value);

        private void ReadRenderStatesOnce(object direct3D, bool postBegin)
        {
            if (direct3D == null)
                return;

            lock (_deviceLock)
            {
                if (postBegin)
                {
                    if (_postBeginAmbient != "(not read)")
                        return;
                }
                else
                {
                    if (_preEndAmbient != "(not read)")
                        return;
                }

                IntPtr pUnk = IntPtr.Zero;
                IntPtr pDevice9 = IntPtr.Zero;

                try
                {
                    pUnk = Marshal.GetIUnknownForObject(direct3D);
                    Guid iidDevice9 = new Guid("D0223B96-BF7A-43FD-92BD-A43B0D82B9EB");

                    int hr = Marshal.QueryInterface(pUnk, ref iidDevice9, out pDevice9);
                    if (hr != 0 || pDevice9 == IntPtr.Zero)
                    {
                        string failure = "QI failed 0x" + hr.ToString("X8");
                        if (postBegin)
                        {
                            _postBeginAmbient = failure;
                            _postBeginLighting = failure;
                        }
                        else
                        {
                            _preEndAmbient = failure;
                            _preEndLighting = failure;
                        }
                        return;
                    }

                    IntPtr vtable = Marshal.ReadIntPtr(pDevice9);
                    IntPtr fn = Marshal.ReadIntPtr(vtable, 58 * IntPtr.Size);
                    GetRenderStateDelegate getRenderState =
                        (GetRenderStateDelegate)Marshal.GetDelegateForFunctionPointer(
                            fn, typeof(GetRenderStateDelegate));

                    uint ambient;
                    uint lighting;

                    int hrAmbient = getRenderState(pDevice9, 26, out ambient);   // D3DRS_AMBIENT
                    int hrLighting = getRenderState(pDevice9, 137, out lighting); // D3DRS_LIGHTING

                    string ambientText = hrAmbient == 0
                        ? "0x" + ambient.ToString("X8")
                        : "HRESULT 0x" + hrAmbient.ToString("X8");

                    string lightingText = hrLighting == 0
                        ? (lighting != 0 ? "ON (" + lighting + ")" : "OFF (0)")
                        : "HRESULT 0x" + hrLighting.ToString("X8");

                    if (postBegin)
                    {
                        _postBeginAmbient = ambientText;
                        _postBeginLighting = lightingText;
                    }
                    else
                    {
                        _preEndAmbient = ambientText;
                        _preEndLighting = lightingText;
                    }
                }
                catch (Exception ex)
                {
                    string failure = ex.GetType().Name + ": " + ex.Message;
                    if (postBegin)
                    {
                        _postBeginAmbient = failure;
                        _postBeginLighting = failure;
                    }
                    else
                    {
                        _preEndAmbient = failure;
                        _preEndLighting = failure;
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

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetLightDelegate(IntPtr device, uint index, out D3DLight9 light);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetLightEnableDelegate(IntPtr device, uint index, out int enabled);

        private string ReadLight0(object direct3D)
        {
            if (direct3D == null)
                return "callback object is null";

            IntPtr pUnk = IntPtr.Zero;
            IntPtr pDevice9 = IntPtr.Zero;

            try
            {
                pUnk = Marshal.GetIUnknownForObject(direct3D);
                Guid iidDevice9 = new Guid("D0223B96-BF7A-43FD-92BD-A43B0D82B9EB");
                int hr = Marshal.QueryInterface(pUnk, ref iidDevice9, out pDevice9);
                if (hr != 0 || pDevice9 == IntPtr.Zero)
                    return "QI failed 0x" + hr.ToString("X8");

                IntPtr vtable = Marshal.ReadIntPtr(pDevice9);
                IntPtr fnGetLight = Marshal.ReadIntPtr(vtable, 52 * IntPtr.Size);
                IntPtr fnGetLightEnable = Marshal.ReadIntPtr(vtable, 54 * IntPtr.Size);

                GetLightDelegate getLight =
                    (GetLightDelegate)Marshal.GetDelegateForFunctionPointer(
                        fnGetLight, typeof(GetLightDelegate));
                GetLightEnableDelegate getLightEnable =
                    (GetLightEnableDelegate)Marshal.GetDelegateForFunctionPointer(
                        fnGetLightEnable, typeof(GetLightEnableDelegate));

                D3DLight9 light;
                int enabled;
                int hrLight = getLight(pDevice9, 0, out light);
                int hrEnabled = getLightEnable(pDevice9, 0, out enabled);

                if (hrLight != 0)
                    return "GetLight(0) HRESULT=0x" + hrLight.ToString("X8") +
                           "; GetLightEnable HRESULT=0x" + hrEnabled.ToString("X8");

                string enabledText = hrEnabled == 0
                    ? (enabled != 0 ? "ON" : "OFF")
                    : "HRESULT 0x" + hrEnabled.ToString("X8");

                return "Type=" + light.Type +
                       ", Enabled=" + enabledText +
                       ", Ambient=(" + light.Ambient.R.ToString("0.###") +
                       "," + light.Ambient.G.ToString("0.###") +
                       "," + light.Ambient.B.ToString("0.###") +
                       "," + light.Ambient.A.ToString("0.###") + ")" +
                       ", Range=" + light.Range.ToString("0.###") +
                       ", Diffuse=(" + light.Diffuse.R.ToString("0.###") +
                       "," + light.Diffuse.G.ToString("0.###") +
                       "," + light.Diffuse.B.ToString("0.###") + ")";
            }
            catch (Exception ex)
            {
                return ex.GetType().Name + ": " + ex.Message;
            }
            finally
            {
                if (pDevice9 != IntPtr.Zero)
                    Marshal.Release(pDevice9);
                if (pUnk != IntPtr.Zero)
                    Marshal.Release(pUnk);
            }
        }

        private void RequestLightSample()
        {
            lock (_deviceLock)
            {
                _postBeginLight0 = "(pending)";
                _preEndLight0 = "(pending)";
                _postBeginLightCaptured = false;
                _sampleLightRequested = true;
            }
        }

        private void Current_CommandLineText(object sender, ChatParserInterceptEventArgs e)
        {
            try
            {
                if (e == null || String.IsNullOrWhiteSpace(e.Text))
                    return;

                string raw = e.Text.Trim();
                if (!raw.StartsWith("/acgamma", StringComparison.OrdinalIgnoreCase))
                    return;

                e.Eat = true;

                string rest = raw.Length > 8 ? raw.Substring(8).Trim() : "";

                if (rest.Length == 0 ||
                    String.Equals(rest, "help", StringComparison.OrdinalIgnoreCase))
                {
                    Chat("Diagnostic commands: /acgamma probe | sample | test on | test off | test 1-30");
                    return;
                }

                if (String.Equals(rest, "probe", StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(rest, "status", StringComparison.OrdinalIgnoreCase))
                {
                    ReportStatus();
                    return;
                }

                if (String.Equals(rest, "sample", StringComparison.OrdinalIgnoreCase))
                {
                    RequestLightSample();
                    Chat("Light 0 sample requested for the next rendered frame.");
                    return;
                }

                if (String.Equals(rest, "test on", StringComparison.OrdinalIgnoreCase))
                {
                    _overlayEnabled = true;
                    _overlayStatus = "enabled; waiting for RenderPreUI";
                    Chat("World-only pre-UI brightness test ON at " + _overlayPercent + "%.");
                    return;
                }

                if (String.Equals(rest, "test off", StringComparison.OrdinalIgnoreCase))
                {
                    _overlayEnabled = false;
                    _overlayStatus = "OFF";
                    Chat("World-only pre-UI brightness test OFF.");
                    return;
                }

                if (rest.StartsWith("test ", StringComparison.OrdinalIgnoreCase))
                {
                    int pct;
                    if (Int32.TryParse(rest.Substring(5).Trim(), out pct) && pct >= 1 && pct <= 30)
                    {
                        _overlayPercent = pct;
                        _overlayEnabled = true;
                        _overlayStatus = "enabled; waiting for RenderPreUI";
                        Chat("World-only pre-UI brightness test ON at " + pct + "%.");
                    }
                    else
                    {
                        Chat("Use /acgamma test 1-30, /acgamma test on, or /acgamma test off.");
                    }
                    return;
                }

                Chat("Use /acgamma probe, /acgamma sample, or /acgamma test on|off|1-30.");
            }
            catch (Exception ex)
            {
                Chat("Command error: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void ReportStatus()
        {
            Chat("Inject sink registered = " + _registered +
                 (_registered ? "" : "; error = " + _registrationError));

            Chat("Callbacks: PreBegin=" + Interlocked.Read(ref _preBeginCount) +
                 ", PostBegin=" + Interlocked.Read(ref _postBeginCount) +
                 ", RenderPreUI=" + Interlocked.Read(ref _renderPreUICount) +
                 ", PreEnd=" + Interlocked.Read(ref _preEndCount) +
                 ", PostEnd=" + Interlocked.Read(ref _postEndCount));

            lock (_deviceLock)
            {
                Chat("D3D callback object = " + _deviceRuntimeType +
                     "; IUnknown = " + _deviceIUnknown);
                Chat("IDirect3DDevice9 QueryInterface = " + _device9Query);
                Chat("PostBeginScene: AMBIENT=" + _postBeginAmbient +
                     ", LIGHTING=" + _postBeginLighting);
                Chat("PreEndScene: AMBIENT=" + _preEndAmbient +
                     ", LIGHTING=" + _preEndLighting);
                Chat("PostBeginScene Light 0: " + _postBeginLight0);
                Chat("PreEndScene Light 0: " + _preEndLight0);
                Chat("Pre-UI brightness test = " + _overlayStatus);
            }
        }

        private static void Chat(string text)
        {
            try
            {
                CoreManager.Current.Actions.AddChatText(
                    "[AC Gamma] " + text, 5);
            }
            catch { }
        }
    }
}

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
        private const string Version = "0.4.0-alpha3";

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
        private long _renderPreUICount;

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
        }

        public void PostBeginScene(object direct3D)
        {
            Interlocked.Increment(ref _postBeginCount);
            ObserveDevice(direct3D);
            ReadRenderStatesOnce(direct3D, true);
        }

        public void PreEndScene(object direct3D)
        {
            Interlocked.Increment(ref _preEndCount);
            ObserveDevice(direct3D);
            ReadRenderStatesOnce(direct3D, false);
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

        private void Hooks_RenderPreUI(object sender, EventArgs e)
        {
            Interlocked.Increment(ref _renderPreUICount);
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
                    Chat("Diagnostic commands: /acgamma probe | status");
                    return;
                }

                if (String.Equals(rest, "probe", StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(rest, "status", StringComparison.OrdinalIgnoreCase))
                {
                    ReportStatus();
                    return;
                }

                Chat("This diagnostic build does not alter lighting. Use /acgamma probe.");
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

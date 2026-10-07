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
        private const string Version = "0.4.0-alpha2";

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

        protected override void Startup()
        {
            try
            {
                CoreManager.Current.CommandLineText += Current_CommandLineText;
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

                object serviceObject = Host.GetObject(@"services\DecalPlugins.InjectService");
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
        }

        public void PreEndScene(object direct3D)
        {
            Interlocked.Increment(ref _preEndCount);
            ObserveDevice(direct3D);
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
                    try
                    {
                        pUnk = Marshal.GetIUnknownForObject(direct3D);
                        if (pUnk != IntPtr.Zero)
                            _deviceIUnknown = "0x" + pUnk.ToInt64().ToString("X");
                    }
                    finally
                    {
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
                 ", PreEnd=" + Interlocked.Read(ref _preEndCount) +
                 ", PostEnd=" + Interlocked.Read(ref _postEndCount));

            lock (_deviceLock)
            {
                Chat("D3D callback object = " + _deviceRuntimeType +
                     "; IUnknown = " + _deviceIUnknown);
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

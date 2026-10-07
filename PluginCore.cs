using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Decal.Adapter;

namespace ACWorldGamma
{
    [FriendlyName("AC World Gamma")]
    [Guid("A9D7C4AA-2A2E-4D2D-9F83-7B728C37E8D4")]
    public sealed class PluginCore : PluginBase
    {
        private const string Version = "0.3.0";

        private RenderHookLib.ISVRenderHook _hook;
        private IntPtr _renderHookModule = IntPtr.Zero;
        private bool _hookReady = false;
        private bool _enabled = false;
        private int _level = 1;

        private static readonly Guid ClsidSVRenderHook =
            new Guid("084DB7D3-FCA8-4C37-8748-18232FE9CF9A");

        private static readonly Guid IidIClassFactory =
            new Guid("00000001-0000-0000-C000-000000000046");

        private static readonly Guid IidSVRenderHook =
            new Guid("F5E367AA-6FC9-473B-8BC8-9060C25EFA39");

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int DllGetClassObjectDelegate(
            ref Guid rclsid,
            ref Guid riid,
            out IntPtr ppv);

        [ComImport]
        [Guid("00000001-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IClassFactory
        {
            [PreserveSig]
            int CreateInstance(
                IntPtr pUnkOuter,
                ref Guid riid,
                out IntPtr ppvObject);

            [PreserveSig]
            int LockServer([MarshalAs(UnmanagedType.Bool)] bool fLock);
        }

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

        private string PrivateRenderHookPath
        {
            get
            {
                string pluginDir = System.IO.Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location);
                return System.IO.Path.Combine(pluginDir, "RenderHook.dll");
            }
        }

        protected override void Startup()
        {
            try
            {
                CoreManager.Current.CommandLineText += Current_CommandLineText;

                LoadSettings();
                InitializePrivateRenderHook();

                if (_hookReady)
                {
                    ApplyCurrentSetting();
                    Chat("v" + Version + " loaded. " + StatusText());
                }
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

            ReleaseRenderHook();
        }

        private void InitializePrivateRenderHook()
        {
            IntPtr factoryPtr = IntPtr.Zero;
            IntPtr hookPtr = IntPtr.Zero;
            IClassFactory factory = null;

            try
            {
                if (!File.Exists(PrivateRenderHookPath))
                {
                    Chat("Private RenderHook.dll is missing.");
                    return;
                }

                _renderHookModule = LoadLibraryW(PrivateRenderHookPath);
                if (_renderHookModule == IntPtr.Zero)
                {
                    throw new System.ComponentModel.Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "LoadLibrary failed for " + PrivateRenderHookPath);
                }

                IntPtr proc = GetProcAddress(_renderHookModule, "DllGetClassObject");
                if (proc == IntPtr.Zero)
                {
                    throw new InvalidOperationException(
                        "RenderHook.dll does not export DllGetClassObject.");
                }

                DllGetClassObjectDelegate getClassObject =
                    (DllGetClassObjectDelegate)Marshal.GetDelegateForFunctionPointer(
                        proc, typeof(DllGetClassObjectDelegate));

                Guid clsid = ClsidSVRenderHook;
                Guid iidFactory = IidIClassFactory;

                int hr = getClassObject(
                    ref clsid, ref iidFactory, out factoryPtr);
                Marshal.ThrowExceptionForHR(hr);

                factory = (IClassFactory)Marshal.GetObjectForIUnknown(factoryPtr);

                Guid iidHook = IidSVRenderHook;
                hr = factory.CreateInstance(
                    IntPtr.Zero, ref iidHook, out hookPtr);
                Marshal.ThrowExceptionForHR(hr);

                _hook = (RenderHookLib.ISVRenderHook)
                    Marshal.GetTypedObjectForIUnknown(
                        hookPtr, typeof(RenderHookLib.ISVRenderHook));

                object netSvc = null;
                try
                {
                    netSvc = Host.Decal.GetObject(
                        @"services\DecalNet.NetService",
                        "{AA405035-E001-4CC3-B43A-156206843D64}");

                    _hook.Init(netSvc);
                }
                finally
                {
                    if (netSvc != null && Marshal.IsComObject(netSvc))
                    {
                        try { Marshal.FinalReleaseComObject(netSvc); } catch { }
                    }
                }

                _hook.fSlope = false;
                _hook.fWater = false;
                _hook.fLight = false;
                _hook.fEnabled = false;

                _hookReady = true;
            }
            catch (Exception ex)
            {
                _hookReady = false;
                Fail("RenderHook", ex);
                ReleaseRenderHook();
            }
            finally
            {
                if (hookPtr != IntPtr.Zero)
                {
                    try { Marshal.Release(hookPtr); } catch { }
                }

                if (factory != null && Marshal.IsComObject(factory))
                {
                    try { Marshal.FinalReleaseComObject(factory); } catch { }
                }

                if (factoryPtr != IntPtr.Zero)
                {
                    try { Marshal.Release(factoryPtr); } catch { }
                }
            }
        }

        private void ReleaseRenderHook()
        {
            try
            {
                if (_hook != null)
                {
                    try { _hook.fLight = false; } catch { }
                    try { _hook.fEnabled = false; } catch { }
                    try { _hook.Finalize(); } catch { }

                    try
                    {
                        if (Marshal.IsComObject(_hook))
                            Marshal.FinalReleaseComObject(_hook);
                    }
                    catch { }
                }
            }
            finally
            {
                _hook = null;
                _hookReady = false;

                if (_renderHookModule != IntPtr.Zero)
                {
                    try { FreeLibrary(_renderHookModule); } catch { }
                    _renderHookModule = IntPtr.Zero;
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
                string lower = raw.ToLowerInvariant();

                if (!lower.StartsWith("/acgamma"))
                    return;

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
                    EnsureLevel();
                    ApplyCurrentSetting();
                    SaveSettings();
                    Chat(StatusText());
                    return;
                }

                if (String.Equals(rest, "off", StringComparison.OrdinalIgnoreCase))
                {
                    _enabled = false;
                    ApplyCurrentSetting();
                    SaveSettings();
                    Chat(StatusText());
                    return;
                }

                if (String.Equals(rest, "reset", StringComparison.OrdinalIgnoreCase))
                {
                    _enabled = false;
                    _level = 1;
                    ApplyCurrentSetting();
                    SaveSettings();
                    Chat("reset to normal world lighting.");
                    return;
                }

                if (String.Equals(rest, "up", StringComparison.OrdinalIgnoreCase))
                {
                    _level = Math.Min(25, _level + 1);
                    _enabled = true;
                    ApplyCurrentSetting();
                    SaveSettings();
                    Chat(StatusText());
                    return;
                }

                if (String.Equals(rest, "down", StringComparison.OrdinalIgnoreCase))
                {
                    _level = Math.Max(0, _level - 1);
                    _enabled = _level > 0;
                    ApplyCurrentSetting();
                    SaveSettings();
                    Chat(StatusText());
                    return;
                }

                int requested;
                if (Int32.TryParse(
                    rest, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out requested))
                {
                    if (requested < 0 || requested > 25)
                    {
                        Chat("level must be from 0 to 25.");
                        return;
                    }

                    _level = requested;
                    _enabled = requested > 0;
                    ApplyCurrentSetting();
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

        private void ApplyCurrentSetting()
        {
            if (!_hookReady || _hook == null)
            {
                Chat("RenderHook is not available.");
                return;
            }

            try
            {
                if (!_enabled || _level <= 0)
                {
                    _hook.fLight = false;
                    _hook.fEnabled = false;
                    return;
                }

                EnsureLevel();

                int rgb = Math.Min(250, _level * 10);
                int argb = unchecked((int)0xFF000000) |
                           (rgb << 16) | (rgb << 8) | rgb;

                _hook.colorLight = argb;
                _hook.fLight = true;
                _hook.fEnabled = true;
            }
            catch (Exception ex)
            {
                Fail("Apply", ex);
            }
        }

        private void EnsureLevel()
        {
            if (_level < 1)
                _level = 1;

            if (_level > 25)
                _level = 25;
        }

        private string StatusText()
        {
            if (!_hookReady)
                return "RenderHook unavailable.";

            if (!_enabled || _level <= 0)
                return "OFF (normal AC world lighting).";

            return "ON, world brightness level " +
                _level.ToString(CultureInfo.InvariantCulture) + " of 25.";
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
                        lines[1], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out level))
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
            try
            {
                CoreManager.Current.Actions.AddChatText(
                    "[AC Gamma] " + where + " error: " + ex.Message, 5);
            }
            catch { }
        }
    }
}

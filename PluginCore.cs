using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Decal.Adapter;

namespace ACWorldGamma
{
    [FriendlyName("AC World Gamma Native Test")]
    [Guid("A9D7C4AA-2A2E-4D2D-9F83-7B728C37E8D4")]
    public sealed class PluginCore : PluginBase
    {
        private const string Version = "0.4.0-alpha1";

        private Direct3DLightHook _hook;
        private bool _hookReady = false;
        private string _lastHookError = "";
        private bool _enabled = false;
        private int _level = 1;

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

                LoadSettings();
                InitializeNativeLightHook();

                if (_hookReady)
                {
                    ApplyCurrentSetting();
                    Chat("v" + Version + " loaded using the independent native light hook. " + StatusText());
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

            ReleaseNativeLightHook();
        }

        private void InitializeNativeLightHook()
        {
            try
            {
                _lastHookError = "";

                object device = Host.Render.UnsafeDevice;

                _hook = new Direct3DLightHook();

                if (device != null)
                {
                    _hook.Install(device);
                }
                else
                {
                    // Decal 2.9.8.3 can return NULL for UnsafeDevice even though
                    // AC is actively using d3d9.dll. Fall back to a private D3D9
                    // device so we can patch the runtime's SetLight method table.
                    _hook.InstallUsingPrivateDevice();
                }

                _hookReady = _hook.Installed;

                if (!_hookReady)
                    _lastHookError = "Hook installation completed but did not report ready.";
            }
            catch (Exception ex)
            {
                _hookReady = false;
                _lastHookError = ex.GetType().Name + ": " + ex.Message;
                ReleaseNativeLightHook();
            }
        }

        private void ReleaseNativeLightHook()
        {
            try
            {
                if (_hook != null)
                    _hook.Dispose();
            }
            catch { }
            finally
            {
                _hook = null;
                _hookReady = false;
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
                    Chat("Commands: /acgamma on | off | 0-25 | up | down | reset | status | probe");
                    return;
                }

                if (String.Equals(rest, "status", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_hookReady)
                        InitializeNativeLightHook();

                    Chat(StatusText());
                    return;
                }

                if (String.Equals(rest, "probe", StringComparison.OrdinalIgnoreCase))
                {
                    ProbeRenderDevice();
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

        private void ProbeRenderDevice()
        {
            try
            {
                object wrapper = Host.Render;
                if (wrapper == null)
                {
                    Chat("PROBE: Host.Render is null.");
                    return;
                }

                Chat("PROBE: Host.Render type = " + wrapper.GetType().FullName);

                object unsafeDevice = null;
                try
                {
                    unsafeDevice = Host.Render.UnsafeDevice;
                }
                catch (Exception ex)
                {
                    Chat("PROBE: UnsafeDevice threw " + ex.GetType().Name + ": " + ex.Message);
                }

                Chat("PROBE: UnsafeDevice = " +
                    (unsafeDevice == null ? "NULL" : unsafeDevice.GetType().FullName));

                FieldInfo internalRenderField = wrapper.GetType().GetField(
                    "internalRender",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                if (internalRenderField == null)
                {
                    Chat("PROBE: internalRender field not found.");
                    return;
                }

                object internalRender = internalRenderField.GetValue(wrapper);
                if (internalRender == null)
                {
                    Chat("PROBE: internalRender = NULL.");
                    return;
                }

                Chat("PROBE: internalRender runtime type = " +
                    internalRender.GetType().FullName);

                Type declaredRenderType = internalRenderField.FieldType;
                Type renderInterface = declaredRenderType.Assembly.GetType(
                    "Decal.Interop.Render.IRenderService");

                if (renderInterface == null)
                {
                    Chat("PROBE: IRenderService interface not found.");
                    return;
                }

                Chat("PROBE: IRenderService found = " + renderInterface.FullName);

                PropertyInfo deviceProperty = renderInterface.GetProperty(
                    "Device",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (deviceProperty == null)
                {
                    Chat("PROBE: IRenderService.Device property not found.");
                    return;
                }

                object rawDevice = null;
                try
                {
                    rawDevice = deviceProperty.GetValue(internalRender, null);
                }
                catch (TargetInvocationException ex)
                {
                    Exception inner = ex.InnerException ?? ex;
                    Chat("PROBE: IRenderService.Device threw " +
                        inner.GetType().Name + ": " + inner.Message);
                    return;
                }

                Chat("PROBE: IRenderService.Device = " +
                    (rawDevice == null ? "NULL" : rawDevice.GetType().FullName));

                if (rawDevice != null)
                {
                    IntPtr pUnk = IntPtr.Zero;
                    try
                    {
                        pUnk = Marshal.GetIUnknownForObject(rawDevice);
                        Chat("PROBE: Device IUnknown = 0x" +
                            pUnk.ToInt64().ToString("X"));
                    }
                    finally
                    {
                        if (pUnk != IntPtr.Zero)
                            Marshal.Release(pUnk);
                    }
                }
            }
            catch (Exception ex)
            {
                Chat("PROBE error: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void ApplyCurrentSetting()
        {
            if (!_hookReady || _hook == null)
            {
                Chat("Native light hook is not available.");
                return;
            }

            try
            {
                if (!_enabled || _level <= 0)
                {
                    _hook.SetLevel(false, 0);
                    return;
                }

                EnsureLevel();
                _hook.SetLevel(true, _level);
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
            {
                if (!String.IsNullOrEmpty(_lastHookError))
                    return "Native light hook unavailable: " + _lastHookError;

                return "Native light hook unavailable.";
            }

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

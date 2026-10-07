using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class Uninstaller
{
    private const string AppName = "AC World Gamma";
    private const string PluginGuid = "{A9D7C4AA-2A2E-4D2D-9F83-7B728C37E8D4}";

    [STAThread]
    private static void Main()
    {
        try
        {
            if (!IsAdministrator())
            {
                RelaunchElevated();
                return;
            }

            if (Process.GetProcessesByName("acclient").Length > 0)
            {
                MessageBox.Show(
                    "Please close all Asheron's Call clients before removing AC World Gamma.",
                    AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                Registry.LocalMachine.DeleteSubKeyTree(
                    @"Software\Decal\Plugins\" + PluginGuid, false);
            }
            catch { }

            try
            {
                Registry.LocalMachine.DeleteSubKeyTree(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName,
                    false);
            }
            catch { }

            string dir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

            string[] files =
            {
                "ACWorldGamma.dll",
                "README.txt",
                "RenderHook.dll",
                "Interop.RenderHookLib.dll",
                "THIRD_PARTY_NOTES.txt"
            };

            foreach (string file in files)
            {
                try
                {
                    string full = Path.Combine(dir, file);
                    if (File.Exists(full))
                        File.Delete(full);
                }
                catch { }
            }

            string cmd =
                "/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"" + dir + "\"";

            Process.Start(new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                Arguments = cmd,
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Uninstall failed:\n\n" + ex.Message,
                AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static bool IsAdministrator()
    {
        WindowsIdentity id = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new WindowsPrincipal(id);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void RelaunchElevated()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Assembly.GetExecutingAssembly().Location,
                UseShellExecute = true,
                Verb = "runas"
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}

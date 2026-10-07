using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class Installer
{
    private const string AppName = "AC World Gamma Native Test";
    private const string Version = "0.4.0-alpha1";
    private const string PluginGuid = "{A9D7C4AA-2A2E-4D2D-9F83-7B728C37E8D4}";
    private const string Surrogate = "{71A69713-6593-47EC-0002-0000000DECA1}";
    private const string InstallDir = @"C:\Games\Decal Plugins\AC World Gamma Native Test";

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
                    "Please close Asheron's Call before installing the native-hook test.",
                    AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string decal = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                @"Decal 3.0\Decal.Adapter.dll");

            if (!File.Exists(decal))
            {
                MessageBox.Show(
                    "Decal 3.0 was not found. Install Decal first.",
                    AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Directory.CreateDirectory(InstallDir);

            ExtractResource("ACWorldGamma.dll",
                System.IO.Path.Combine(InstallDir, "ACWorldGamma.dll"));
            ExtractResource("uninstall.exe",
                System.IO.Path.Combine(InstallDir, "uninstall.exe"));
            ExtractResource("README.txt",
                System.IO.Path.Combine(InstallDir, "README.txt"));
            ExtractResource("NATIVE_HOOK_NOTES.md",
                System.IO.Path.Combine(InstallDir, "NATIVE_HOOK_NOTES.md"));

            using (RegistryKey plugin = Registry.LocalMachine.CreateSubKey(
                @"Software\Decal\Plugins\" + PluginGuid))
            {
                plugin.SetValue("", AppName, RegistryValueKind.String);
                plugin.SetValue("Enabled", 1, RegistryValueKind.DWord);
                plugin.SetValue("Object", "ACWorldGamma.PluginCore", RegistryValueKind.String);
                plugin.SetValue("Assembly", "ACWorldGamma.dll", RegistryValueKind.String);
                plugin.SetValue("Path", InstallDir, RegistryValueKind.String);
                plugin.SetValue("Surrogate", Surrogate, RegistryValueKind.String);
                plugin.SetValue("Uninstaller", AppName, RegistryValueKind.String);
            }

            using (RegistryKey un = Registry.LocalMachine.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName))
            {
                un.SetValue("DisplayName", AppName, RegistryValueKind.String);
                un.SetValue("DisplayVersion", Version, RegistryValueKind.String);
                un.SetValue("Publisher", "EWARAC", RegistryValueKind.String);
                un.SetValue("InstallLocation", InstallDir, RegistryValueKind.String);
                un.SetValue(
                    "UninstallString",
                    "\"" + System.IO.Path.Combine(InstallDir, "uninstall.exe") + "\"",
                    RegistryValueKind.String);
                un.SetValue(
                    "DisplayIcon",
                    System.IO.Path.Combine(InstallDir, "uninstall.exe"),
                    RegistryValueKind.String);
                un.SetValue("NoModify", 1, RegistryValueKind.DWord);
                un.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }

            MessageBox.Show(
                "AC World Gamma native-hook test " + Version + " is installed.\n\n" +
                "This experimental build replaces the Decal registration for the stable plugin, " +
                "but installs into a separate folder and does not overwrite the stable files.\n\n" +
                "Start Asheron's Call normally, then type:\n\n/acgamma status",
                AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Installation failed:\n\n" + ex.Message,
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

    private static void ExtractResource(string resourceName, string destination)
    {
        Assembly asm = Assembly.GetExecutingAssembly();

        using (Stream input = asm.GetManifestResourceStream(resourceName))
        {
            if (input == null)
            {
                throw new InvalidOperationException(
                    "Installer resource missing: " + resourceName);
            }

            using (FileStream output = new FileStream(
                destination, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                input.CopyTo(output);
            }
        }
    }
}

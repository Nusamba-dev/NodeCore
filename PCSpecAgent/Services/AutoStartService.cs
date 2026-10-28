using Microsoft.Win32;

namespace PCSpecAgent.Services;

public static class AutoStartService
{
    private const string AppName = "NusambaNodeCore";
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    public static void SetAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key == null) return;

            string exePath = Application.ExecutablePath;

            if (enable)
            {
                key?.SetValue(AppName, $"\"{exePath}\" --minimized");

                // Also register elevated scheduled task so silent installs/uninstalls run with highest available privileges
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = $"/create /tn \"{AppName}\" /tr \"\\\"{exePath}\\\" --minimized\" /sc onlogon /rl highest /f",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                    };
                    using var p = System.Diagnostics.Process.Start(psi);
                    p?.WaitForExit(3000);
                }
                catch { }
            }
            else
            {
                key?.DeleteValue(AppName, false);

                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = $"/delete /tn \"{AppName}\" /f",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var p = System.Diagnostics.Process.Start(psi);
                    p?.WaitForExit(3000);
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to set autostart: {ex.Message}");
        }
    }

    public static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }
}

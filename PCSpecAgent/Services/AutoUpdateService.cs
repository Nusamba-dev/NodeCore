using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using PCSpecAgent.Models;

namespace PCSpecAgent.Services;

public static class AutoUpdateService
{
    private static readonly object _lock = new();
    private static bool _isUpdating = false;
    private static string? _lastAttemptedVersion = null;
    private static DateTime _lastAttemptTime = DateTime.MinValue;

    public static Version CurrentVersion
    {
        get
        {
            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            return ver ?? new Version(3, 0, 0);
        }
    }

    public static string CurrentVersionString => $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

    /// <summary>
    /// Check server update payload and automatically download & apply MSI upgrade silently if newer
    /// </summary>
    public static async Task CheckAndApplyUpdateAsync(
        AgentUpdateInfo updateInfo,
        ApiClient apiClient,
        Action<string>? notify = null,
        bool isManualCheck = false)
    {
        lock (_lock)
        {
            if (_isUpdating) return;
        }

        if (string.IsNullOrWhiteSpace(updateInfo.LatestVersion))
        {
            if (isManualCheck) notify?.Invoke("Informasi versi terbaru belum tersedia di server.");
            return;
        }

        if (!Version.TryParse(updateInfo.LatestVersion, out var serverVer))
        {
            if (isManualCheck) notify?.Invoke($"Format versi server tidak valid: {updateInfo.LatestVersion}");
            return;
        }

        // Compare versions (e.g. 3.0.0 vs 2.6.2)
        if (serverVer <= CurrentVersion)
        {
            if (isManualCheck)
            {
                notify?.Invoke($"Nusamba NodeCore sudah merupakan versi terbaru (v{CurrentVersionString}).");
            }
            return;
        }

        // Anti-loop protection: do not retry within 15 minutes if automatic check
        if (!isManualCheck && _lastAttemptedVersion == updateInfo.LatestVersion && (DateTime.UtcNow - _lastAttemptTime) < TimeSpan.FromMinutes(15))
        {
            return;
        }

        lock (_lock)
        {
            _isUpdating = true;
            _lastAttemptedVersion = updateInfo.LatestVersion;
            _lastAttemptTime = DateTime.UtcNow;
        }

        try
        {
            // Detect architecture
            bool isArm64 = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
            string? downloadUrl = isArm64
                ? (!string.IsNullOrWhiteSpace(updateInfo.DownloadUrlArm64) ? updateInfo.DownloadUrlArm64 : updateInfo.DownloadUrlX64)
                : (!string.IsNullOrWhiteSpace(updateInfo.DownloadUrlX64) ? updateInfo.DownloadUrlX64 : updateInfo.DownloadUrlArm64);

            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                if (isManualCheck) notify?.Invoke("URL unduhan installer pembaruan tidak ditemukan.");
                return;
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "NodeCoreUpdate");
            Directory.CreateDirectory(tempDir);
            string msiPath = Path.Combine(tempDir, $"NusambaNodeCore-Setup-v{serverVer}.msi");

            notify?.Invoke($"Ditemukan Nusamba NodeCore v{serverVer}! Mengunduh pembaruan di latar belakang...");

            bool downloaded = await apiClient.DownloadPackageAsync(downloadUrl, msiPath);
            if (!downloaded || !File.Exists(msiPath) || new FileInfo(msiPath).Length < 1024 * 1024 * 10) // Must be valid size (>10MB)
            {
                notify?.Invoke("Gagal mengunduh paket pembaruan Nusamba NodeCore.");
                return;
            }

            string exePath = Environment.ProcessPath ?? Application.ExecutablePath;
            string cmdPath = Path.Combine(tempDir, "apply_update.cmd");

            // Write helper command to execute silent MSI installation and restart application
            string cmdScript = $@"@echo off
timeout /t 3 /nobreak >nul
taskkill /f /im NusambaNode.exe >nul 2>&1
taskkill /f /im TheWizard-io.exe >nul 2>&1
timeout /t 2 /nobreak >nul
start /wait msiexec.exe /i ""{msiPath}"" /qn /norestart
timeout /t 3 /nobreak >nul
if exist ""C:\Program Files\Nusamba NodeCore\NusambaNode.exe"" (
    start """" ""C:\Program Files\Nusamba NodeCore\NusambaNode.exe""
) else if exist ""C:\Program Files (x86)\Nusamba NodeCore\NusambaNode.exe"" (
    start """" ""C:\Program Files (x86)\Nusamba NodeCore\NusambaNode.exe""
) else if exist ""C:\Program Files\TheWizard-io\TheWizard-io.exe"" (
    start """" ""C:\Program Files\TheWizard-io\TheWizard-io.exe""
) else (
    start """" ""{exePath}""
)
timeout /t 2 /nobreak >nul
del ""{msiPath}"" >nul 2>&1
del ""%~f0"" >nul 2>&1
";
            await File.WriteAllTextAsync(cmdPath, cmdScript);

            notify?.Invoke($"Nusamba NodeCore v{serverVer} siap dipasang. Merestart aplikasi dalam beberapa detik...");
            await Task.Delay(2000);

            // Spawn detached installer helper
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{cmdPath}\"\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi);

            // Terminate current process to release locked binaries
            Application.Exit();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Self-update failed: {ex.Message}");
            notify?.Invoke($"Pembaruan otomatis gagal: {ex.Message}");
        }
        finally
        {
            lock (_lock)
            {
                _isUpdating = false;
            }
        }
    }
}

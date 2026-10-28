using System.Diagnostics;
using System.Security.Principal;
using PCSpecAgent.Models;

namespace PCSpecAgent.Services;

public static class JobExecutor
{
    private static readonly object _lock = new();
    private static bool _isRunning = false;

    public static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static string GetSafeTempDirectory()
    {
        try
        {
            string commonData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (!string.IsNullOrEmpty(commonData))
            {
                string p = Path.Combine(commonData, "NusambaNodeCore", "downloads");
                Directory.CreateDirectory(p);
                return p;
            }
        }
        catch { }
        string temp = Path.Combine(Path.GetTempPath(), "NusambaNodeCore", "downloads");
        Directory.CreateDirectory(temp);
        return temp;
    }

    public static async Task ProcessJobsAsync(List<PendingJob> jobs, ApiClient apiClient, AppConfig config, Action<string>? onStatusUpdate = null)
    {
        lock (_lock)
        {
            if (_isRunning) return;
            _isRunning = true;
        }

        try
        {
            foreach (var job in jobs)
            {
                await ExecuteSingleJobAsync(job, apiClient, config, onStatusUpdate);
            }
        }
        finally
        {
            lock (_lock)
            {
                _isRunning = false;
            }
        }
    }

    private static async Task ExecuteSingleJobAsync(PendingJob job, ApiClient apiClient, AppConfig config, Action<string>? onStatusUpdate)
    {
        try
        {
            if (job.JobType.Equals("install", StringComparison.OrdinalIgnoreCase))
            {
                await ExecuteInstallJobAsync(job, apiClient, config, onStatusUpdate);
            }
            else if (job.JobType.Equals("uninstall", StringComparison.OrdinalIgnoreCase))
            {
                await ExecuteUninstallJobAsync(job, apiClient, config, onStatusUpdate);
            }
            else if (job.JobType.Equals("command", StringComparison.OrdinalIgnoreCase) || job.JobType.Equals("script", StringComparison.OrdinalIgnoreCase))
            {
                await ExecuteCommandJobAsync(job, apiClient, config, onStatusUpdate);
            }
            else
            {
                await apiClient.ReportJobStatusAsync(job.JobId, "failed", 100, "", $"Tipe tugas tidak dikenali: {job.JobType}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error executing job {job.JobId}: {ex.Message}");
            await apiClient.ReportJobStatusAsync(job.JobId, "failed", 100, "", $"Kesalahan sistem: {ex.Message}");
        }
    }

    private static async Task ExecuteInstallJobAsync(PendingJob job, ApiClient apiClient, AppConfig config, Action<string>? onStatusUpdate)
    {
        if (string.IsNullOrWhiteSpace(job.DownloadUrl))
        {
            await apiClient.ReportJobStatusAsync(job.JobId, "failed", 100, "", "URL download kosong.");
            return;
        }

        onStatusUpdate?.Invoke($"Sedang mendownload {job.AppName}...");
        await apiClient.ReportJobStatusAsync(job.JobId, "downloading", 10, $"Mulai mendownload {job.AppName} dari {job.DownloadUrl}...");

        string tempDir = GetSafeTempDirectory();
        string fileName = Path.GetFileName(new Uri(job.DownloadUrl).LocalPath);
        if (string.IsNullOrEmpty(fileName)) fileName = $"{job.AppName}.msi";
        string localFilePath = Path.Combine(tempDir, $"{job.JobId}_{fileName}");

        // Progress reporter
        int lastReportedPct = 10;
        var progress = new Progress<int>(async pct =>
        {
            if (pct >= lastReportedPct + 20 && pct < 90)
            {
                lastReportedPct = pct;
                await apiClient.ReportJobStatusAsync(job.JobId, "downloading", pct, $"Download {pct}%...");
            }
        });

        bool downloaded = await apiClient.DownloadPackageAsync(job.DownloadUrl, localFilePath, progress);
        if (!downloaded || !File.Exists(localFilePath))
        {
            await apiClient.ReportJobStatusAsync(job.JobId, "failed", 100, "", $"Gagal mendownload installer dari {job.DownloadUrl}");
            return;
        }

        onStatusUpdate?.Invoke($"Sedang menginstall {job.AppName}...");
        await apiClient.ReportJobStatusAsync(job.JobId, "running", 60, $"File berhasil didownload ({new FileInfo(localFilePath).Length / 1024} KB). Memulai instalasi silent...");

        // Determine installer execution
        string ext = Path.GetExtension(localFilePath).ToLowerInvariant();
        string execFile;
        string execArgs;

        string silentArgs = job.SilentArgs?.Trim() ?? "";
        string adminUser = !string.IsNullOrWhiteSpace(job.AdminUser) ? job.AdminUser : (config.AdminUser ?? "");
        string adminPass = !string.IsNullOrWhiteSpace(job.AdminPass) ? job.AdminPass : (config.AdminPass ?? "");
        bool isAdmin = IsAdministrator();

        if (ext == ".msi")
        {
            execFile = "msiexec.exe";
            if (string.IsNullOrEmpty(silentArgs)) silentArgs = "/qn /norestart";

            // If running as standard non-admin and no credentials provided, add dual-purpose per-user flags
            if (!isAdmin && string.IsNullOrEmpty(adminUser) && !silentArgs.Contains("ALLUSERS", StringComparison.OrdinalIgnoreCase))
            {
                silentArgs += " ALLUSERS=2 MSIINSTALLPERUSER=1";
            }
            execArgs = $"/i \"{localFilePath}\" {silentArgs}";
        }
        else
        {
            execFile = localFilePath;
            if (string.IsNullOrEmpty(silentArgs)) silentArgs = "/VERYSILENT /NORESTART /SUPPRESSMSGBOXES";
            execArgs = silentArgs;
        }

        var (exitCode, stdout, stderr) = await RunElevatedOrStandardProcessAsync(execFile, execArgs, adminUser, adminPass, TimeSpan.FromMinutes(15));

        // Delete downloaded installer file to save disk space
        try { File.Delete(localFilePath); } catch { }

        // Exit Code 0 (Success) or 3010 (Success reboot required)
        if (exitCode == 0 || exitCode == 3010)
        {
            string note = exitCode == 3010 ? " (Perlu restart komputer)" : "";
            await apiClient.ReportJobStatusAsync(job.JobId, "success", 100, $"Instalasi berhasil selesai dengan kode 0{note}.\n{stdout}", "", exitCode);
            onStatusUpdate?.Invoke($"Instalasi {job.AppName} berhasil!");

            // Re-scan and update software list on server
            _ = Task.Run(async () =>
            {
                await Task.Delay(3000);
                var fullPayload = HardwareCollector.CollectAll(config);
                await apiClient.RegisterAsync(fullPayload);
            });
        }
        else
        {
            string err = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            await apiClient.ReportJobStatusAsync(job.JobId, "failed", 100, stdout, $"Instalasi gagal dengan kode keluar: {exitCode}\n{err}", exitCode);
            onStatusUpdate?.Invoke($"Instalasi {job.AppName} gagal.");
        }
    }

    private static async Task ExecuteUninstallJobAsync(PendingJob job, ApiClient apiClient, AppConfig config, Action<string>? onStatusUpdate)
    {
        onStatusUpdate?.Invoke($"Sedang menghapus {job.AppName}...");
        await apiClient.ReportJobStatusAsync(job.JobId, "running", 30, $"Memulai uninstalasi silent untuk {job.AppName}...");

        string cmd = job.Command?.Trim() ?? "";
        string execFile = "";
        string execArgs = "";

        string adminUser = !string.IsNullOrWhiteSpace(job.AdminUser) ? job.AdminUser : (config.AdminUser ?? "");
        string adminPass = !string.IsNullOrWhiteSpace(job.AdminPass) ? job.AdminPass : (config.AdminPass ?? "");

        if (cmd.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
        {
            execFile = "msiexec.exe";
            // Replace /I with /X
            string argsOnly = cmd;
            int idx = cmd.IndexOf("msiexec", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                argsOnly = cmd.Substring(idx + 7).Trim();
            }

            // Replace /I or /i with /x
            argsOnly = System.Text.RegularExpressions.Regex.Replace(argsOnly, @"/[iI]", "/x");
            if (!argsOnly.Contains("/qn", StringComparison.OrdinalIgnoreCase))
            {
                argsOnly += " /qn /norestart";
            }
            execArgs = argsOnly;
        }
        else if (!string.IsNullOrEmpty(cmd))
        {
            // Parse executable and args
            if (cmd.StartsWith("\""))
            {
                int closeQuote = cmd.IndexOf('"', 1);
                if (closeQuote > 0)
                {
                    execFile = cmd.Substring(1, closeQuote - 1);
                    execArgs = cmd.Substring(closeQuote + 1).Trim();
                }
            }

            if (string.IsNullOrEmpty(execFile))
            {
                string[] parts = cmd.Split(' ', 2);
                execFile = parts[0];
                execArgs = parts.Length > 1 ? parts[1] : "";
            }

            // Append silent switches if not present
            if (!execArgs.Contains("/silent", StringComparison.OrdinalIgnoreCase) &&
                !execArgs.Contains("/verysilent", StringComparison.OrdinalIgnoreCase) &&
                !execArgs.Contains("/s", StringComparison.OrdinalIgnoreCase) &&
                !execArgs.Contains("/qn", StringComparison.OrdinalIgnoreCase))
            {
                execArgs += " /VERYSILENT /NORESTART /SUPPRESSMSGBOXES /S /quiet";
            }
        }
        else
        {
            // Fallback: try winget silent uninstall
            execFile = "winget";
            execArgs = $"uninstall --name \"{job.AppName}\" --silent --accept-source-agreements";
        }

        var (exitCode, stdout, stderr) = await RunElevatedOrStandardProcessAsync(execFile, execArgs, adminUser, adminPass, TimeSpan.FromMinutes(10));

        if (exitCode == 0 || exitCode == 3010)
        {
            await apiClient.ReportJobStatusAsync(job.JobId, "success", 100, $"Uninstalasi {job.AppName} berhasil selesai dengan kode 0.\n{stdout}", "", exitCode);
            onStatusUpdate?.Invoke($"Uninstalasi {job.AppName} berhasil!");

            // Re-scan and update software list on server
            _ = Task.Run(async () =>
            {
                await Task.Delay(3000);
                var fullPayload = HardwareCollector.CollectAll(config);
                await apiClient.RegisterAsync(fullPayload);
            });
        }
        else
        {
            string err = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            await apiClient.ReportJobStatusAsync(job.JobId, "failed", 100, stdout, $"Uninstalasi gagal dengan kode keluar: {exitCode}\n{err}", exitCode);
            onStatusUpdate?.Invoke($"Uninstalasi {job.AppName} gagal.");
        }
    }

    private static async Task ExecuteCommandJobAsync(PendingJob job, ApiClient apiClient, AppConfig config, Action<string>? onStatusUpdate)
    {
        onStatusUpdate?.Invoke($"Sedang menjalankan tugas: {job.AppName}...");
        await apiClient.ReportJobStatusAsync(job.JobId, "running", 30, $"Memulai eksekusi perintah untuk {job.AppName}...");

        string cmd = job.Command?.Trim() ?? "";
        if (string.IsNullOrEmpty(cmd))
        {
            await apiClient.ReportJobStatusAsync(job.JobId, "failed", 100, "", "Perintah kosong.");
            return;
        }

        string adminUser = !string.IsNullOrWhiteSpace(job.AdminUser) ? job.AdminUser : (config.AdminUser ?? "");
        string adminPass = !string.IsNullOrWhiteSpace(job.AdminPass) ? job.AdminPass : (config.AdminPass ?? "");

        var (exitCode, stdout, stderr) = await RunElevatedOrStandardProcessAsync("cmd.exe", $"/c {cmd}", adminUser, adminPass, TimeSpan.FromMinutes(10));

        if (exitCode == 0)
        {
            await apiClient.ReportJobStatusAsync(job.JobId, "success", 100, $"Perintah {job.AppName} berhasil dijalankan.\n{stdout}", "", exitCode);
            onStatusUpdate?.Invoke($"Tugas {job.AppName} selesai!");

            // Re-scan and update specs & VPN/RDP on server
            _ = Task.Run(async () =>
            {
                await Task.Delay(2000);
                var fullPayload = HardwareCollector.CollectAll(config);
                await apiClient.RegisterAsync(fullPayload);
            });
        }
        else
        {
            string err = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            await apiClient.ReportJobStatusAsync(job.JobId, "failed", 100, stdout, $"Gagal dengan kode: {exitCode}\n{err}", exitCode);
            onStatusUpdate?.Invoke($"Tugas {job.AppName} gagal.");
        }
    }

    private static async Task<(int exitCode, string stdout, string stderr)> RunElevatedOrStandardProcessAsync(
        string fileName,
        string arguments,
        string adminUser,
        string adminPass,
        TimeSpan timeout)
    {
        bool isAdmin = IsAdministrator();

        // 1. If running as Administrator or SYSTEM, execute directly
        if (isAdmin || (string.IsNullOrWhiteSpace(adminUser) && string.IsNullOrWhiteSpace(adminPass)))
        {
            var res = await RunProcessAsync(fileName, arguments, timeout);
            if (res.exitCode == 0 || res.exitCode == 3010 || string.IsNullOrWhiteSpace(adminUser))
            {
                return res;
            }
            // If failed with Access Denied (exitCode 1, 5, or 1603) and admin credentials are supplied, attempt elevation!
        }

        // 2. If admin credentials supplied, execute via elevated Task Scheduler
        if (!string.IsNullOrWhiteSpace(adminUser) && !string.IsNullOrWhiteSpace(adminPass))
        {
            var taskRes = await RunViaElevatedTaskSchedulerAsync(fileName, arguments, adminUser, adminPass, timeout);
            if (taskRes.exitCode == 0 || taskRes.exitCode == 3010)
            {
                return taskRes;
            }
            // Return task result if it attempted execution
            if (!string.IsNullOrEmpty(taskRes.stdout) || !string.IsNullOrEmpty(taskRes.stderr))
            {
                return taskRes;
            }
        }

        // 3. Fallback standard execution
        return await RunProcessAsync(fileName, arguments, timeout);
    }

    private static async Task<(int exitCode, string stdout, string stderr)> RunViaElevatedTaskSchedulerAsync(
        string fileName,
        string arguments,
        string adminUser,
        string adminPass,
        TimeSpan timeout)
    {
        return await Task.Run(async () =>
        {
            string safeDir = GetSafeTempDirectory();
            string jobId = Guid.NewGuid().ToString("N")[..8];
            string taskName = $"NusambaNode_{jobId}";
            string scriptPath = Path.Combine(safeDir, $"run_{jobId}.cmd");
            string logPath = Path.Combine(safeDir, $"out_{jobId}.log");
            string exitCodePath = Path.Combine(safeDir, $"exit_{jobId}.txt");

            try
            {
                // Write batch script
                string scriptContent = $@"@echo off
chcp 65001 >nul
""{fileName}"" {arguments} > ""{logPath}"" 2>&1
echo %ERRORLEVEL% > ""{exitCodePath}""
";
                await File.WriteAllTextAsync(scriptPath, scriptContent);

                // Create elevated task
                var (createCode, createOut, createErr) = await RunProcessAsync(
                    "schtasks.exe",
                    $"/create /tn \"{taskName}\" /tr \"cmd.exe /c \\\"{scriptPath}\\\"\" /ru \"{adminUser}\" /rp \"{adminPass}\" /rl HIGHEST /sc once /st 00:00 /f",
                    TimeSpan.FromSeconds(15)
                );

                if (createCode != 0)
                {
                    return (createCode, createOut, $"Gagal mendaftarkan task elevasi: {createErr}");
                }

                // Run task immediately
                await RunProcessAsync("schtasks.exe", $"/run /tn \"{taskName}\"", TimeSpan.FromSeconds(10));

                // Wait for exit code file or timeout
                var sw = Stopwatch.StartNew();
                while (sw.Elapsed < timeout)
                {
                    if (File.Exists(exitCodePath))
                    {
                        await Task.Delay(500); // brief settling
                        string rawCode = (await File.ReadAllTextAsync(exitCodePath)).Trim();
                        int parsedCode = int.TryParse(rawCode, out int c) ? c : 0;
                        string logOut = File.Exists(logPath) ? await File.ReadAllTextAsync(logPath) : "";
                        return (parsedCode, logOut, "");
                    }
                    await Task.Delay(1000);
                }

                return (-1, "", "Eksekusi task elevasi timeout.");
            }
            catch (Exception ex)
            {
                return (-99, "", $"Kesalahan runner elevasi: {ex.Message}");
            }
            finally
            {
                // Cleanup task and temp files
                try { await RunProcessAsync("schtasks.exe", $"/delete /tn \"{taskName}\" /f", TimeSpan.FromSeconds(5)); } catch { }
                try { File.Delete(scriptPath); } catch { }
                try { File.Delete(exitCodePath); } catch { }
                try { File.Delete(logPath); } catch { }
            }
        });
    }

    private static async Task<(int exitCode, string stdout, string stderr)> RunProcessAsync(string fileName, string arguments, TimeSpan timeout)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = GetSafeTempDirectory()
                };

                var stdout = new System.Text.StringBuilder();
                var stderr = new System.Text.StringBuilder();

                process.OutputDataReceived += (s, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                bool finished = process.WaitForExit((int)timeout.TotalMilliseconds);
                if (!finished)
                {
                    try { process.Kill(true); } catch { }
                    return (-1, stdout.ToString(), "Proses dibatalkan karena melebihi batas waktu (timeout).");
                }

                return (process.ExitCode, stdout.ToString(), stderr.ToString());
            }
            catch (Exception ex)
            {
                return (-99, "", ex.Message);
            }
        });
    }
}

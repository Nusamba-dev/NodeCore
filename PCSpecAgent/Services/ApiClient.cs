using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PCSpecAgent.Models;

namespace PCSpecAgent.Services;

public class ApiClient
{
    private readonly HttpClient _http;
    private readonly AppConfig _config;

    public ApiClient(AppConfig config)
    {
        _config = config;
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(25)
        };
        _http.DefaultRequestHeaders.Add("X-PC-TOKEN", _config.OrgToken);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private string NormalizeUrl(string endpoint)
    {
        string baseUrl = _config.ServerUrl.TrimEnd('/');
        return $"{baseUrl}/{endpoint.TrimStart('/')}";
    }

    public async Task<MetaApiResponse?> GetMetaAsync()
    {
        try
        {
            string url = NormalizeUrl("api/pc-specs/meta");
            var response = await _http.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<MetaApiResponse>(json);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to fetch meta: {ex.Message}");
        }
        return null;
    }

    public async Task<(bool success, string message)> RegisterAsync(PcSpecPayload payload)
    {
        try
        {
            string url = NormalizeUrl("api/pc-specs/register");
            string json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _http.PostAsync(url, content);
            string responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var res = JsonSerializer.Deserialize<ApiResponse>(responseBody);
                return (true, res?.Message ?? "Spesifikasi PC berhasil didaftarkan.");
            }
            else
            {
                var res = JsonSerializer.Deserialize<ApiResponse>(responseBody);
                return (false, res?.Message ?? $"Server error ({response.StatusCode})");
            }
        }
        catch (Exception ex)
        {
            return (false, $"Gagal terhubung ke server: {ex.Message}");
        }
    }

    public async Task<HeartbeatResponse?> SendHeartbeatAsync(string? lanIp = null, string? vpnIp = null, string? vpnType = null, int rdpEnabled = 0)
    {
        try
        {
            string url = NormalizeUrl("api/pc-specs/heartbeat");
            var payload = new HeartbeatPayload
            {
                MachineId = _config.MachineId,
                IpAddress = lanIp ?? "",
                VpnIp = vpnIp,
                VpnType = vpnType,
                RdpEnabled = rdpEnabled,
                AgentVersion = AutoUpdateService.CurrentVersionString,
                Telemetry = new TelemetryInfo
                {
                    UptimeHours = Math.Round((double)Environment.TickCount64 / (1000 * 3600), 1),
                    LanProbe = LanProberService.GetLastResult()
                }
            };

            string json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _http.PostAsync(url, content);
            string body = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                return JsonSerializer.Deserialize<HeartbeatResponse>(body);
            }

            var res = JsonSerializer.Deserialize<ApiResponse>(body);
            if (res?.RequireRegistration == true)
            {
                var fullPayload = HardwareCollector.CollectAll(_config);
                await RegisterAsync(fullPayload);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Heartbeat error: {ex.Message}");
        }

        return null;
    }

    public async Task<bool> ReportJobStatusAsync(int jobId, string status, int progress, string logOutput = "", string errorMessage = "", int? exitCode = null)
    {
        try
        {
            string url = NormalizeUrl("api/pc-specs/jobs/report");
            var payload = new
            {
                job_id = jobId,
                machine_id = _config.MachineId,
                status = status,
                progress = progress,
                log_output = logOutput,
                error_message = errorMessage,
                exit_code = exitCode
            };

            string json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _http.PostAsync(url, content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Report job error: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> DownloadPackageAsync(string downloadUrl, string destinationPath, IProgress<int>? progress = null)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

            using var downloadClient = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            if (!string.IsNullOrEmpty(_config.OrgToken))
            {
                downloadClient.DefaultRequestHeaders.Add("X-PC-TOKEN", _config.OrgToken);
            }

            using var response = await downloadClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;

            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);

            var buffer = new byte[65536];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                totalRead += bytesRead;

                if (totalBytes.HasValue && totalBytes.Value > 0)
                {
                    int pct = (int)((totalRead * 100) / totalBytes.Value);
                    progress?.Report(pct);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Download error: {ex.Message}");
            return false;
        }
    }
}


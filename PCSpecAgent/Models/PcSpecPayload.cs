using System.Text.Json.Serialization;

namespace PCSpecAgent.Models;

public class PcSpecPayload
{
    [JsonPropertyName("machine_id")]
    public string MachineId { get; set; } = "";

    [JsonPropertyName("user_name")]
    public string UserName { get; set; } = "";

    [JsonPropertyName("position")]
    public string Position { get; set; } = "";

    [JsonPropertyName("division")]
    public string Division { get; set; } = "";

    [JsonPropertyName("office")]
    public string Office { get; set; } = "";

    [JsonPropertyName("hostname")]
    public string Hostname { get; set; } = "";

    [JsonPropertyName("os_name")]
    public string OsName { get; set; } = "";

    [JsonPropertyName("os_version")]
    public string OsVersion { get; set; } = "";

    [JsonPropertyName("os_arch")]
    public string OsArch { get; set; } = "";

    [JsonPropertyName("cpu_name")]
    public string CpuName { get; set; } = "";

    [JsonPropertyName("cpu_cores")]
    public int? CpuCores { get; set; }

    [JsonPropertyName("cpu_threads")]
    public int? CpuThreads { get; set; }

    [JsonPropertyName("cpu_speed_ghz")]
    public double? CpuSpeedGhz { get; set; }

    [JsonPropertyName("ram_total_gb")]
    public double? RamTotalGb { get; set; }

    [JsonPropertyName("ram_details")]
    public List<RamSlotInfo> RamDetails { get; set; } = new();

    [JsonPropertyName("gpu_name")]
    public string GpuName { get; set; } = "";

    [JsonPropertyName("gpu_vram_mb")]
    public int? GpuVramMb { get; set; }

    [JsonPropertyName("motherboard")]
    public string Motherboard { get; set; } = "";

    [JsonPropertyName("monitor_resolution")]
    public string MonitorResolution { get; set; } = "";

    [JsonPropertyName("ip_address")]
    public string IpAddress { get; set; } = "";

    [JsonPropertyName("vpn_ip")]
    public string? VpnIp { get; set; }

    [JsonPropertyName("vpn_type")]
    public string? VpnType { get; set; }

    [JsonPropertyName("rdp_enabled")]
    public int RdpEnabled { get; set; } = 0;

    [JsonPropertyName("mac_address")]
    public string MacAddress { get; set; } = "";

    [JsonPropertyName("network_details")]
    public List<NetworkInterfaceInfo> NetworkDetails { get; set; } = new();

    [JsonPropertyName("installed_software")]
    public List<InstalledAppInfo> InstalledSoftware { get; set; } = new();

    [JsonPropertyName("disks")]
    public List<DiskInfo> Disks { get; set; } = new();

    [JsonPropertyName("agent_version")]
    public string AgentVersion { get; set; } = "2.6.2";

    [JsonPropertyName("telemetry")]
    public TelemetryInfo Telemetry { get; set; } = new();
}

public class DiskInfo
{
    [JsonPropertyName("drive_letter")]
    public string DriveLetter { get; set; } = "";

    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = "";

    [JsonPropertyName("total_gb")]
    public double TotalGb { get; set; }

    [JsonPropertyName("free_gb")]
    public double FreeGb { get; set; }

    [JsonPropertyName("health")]
    public string Health { get; set; } = "OK";
}

public class RamSlotInfo
{
    [JsonPropertyName("slot")]
    public string Slot { get; set; } = "";

    [JsonPropertyName("capacity_gb")]
    public double CapacityGb { get; set; }

    [JsonPropertyName("speed_mhz")]
    public int SpeedMhz { get; set; }

    [JsonPropertyName("manufacturer")]
    public string Manufacturer { get; set; } = "";
}

public class NetworkInterfaceInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("ip")]
    public string Ip { get; set; } = "";

    [JsonPropertyName("mac")]
    public string Mac { get; set; } = "";

    [JsonPropertyName("gateway")]
    public string Gateway { get; set; } = "";
}

public class InstalledAppInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("publisher")]
    public string Publisher { get; set; } = "";
}

public class TelemetryInfo
{
    [JsonPropertyName("uptime_hours")]
    public double UptimeHours { get; set; }

    [JsonPropertyName("ram_usage_percent")]
    public int RamUsagePercent { get; set; }

    [JsonPropertyName("lan_probe")]
    public PCSpecAgent.Services.LanProbeResult? LanProbe { get; set; }
}

public class HeartbeatPayload
{
    [JsonPropertyName("machine_id")]
    public string MachineId { get; set; } = "";

    [JsonPropertyName("ip_address")]
    public string IpAddress { get; set; } = "";

    [JsonPropertyName("vpn_ip")]
    public string? VpnIp { get; set; }

    [JsonPropertyName("vpn_type")]
    public string? VpnType { get; set; }

    [JsonPropertyName("rdp_enabled")]
    public int RdpEnabled { get; set; } = 0;

    [JsonPropertyName("agent_version")]
    public string AgentVersion { get; set; } = "2.6.2";

    [JsonPropertyName("telemetry")]
    public TelemetryInfo Telemetry { get; set; } = new();
}

public class ApiResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("require_registration")]
    public bool RequireRegistration { get; set; }
}

public class MetaApiResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("offices")]
    public List<OfficeMeta> Offices { get; set; } = new();

    [JsonPropertyName("divisions")]
    public List<string> Divisions { get; set; } = new();
}

public class OfficeMeta
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("code")]
    public string Code { get; set; } = "";
}

public class PendingJob
{
    [JsonPropertyName("job_id")]
    public int JobId { get; set; }

    [JsonPropertyName("job_type")]
    public string JobType { get; set; } = "install"; // install or uninstall

    [JsonPropertyName("app_name")]
    public string AppName { get; set; } = "";

    [JsonPropertyName("download_url")]
    public string? DownloadUrl { get; set; }

    [JsonPropertyName("command")]
    public string? Command { get; set; }

    [JsonPropertyName("silent_args")]
    public string? SilentArgs { get; set; }

    [JsonPropertyName("admin_user")]
    public string? AdminUser { get; set; }

    [JsonPropertyName("admin_pass")]
    public string? AdminPass { get; set; }

    [JsonPropertyName("elevation_mode")]
    public string? ElevationMode { get; set; }
}

public class AgentUpdateInfo
{
    [JsonPropertyName("latest_version")]
    public string? LatestVersion { get; set; }

    [JsonPropertyName("download_url_x64")]
    public string? DownloadUrlX64 { get; set; }

    [JsonPropertyName("download_url_arm64")]
    public string? DownloadUrlArm64 { get; set; }

    [JsonPropertyName("mandatory")]
    public bool Mandatory { get; set; }

    [JsonPropertyName("changelog")]
    public string? Changelog { get; set; }
}

public class HeartbeatResponse : ApiResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("server_time")]
    public string ServerTime { get; set; } = "";

    [JsonPropertyName("pending_jobs")]
    public List<PendingJob>? PendingJobs { get; set; }

    [JsonPropertyName("agent_update")]
    public AgentUpdateInfo? AgentUpdate { get; set; }
}


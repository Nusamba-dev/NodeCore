using System.Diagnostics;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Win32;
using PCSpecAgent.Models;

namespace PCSpecAgent.Services;

public class HardwareCollector
{
    public static PcSpecPayload CollectAll(AppConfig config)
    {
        var payload = new PcSpecPayload
        {
            MachineId = config.MachineId,
            UserName = config.UserName,
            Position = config.Position,
            Division = config.Division,
            Office = config.Office,
            Hostname = Environment.MachineName,
            AgentVersion = AutoUpdateService.CurrentVersionString,
        };

        CollectOperatingSystem(payload);
        CollectCpu(payload);
        CollectMemory(payload);
        CollectDisks(payload);
        CollectGpuAndDisplay(payload);
        CollectMotherboard(payload);
        CollectNetwork(payload);
        CollectVpnAndRdp(payload);
        CollectInstalledSoftware(payload);
        CollectTelemetry(payload);

        return payload;
    }

    public static (string ips, string mac) GetAllLanIpsAndMac()
    {
        var allIps = new List<string>();
        string primaryMac = "";

        // 1. Check via WMI
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Description, IPAddress, MACAddress, DefaultIPGateway, IPEnabled FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE");

            foreach (ManagementObject obj in searcher.Get())
            {
                string[]? ips = obj["IPAddress"] as string[];
                string mac = obj["MACAddress"]?.ToString() ?? "";

                if (ips != null)
                {
                    foreach (var ip in ips)
                    {
                        if (IsValidLanIpv4(ip) && !allIps.Contains(ip))
                        {
                            allIps.Add(ip);
                            if (string.IsNullOrEmpty(primaryMac) && !string.IsNullOrEmpty(mac))
                            {
                                primaryMac = mac;
                            }
                        }
                    }
                }
            }
        }
        catch { }

        // 2. Double check via .NET NetworkInterface API to ensure no IP is missed (including multiple IPs per NIC)
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                var props = ni.GetIPProperties();
                foreach (var ua in props.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        string ipStr = ua.Address.ToString();
                        if (IsValidLanIpv4(ipStr) && !allIps.Contains(ipStr))
                        {
                            allIps.Add(ipStr);
                            if (string.IsNullOrEmpty(primaryMac))
                            {
                                primaryMac = string.Join(":", ni.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")));
                            }
                        }
                    }
                }
            }
        }
        catch { }

        // Sort so standard LAN IPs (192.168.x / 10.x / 172.x) appear first
        allIps.Sort((a, b) =>
        {
            int Score(string ip) => ip.StartsWith("192.168.") ? 1 : (ip.StartsWith("10.") ? 2 : (ip.StartsWith("172.") ? 3 : 4));
            return Score(a).CompareTo(Score(b));
        });

        return (string.Join(", ", allIps), primaryMac);
    }

    private static bool IsValidLanIpv4(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return false;
        if (!ip.Contains('.')) return false;
        if (ip.StartsWith("127.")) return false; // Loopback
        if (ip.StartsWith("169.254.")) return false; // APIPA self-assigned
        return true;
    }

    private static void CollectOperatingSystem(PcSpecPayload payload)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Caption, Version, OSArchitecture, BuildNumber FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                payload.OsName = obj["Caption"]?.ToString()?.Trim() ?? "Windows";
                string version = obj["Version"]?.ToString() ?? "";
                string build = obj["BuildNumber"]?.ToString() ?? "";
                payload.OsVersion = !string.IsNullOrEmpty(build) ? $"{version} (Build {build})" : version;
                payload.OsArch = obj["OSArchitecture"]?.ToString() ?? (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");
                break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error collecting OS: {ex.Message}");
            payload.OsName = Environment.OSVersion.VersionString;
            payload.OsArch = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";
        }
    }

    private static void CollectCpu(PcSpecPayload payload)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
            foreach (ManagementObject obj in searcher.Get())
            {
                payload.CpuName = obj["Name"]?.ToString()?.Trim() ?? "";
                if (int.TryParse(obj["NumberOfCores"]?.ToString(), out int cores))
                    payload.CpuCores = cores;
                if (int.TryParse(obj["NumberOfLogicalProcessors"]?.ToString(), out int threads))
                    payload.CpuThreads = threads;
                if (double.TryParse(obj["MaxClockSpeed"]?.ToString(), out double clockMhz))
                    payload.CpuSpeedGhz = Math.Round(clockMhz / 1000.0, 2);
                break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error collecting CPU: {ex.Message}");
        }
    }

    private static void CollectMemory(PcSpecPayload payload)
    {
        try
        {
            using (var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    if (ulong.TryParse(obj["TotalPhysicalMemory"]?.ToString(), out ulong totalBytes))
                    {
                        payload.RamTotalGb = Math.Round((double)totalBytes / (1024 * 1024 * 1024), 2);
                    }
                    break;
                }
            }

            using (var searcher = new ManagementObjectSearcher("SELECT BankLabel, DeviceLocator, Capacity, Speed, Manufacturer FROM Win32_PhysicalMemory"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    string slot = obj["DeviceLocator"]?.ToString() ?? obj["BankLabel"]?.ToString() ?? "Slot";
                    double capacityGb = 0;
                    if (ulong.TryParse(obj["Capacity"]?.ToString(), out ulong capBytes))
                    {
                        capacityGb = Math.Round((double)capBytes / (1024 * 1024 * 1024), 2);
                    }

                    int speed = 0;
                    if (int.TryParse(obj["Speed"]?.ToString(), out int spd))
                    {
                        speed = spd;
                    }

                    string manufacturer = obj["Manufacturer"]?.ToString()?.Trim() ?? "";

                    payload.RamDetails.Add(new RamSlotInfo
                    {
                        Slot = slot,
                        CapacityGb = capacityGb,
                        SpeedMhz = speed,
                        Manufacturer = manufacturer
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error collecting Memory: {ex.Message}");
        }
    }

    private static void CollectDisks(PcSpecPayload payload)
    {
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                {
                    string driveName = drive.Name.TrimEnd('\\');
                    double totalGb = Math.Round((double)drive.TotalSize / (1024 * 1024 * 1024), 1);
                    double freeGb = Math.Round((double)drive.AvailableFreeSpace / (1024 * 1024 * 1024), 1);

                    payload.Disks.Add(new DiskInfo
                    {
                        DriveLetter = driveName,
                        TotalGb = totalGb,
                        FreeGb = freeGb,
                        MediaType = DetectMediaType(driveName),
                        Model = GetDiskModelForDrive(driveName),
                        Health = "Good"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error collecting Disks: {ex.Message}");
        }
    }

    private static string DetectMediaType(string driveLetter)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT MediaType, BusType FROM MSFT_PhysicalDisk");
            foreach (ManagementObject obj in searcher.Get())
            {
                if (int.TryParse(obj["MediaType"]?.ToString(), out int mediaType))
                {
                    return mediaType switch
                    {
                        3 => "HDD",
                        4 => "SSD",
                        5 => "SCM",
                        _ => "Fixed Disk"
                    };
                }
            }
        }
        catch { }

        return "Disk";
    }

    private static string GetDiskModelForDrive(string driveLetter)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Model, Caption, DeviceID FROM Win32_DiskDrive");
            foreach (ManagementObject obj in searcher.Get())
            {
                string model = obj["Model"]?.ToString() ?? obj["Caption"]?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(model))
                {
                    return model.Trim();
                }
            }
        }
        catch { }
        return "Internal Disk";
    }

    private static void CollectGpuAndDisplay(PcSpecPayload payload)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate FROM Win32_VideoController");
            foreach (ManagementObject obj in searcher.Get())
            {
                string name = obj["Name"]?.ToString()?.Trim() ?? "";
                if (!string.IsNullOrEmpty(name) && string.IsNullOrEmpty(payload.GpuName))
                {
                    payload.GpuName = name;

                    if (long.TryParse(obj["AdapterRAM"]?.ToString(), out long vramBytes) && vramBytes > 0)
                    {
                        payload.GpuVramMb = (int)(vramBytes / (1024 * 1024));
                    }

                    string width = obj["CurrentHorizontalResolution"]?.ToString() ?? "";
                    string height = obj["CurrentVerticalResolution"]?.ToString() ?? "";
                    string refresh = obj["CurrentRefreshRate"]?.ToString() ?? "";

                    if (!string.IsNullOrEmpty(width) && !string.IsNullOrEmpty(height))
                    {
                        payload.MonitorResolution = $"{width} x {height}" + (!string.IsNullOrEmpty(refresh) ? $" @ {refresh}Hz" : "");
                    }
                }
            }

            if (string.IsNullOrEmpty(payload.MonitorResolution))
            {
                var bounds = Screen.PrimaryScreen?.Bounds;
                if (bounds.HasValue)
                {
                    payload.MonitorResolution = $"{bounds.Value.Width} x {bounds.Value.Height}";
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error collecting GPU: {ex.Message}");
        }
    }

    private static void CollectMotherboard(PcSpecPayload payload)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");
            foreach (ManagementObject obj in searcher.Get())
            {
                string manufacturer = obj["Manufacturer"]?.ToString()?.Trim() ?? "";
                string product = obj["Product"]?.ToString()?.Trim() ?? "";
                payload.Motherboard = $"{manufacturer} {product}".Trim();
                break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error collecting Motherboard: {ex.Message}");
        }
    }

    private static void CollectNetwork(PcSpecPayload payload)
    {
        try
        {
            var (allLanIps, primaryMac) = GetAllLanIpsAndMac();
            payload.IpAddress = allLanIps;
            payload.MacAddress = primaryMac;

            // 1. Gather via WMI
            using var searcher = new ManagementObjectSearcher(
                "SELECT Description, IPAddress, MACAddress, DefaultIPGateway, IPEnabled FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE");
            
            var seenAdapterIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (ManagementObject obj in searcher.Get())
            {
                string[]? ips = obj["IPAddress"] as string[];
                string[]? gateways = obj["DefaultIPGateway"] as string[];
                string mac = obj["MACAddress"]?.ToString() ?? "";
                string desc = obj["Description"]?.ToString() ?? "";

                // Collect ALL valid IPv4 addresses for this adapter!
                var validIpv4List = ips?.Where(IsValidLanIpv4).Distinct().ToList() ?? new List<string>();

                if (validIpv4List.Count > 0)
                {
                    string joinedIps = string.Join(", ", validIpv4List);
                    string gateway = gateways != null ? string.Join(", ", gateways.Where(g => !string.IsNullOrWhiteSpace(g))) : "";

                    foreach (var ip in validIpv4List) seenAdapterIps.Add(ip);

                    payload.NetworkDetails.Add(new NetworkInterfaceInfo
                    {
                        Name = desc,
                        Ip = joinedIps,
                        Mac = mac,
                        Gateway = gateway
                    });
                }
            }

            // 2. Check if any adapter with IP was missed (e.g. corporate VPNs / tunnels)
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                var props = ni.GetIPProperties();
                var extraIps = new List<string>();

                foreach (var ua in props.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        string ipStr = ua.Address.ToString();
                        if (IsValidLanIpv4(ipStr) && !seenAdapterIps.Contains(ipStr))
                        {
                            extraIps.Add(ipStr);
                        }
                    }
                }

                if (extraIps.Count > 0)
                {
                    string gateways = string.Join(", ", props.GatewayAddresses
                        .Where(g => g.Address != null && !g.Address.ToString().Equals("0.0.0.0"))
                        .Select(g => g.Address.ToString()));

                    string mac = string.Join(":", ni.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")));

                    foreach (var ip in extraIps) seenAdapterIps.Add(ip);

                    payload.NetworkDetails.Add(new NetworkInterfaceInfo
                    {
                        Name = ni.Description ?? ni.Name,
                        Ip = string.Join(", ", extraIps),
                        Mac = mac,
                        Gateway = gateways
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error collecting Network: {ex.Message}");
        }
    }

    private static void CollectInstalledSoftware(PcSpecPayload payload)
    {
        var appList = new List<InstalledAppInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string[] registryRoots = {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        foreach (var rootPath in registryRoots)
        {
            ScanUninstallKey(Registry.LocalMachine, rootPath, appList, seen);
        }

        ScanUninstallKey(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", appList, seen);

        payload.InstalledSoftware = appList.OrderBy(a => a.Name).ToList();
    }

    private static void ScanUninstallKey(RegistryKey baseKey, string subKeyPath, List<InstalledAppInfo> appList, HashSet<string> seen)
    {
        try
        {
            using var key = baseKey.OpenSubKey(subKeyPath);
            if (key == null) return;

            foreach (var subKeyName in key.GetSubKeyNames())
            {
                using var appKey = key.OpenSubKey(subKeyName);
                if (appKey == null) continue;

                string? displayName = appKey.GetValue("DisplayName")?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(displayName)) continue;

                int isSystemComponent = Convert.ToInt32(appKey.GetValue("SystemComponent") ?? 0);
                string? parentKeyName = appKey.GetValue("ParentKeyName")?.ToString();
                if (isSystemComponent == 1 || !string.IsNullOrEmpty(parentKeyName)) continue;

                if (!seen.Add(displayName)) continue;

                string version = appKey.GetValue("DisplayVersion")?.ToString()?.Trim() ?? "";
                string publisher = appKey.GetValue("Publisher")?.ToString()?.Trim() ?? "";

                appList.Add(new InstalledAppInfo
                {
                    Name = displayName,
                    Version = version,
                    Publisher = publisher
                });
            }
        }
        catch { }
    }

    private static void CollectTelemetry(PcSpecPayload payload)
    {
        try
        {
            double uptimeHours = (double)Environment.TickCount64 / (1000 * 3600);
            payload.Telemetry.UptimeHours = Math.Round(uptimeHours, 1);
            payload.Telemetry.RamUsagePercent = 0;
            payload.Telemetry.LanProbe = LanProberService.GetLastResult();
        }
        catch { }
    }

    private static void CollectVpnAndRdp(PcSpecPayload payload)
    {
        try
        {
            var (vpnIp, vpnType, rdpEnabled) = GetVpnAndRdpStatus();
            payload.VpnIp = vpnIp;
            payload.VpnType = vpnType;
            payload.RdpEnabled = rdpEnabled;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error detecting VPN/RDP: {ex.Message}");
        }
    }

    public static (string? vpnIp, string? vpnType, int rdpEnabled) GetVpnAndRdpStatus()
    {
        string? detectedVpnIp = null;
        string? detectedVpnType = null;
        int rdpEnabled = 0;

        // 1. Detect RDP status via Windows Registry
        try
        {
            using var rdpKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Terminal Server");
            if (rdpKey != null)
            {
                var val = rdpKey.GetValue("fDenyTSConnections");
                if (val != null && Convert.ToInt32(val) == 0)
                {
                    rdpEnabled = 1;
                }
            }
        }
        catch { }

        // 2. Detect Headscale / Tailscale / WireGuard VPN Adapter & IP
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                string desc = (ni.Description ?? "").ToLowerInvariant();
                string name = (ni.Name ?? "").ToLowerInvariant();

                bool isTailscale = desc.Contains("tailscale") || name.Contains("tailscale");
                bool isWireGuard = desc.Contains("wireguard") || name.Contains("wireguard");
                bool isOpenVpn = desc.Contains("tap-windows") || desc.Contains("openvpn") || name.Contains("openvpn");

                var ipProps = ni.GetIPProperties();
                foreach (var ua in ipProps.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        string ipStr = ua.Address.ToString();
                        byte[] bytes = ua.Address.GetAddressBytes();

                        // Tailscale / Headscale CGNAT range: 100.64.0.0/10 (100.64.0.0 to 100.127.255.255)
                        bool isCgnatTailscale = (bytes.Length == 4 && bytes[0] == 100 && (bytes[1] >= 64 && bytes[1] <= 127));

                        if (isTailscale || isCgnatTailscale)
                        {
                            detectedVpnIp = ipStr;
                            detectedVpnType = "Headscale / Tailscale";
                            break;
                        }
                        else if (isWireGuard && detectedVpnIp == null)
                        {
                            detectedVpnIp = ipStr;
                            detectedVpnType = "WireGuard";
                        }
                        else if (isOpenVpn && detectedVpnIp == null)
                        {
                            detectedVpnIp = ipStr;
                            detectedVpnType = "OpenVPN";
                        }
                    }
                }

                if (detectedVpnIp != null && detectedVpnType == "Headscale / Tailscale")
                {
                    break;
                }
            }
        }
        catch { }

        return (detectedVpnIp, detectedVpnType, rdpEnabled);
    }
}

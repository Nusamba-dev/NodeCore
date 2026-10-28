using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using PCSpecAgent.Models;

namespace PCSpecAgent.Services;

public class LanNeighborInfo
{
    public string Ip { get; set; } = "";
    public string Mac { get; set; } = "";
    public string Vendor { get; set; } = "";
    public string DeviceCategory { get; set; } = "unknown"; // smartphone, laptop, printer, router, unknown
    public string Hostname { get; set; } = "";
}

public class LanProbeResult
{
    public string GatewayIp { get; set; } = "";
    public string GatewayMac { get; set; } = "";
    public string GatewayVendor { get; set; } = "";
    public double GatewayPingMs { get; set; } = -1;
    public double InternetPingMs { get; set; } = -1;
    public string? WifiSsid { get; set; }
    public int? WifiSignalPct { get; set; }
    public List<LanNeighborInfo> Neighbors { get; set; } = new();
    public DateTime ProbedAt { get; set; } = DateTime.UtcNow;
}

public static class LanProberService
{
    private static LanProbeResult? _cachedResult = null;
    private static DateTime _lastProbeTime = DateTime.MinValue;
    private static readonly object _lock = new();
    private static bool _isProbing = false;

    // Cache duration: 60 seconds
    private static readonly TimeSpan CacheExpiry = TimeSpan.FromSeconds(60);

    public static LanProbeResult GetLastResult()
    {
        lock (_lock)
        {
            if (_cachedResult != null && (DateTime.UtcNow - _lastProbeTime) < CacheExpiry)
            {
                return _cachedResult;
            }
        }

        // Trigger background probe if expired or empty
        TriggerBackgroundProbe();

        lock (_lock)
        {
            return _cachedResult ?? new LanProbeResult();
        }
    }

    public static void TriggerBackgroundProbe()
    {
        lock (_lock)
        {
            if (_isProbing) return;
            _isProbing = true;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var res = await PerformProbeAsync();
                lock (_lock)
                {
                    _cachedResult = res;
                    _lastProbeTime = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LanProberService error: {ex.Message}");
            }
            finally
            {
                lock (_lock)
                {
                    _isProbing = false;
                }
            }
        });
    }

    public static async Task<LanProbeResult> PerformProbeAsync()
    {
        var result = new LanProbeResult();

        // 1. Identify primary active IPv4 adapter and its default gateway
        string? activeGatewayIp = null;
        string? localIp = null;

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                var ipProps = ni.GetIPProperties();
                var gateways = ipProps.GatewayAddresses;

                foreach (var gw in gateways)
                {
                    if (gw.Address.AddressFamily == AddressFamily.InterNetwork && !gw.Address.ToString().StartsWith("0.0.0.0"))
                    {
                        activeGatewayIp = gw.Address.ToString();
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(activeGatewayIp))
                {
                    var unicast = ipProps.UnicastAddresses
                        .FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork && !u.Address.ToString().StartsWith("169.254."));
                    if (unicast != null)
                    {
                        localIp = unicast.Address.ToString();
                    }
                    break;
                }
            }
        }
        catch { }

        result.GatewayIp = activeGatewayIp ?? "";

        // 2. Ping Gateway & Ping Internet asynchronously
        var pingGwTask = PingHostAsync(result.GatewayIp, 800);
        var pingInetTask = PingHostAsync("8.8.8.8", 1200);

        await Task.WhenAll(pingGwTask, pingInetTask);
        result.GatewayPingMs = await pingGwTask;
        result.InternetPingMs = await pingInetTask;

        // 3. Detect Wi-Fi SSID & Signal if on Wi-Fi
        try
        {
            var (ssid, signal) = DetectWifiInfo();
            result.WifiSsid = ssid;
            result.WifiSignalPct = signal;
        }
        catch { }

        // 4. Scan ARP table to discover connected network devices (HP, Laptop, Router, etc.)
        try
        {
            result.Neighbors = ParseArpTable(result.GatewayIp, localIp);
            
            // Resolve gateway MAC and Vendor from neighbors if present
            var gwNeighbor = result.Neighbors.FirstOrDefault(n => n.Ip == result.GatewayIp);
            if (gwNeighbor != null)
            {
                result.GatewayMac = gwNeighbor.Mac;
                result.GatewayVendor = gwNeighbor.Vendor;
                gwNeighbor.DeviceCategory = "router";
            }
            else if (!string.IsNullOrEmpty(result.GatewayIp))
            {
                // Attempt direct SendARP for gateway
                string gwMac = ResolveMacAddressViaArp(result.GatewayIp);
                if (!string.IsNullOrEmpty(gwMac))
                {
                    result.GatewayMac = gwMac;
                    result.GatewayVendor = ResolveVendorOUI(gwMac);
                }
            }
        }
        catch { }

        return result;
    }

    private static async Task<double> PingHostAsync(string hostOrIp, int timeoutMs)
    {
        if (string.IsNullOrWhiteSpace(hostOrIp)) return -1;
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(hostOrIp, timeoutMs);
            if (reply.Status == IPStatus.Success)
            {
                return (double)reply.RoundtripTime;
            }
        }
        catch { }
        return -1;
    }

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern int SendARP(int destIp, int srcIp, byte[] macAddr, ref uint physicalAddrLen);

    public static string ResolveMacAddressViaArp(string ipAddress)
    {
        try
        {
            if (IPAddress.TryParse(ipAddress, out var ip))
            {
                byte[] macBytes = new byte[6];
                uint len = (uint)macBytes.Length;
                int destIp = BitConverter.ToInt32(ip.GetAddressBytes(), 0);
                if (SendARP(destIp, 0, macBytes, ref len) == 0)
                {
                    return string.Join(":", macBytes.Take((int)len).Select(b => b.ToString("X2")));
                }
            }
        }
        catch { }
        return "";
    }

    private static List<LanNeighborInfo> ParseArpTable(string gatewayIp, string? myLocalIp)
    {
        var list = new List<LanNeighborInfo>();
        var seenIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "arp",
                Arguments = "-a",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(1000);

                var regex = new Regex(@"([0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3})\s+([0-9a-fA-F]{2}[-:][0-9a-fA-F]{2}[-:][0-9a-fA-F]{2}[-:][0-9a-fA-F]{2}[-:][0-9a-fA-F]{2}[-:][0-9a-fA-F]{2})\s+(\w+)", RegexOptions.Compiled);
                var matches = regex.Matches(output);

                foreach (Match m in matches)
                {
                    string ip = m.Groups[1].Value.Trim();
                    string macRaw = m.Groups[2].Value.Trim().Replace("-", ":").ToUpperInvariant();
                    string type = m.Groups[3].Value.Trim();

                    // Filter out multicast and broadcast
                    if (ip.StartsWith("224.") || ip.StartsWith("239.") || ip.EndsWith(".255") || ip == "255.255.255.255")
                        continue;
                    if (macRaw == "FF:FF:FF:FF:FF:FF" || macRaw.StartsWith("01:00:5E"))
                        continue;
                    if (ip == myLocalIp)
                        continue;

                    if (!seenIps.Add(ip)) continue;

                    string vendor = ResolveVendorOUI(macRaw);
                    string category = ClassifyDeviceCategory(vendor, ip == gatewayIp);

                    list.Add(new LanNeighborInfo
                    {
                        Ip = ip,
                        Mac = macRaw,
                        Vendor = vendor,
                        DeviceCategory = category
                    });
                }
            }
        }
        catch { }

        return list;
    }

    public static string ResolveVendorOUI(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac) || mac.Length < 8) return "Unknown Vendor";

        string prefix = mac.Substring(0, 8).ToUpperInvariant();

        // Huawei
        if (prefix.StartsWith("48:D2:24") || prefix.StartsWith("E0:24:7F") || prefix.StartsWith("00:1E:10") ||
            prefix.StartsWith("00:25:9E") || prefix.StartsWith("20:08:89") || prefix.StartsWith("70:54:F5") ||
            prefix.StartsWith("80:B6:86") || prefix.StartsWith("9C:28:40") || prefix.StartsWith("AC:E8:7B") ||
            prefix.StartsWith("D4:6E:5C") || prefix.StartsWith("F4:C7:14"))
            return "Huawei Technologies";

        // ZTE
        if (prefix.StartsWith("DC:02:8E") || prefix.StartsWith("00:1E:73") || prefix.StartsWith("34:E0:CF") ||
            prefix.StartsWith("70:9F:26") || prefix.StartsWith("84:74:2A") || prefix.StartsWith("98:F5:37") ||
            prefix.StartsWith("B0:75:D5") || prefix.StartsWith("CC:7B:35") || prefix.StartsWith("D8:74:95") ||
            prefix.StartsWith("E8:65:D4") || prefix.StartsWith("F8:35:DD"))
            return "ZTE Corporation";

        // TP-Link
        if (prefix.StartsWith("50:D4:F7") || prefix.StartsWith("00:0A:EB") || prefix.StartsWith("00:14:78") ||
            prefix.StartsWith("00:19:E0") || prefix.StartsWith("00:21:27") || prefix.StartsWith("14:CC:20") ||
            prefix.StartsWith("30:B5:C2") || prefix.StartsWith("54:AF:97") || prefix.StartsWith("60:32:B1") ||
            prefix.StartsWith("74:DA:38") || prefix.StartsWith("84:16:F9") || prefix.StartsWith("90:F6:52") ||
            prefix.StartsWith("A4:2B:B0") || prefix.StartsWith("B0:4E:26") || prefix.StartsWith("C0:4A:00") ||
            prefix.StartsWith("D8:0D:17") || prefix.StartsWith("E8:48:B8") || prefix.StartsWith("F4:EC:38"))
            return "TP-Link Corporation";

        // MikroTik
        if (prefix.StartsWith("00:0C:42") || prefix.StartsWith("48:8F:5A") || prefix.StartsWith("6C:3B:6B") ||
            prefix.StartsWith("78:9A:18") || prefix.StartsWith("2C:C8:1B") || prefix.StartsWith("4C:5E:0C") ||
            prefix.StartsWith("64:D1:54") || prefix.StartsWith("B8:69:F4") || prefix.StartsWith("CC:2D:E0") ||
            prefix.StartsWith("D4:CA:6D") || prefix.StartsWith("E4:8D:8C") || prefix.StartsWith("F4:1E:26"))
            return "MikroTik RouterOS";

        // D-Link
        if (prefix.StartsWith("00:05:5D") || prefix.StartsWith("00:0D:88") || prefix.StartsWith("00:11:95") ||
            prefix.StartsWith("00:13:46") || prefix.StartsWith("00:15:E9") || prefix.StartsWith("14:D6:4D") ||
            prefix.StartsWith("28:10:7B") || prefix.StartsWith("70:62:B8") || prefix.StartsWith("B0:C5:54") ||
            prefix.StartsWith("C8:D3:A3"))
            return "D-Link Systems";

        // Apple (iPhone, iPad, Mac)
        if (prefix.StartsWith("00:03:93") || prefix.StartsWith("00:05:02") || prefix.StartsWith("00:10:FA") ||
            prefix.StartsWith("00:16:CB") || prefix.StartsWith("00:17:F2") || prefix.StartsWith("00:19:E3") ||
            prefix.StartsWith("00:1C:B3") || prefix.StartsWith("00:23:12") || prefix.StartsWith("00:26:08") ||
            prefix.StartsWith("BC:D1:D3") || prefix.StartsWith("F0:18:98") || prefix.StartsWith("F8:FF:C2") ||
            prefix.StartsWith("AC:BC:32") || prefix.StartsWith("DC:A9:04") || prefix.StartsWith("3C:22:FB"))
            return "Apple (iPhone/Mac)";

        // Samsung
        if (prefix.StartsWith("00:07:AB") || prefix.StartsWith("00:12:47") || prefix.StartsWith("00:15:99") ||
            prefix.StartsWith("00:17:C9") || prefix.StartsWith("00:21:19") || prefix.StartsWith("34:14:5F") ||
            prefix.StartsWith("40:4E:36") || prefix.StartsWith("50:32:75") || prefix.StartsWith("54:88:0E") ||
            prefix.StartsWith("78:47:1D") || prefix.StartsWith("88:32:9B") || prefix.StartsWith("94:65:2D") ||
            prefix.StartsWith("A0:82:1F") || prefix.StartsWith("B8:5A:73") || prefix.StartsWith("CC:07:AB") ||
            prefix.StartsWith("E8:50:8B") || prefix.StartsWith("F8:04:2E") || prefix.StartsWith("00:26:37"))
            return "Samsung Electronics";

        // Xiaomi
        if (prefix.StartsWith("00:EC:0A") || prefix.StartsWith("18:59:36") || prefix.StartsWith("28:6C:07") ||
            prefix.StartsWith("34:80:B3") || prefix.StartsWith("44:23:7C") || prefix.StartsWith("58:44:98") ||
            prefix.StartsWith("64:CC:2E") || prefix.StartsWith("78:02:F8") || prefix.StartsWith("7C:49:EB") ||
            prefix.StartsWith("80:48:99") || prefix.StartsWith("8C:BE:BE") || prefix.StartsWith("98:FA:E3") ||
            prefix.StartsWith("A4:44:D1") || prefix.StartsWith("AC:C1:EE") || prefix.StartsWith("B0:E2:35") ||
            prefix.StartsWith("D4:97:0B") || prefix.StartsWith("E4:46:DA") || prefix.StartsWith("FC:64:3A"))
            return "Xiaomi Communications";

        // Oppo / Realme
        if (prefix.StartsWith("24:E2:71") || prefix.StartsWith("2C:59:8A") || prefix.StartsWith("38:BC:1A") ||
            prefix.StartsWith("48:2C:A0") || prefix.StartsWith("50:8F:4C") || prefix.StartsWith("68:3E:34") ||
            prefix.StartsWith("7C:38:AD") || prefix.StartsWith("84:D4:7E") || prefix.StartsWith("94:87:E0") ||
            prefix.StartsWith("A4:3B:B7") || prefix.StartsWith("B4:CD:27") || prefix.StartsWith("C0:8C:60") ||
            prefix.StartsWith("D4:4B:5E") || prefix.StartsWith("DC:72:9B") || prefix.StartsWith("E8:8D:28"))
            return "Oppo / Realme";

        // Vivo
        if (prefix.StartsWith("10:2C:6B") || prefix.StartsWith("14:7D:C5") || prefix.StartsWith("18:1E:78") ||
            prefix.StartsWith("20:F4:1B") || prefix.StartsWith("38:95:C7") || prefix.StartsWith("40:45:DA") ||
            prefix.StartsWith("50:5F:B6") || prefix.StartsWith("68:DB:54") || prefix.StartsWith("70:BC:10") ||
            prefix.StartsWith("88:83:5D") || prefix.StartsWith("A0:93:47") || prefix.StartsWith("C4:7F:51"))
            return "Vivo Mobile";

        // Printers
        if (prefix.StartsWith("00:00:48") || prefix.StartsWith("00:00:85") || prefix.StartsWith("00:01:E6") ||
            prefix.StartsWith("00:03:7F") || prefix.StartsWith("00:0D:9D") || prefix.StartsWith("00:1E:8F") ||
            prefix.StartsWith("00:26:73") || prefix.StartsWith("10:1F:74") || prefix.StartsWith("18:A9:58"))
            return "Epson / Canon Printer";

        // Intel / Realtek (PC / Laptop)
        if (prefix.StartsWith("00:1B:21") || prefix.StartsWith("00:1E:67") || prefix.StartsWith("00:21:6A") ||
            prefix.StartsWith("00:24:D7") || prefix.StartsWith("00:28:F8") || prefix.StartsWith("24:4B:FE") ||
            prefix.StartsWith("34:13:E8") || prefix.StartsWith("3C:F8:62") || prefix.StartsWith("40:B0:34") ||
            prefix.StartsWith("5C:80:B6") || prefix.StartsWith("68:05:CA") || prefix.StartsWith("8C:16:45") ||
            prefix.StartsWith("A4:4C:C8") || prefix.StartsWith("E8:B1:FC") || prefix.StartsWith("F8:63:3F"))
            return "Intel (PC/Laptop)";

        if (prefix.StartsWith("00:E0:4C") || prefix.StartsWith("00:E0:22") || prefix.StartsWith("52:54:00") ||
            prefix.StartsWith("00:1A:64") || prefix.StartsWith("10:FF:E0") || prefix.StartsWith("A8:5E:45") ||
            prefix.StartsWith("1C:83:41") || prefix.StartsWith("94:C6:91") || prefix.StartsWith("60:12:8B") ||
            prefix.StartsWith("18:C0:4D") || prefix.StartsWith("9C:5A:44") || prefix.StartsWith("00:11:32"))
            return "Realtek / Generic NIC";

        return "Standard Network Device";
    }

    private static string ClassifyDeviceCategory(string vendor, bool isGateway)
    {
        if (isGateway) return "router";
        if (vendor.Contains("Huawei") || vendor.Contains("ZTE") || vendor.Contains("TP-Link") || vendor.Contains("MikroTik") || vendor.Contains("D-Link"))
            return "router";
        if (vendor.Contains("Apple") || vendor.Contains("Samsung") || vendor.Contains("Xiaomi") || vendor.Contains("Oppo") || vendor.Contains("Vivo"))
            return "smartphone";
        if (vendor.Contains("Printer"))
            return "printer";
        if (vendor.Contains("Intel") || vendor.Contains("Realtek"))
            return "laptop";
        return "unknown";
    }

    private static (string? ssid, int? signal) DetectWifiInfo()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = "wlan show interfaces",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(800);

                string? ssid = null;
                int? signal = null;

                var ssidMatch = Regex.Match(output, @"^\s*SSID\s*:\s*(.+)$", RegexOptions.Multiline);
                if (ssidMatch.Success)
                {
                    ssid = ssidMatch.Groups[1].Value.Trim();
                }

                var signalMatch = Regex.Match(output, @"^\s*Signal\s*:\s*(\d+)%", RegexOptions.Multiline);
                if (signalMatch.Success && int.TryParse(signalMatch.Groups[1].Value, out int sigVal))
                {
                    signal = sigVal;
                }

                return (ssid, signal);
            }
        }
        catch { }

        return (null, null);
    }
}

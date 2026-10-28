using PCSpecAgent.Forms;
using PCSpecAgent.Models;
using PCSpecAgent.Services;

namespace PCSpecAgent;

public class TrayAppContext : ApplicationContext
{
    private readonly AppConfig _config;
    private readonly NotifyIcon _trayIcon;
    private readonly System.Windows.Forms.Timer _heartbeatTimer;
    private readonly System.Windows.Forms.Timer _specRefreshTimer;
    private readonly System.Windows.Forms.Timer _remoteCheckTimer;
    private readonly ApiClient _apiClient;
    private readonly WebRemoteService _webRemoteService;
    private ToolStripMenuItem _menuStatusItem = null!;

    public TrayAppContext(AppConfig config)
    {
        _config = config;
        _apiClient = new ApiClient(_config);
        _webRemoteService = new WebRemoteService(_config);

        Icon trayIconGraphic = SystemIcons.Application;
        try
        {
            var exeIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (exeIcon != null)
            {
                trayIconGraphic = exeIcon;
            }
        }
        catch { }

        _trayIcon = new NotifyIcon
        {
            Icon = trayIconGraphic,
            Text = $"Nusamba NodeCore - {_config.UserName}",
            Visible = true,
            ContextMenuStrip = CreateContextMenu()
        };

        _trayIcon.DoubleClick += (s, e) => OpenProfileSettings();

        // Remote assistance incoming session check timer: every 3 seconds (3,000 ms)
        _remoteCheckTimer = new System.Windows.Forms.Timer
        {
            Interval = 3000
        };
        _remoteCheckTimer.Tick += async (s, e) =>
        {
            await _webRemoteService.CheckAndHandleIncomingSessionAsync();
        };
        _remoteCheckTimer.Start();

        // Heartbeat timer: every 5 minutes (300,000 ms)
        _heartbeatTimer = new System.Windows.Forms.Timer
        {
            Interval = 5 * 60 * 1000
        };
        _heartbeatTimer.Tick += async (s, e) => await OnHeartbeatTickAsync();
        _heartbeatTimer.Start();

        // Full spec refresh timer: every 6 hours
        _specRefreshTimer = new System.Windows.Forms.Timer
        {
            Interval = 6 * 60 * 60 * 1000
        };
        _specRefreshTimer.Tick += async (s, e) => await RefreshSpecsAsync(showNotification: false);
        _specRefreshTimer.Start();

        // Initial heartbeat right away
        _ = Task.Run(async () =>
        {
            await Task.Delay(2000);
            await OnHeartbeatTickAsync();
        });

        _trayIcon.ShowBalloonTip(
            3000,
            "Nusamba NodeCore Agent Aktif",
            $"Memantau PC ({_config.UserName} - {_config.Office}). Heartbeat & Remote Assistance Siap.",
            ToolTipIcon.Info
        );
    }

    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip();

        _menuStatusItem = new ToolStripMenuItem($"🟢 Status: Online ({_config.UserName})")
        {
            Enabled = false,
            Font = new Font(menu.Font, FontStyle.Bold)
        };
        menu.Items.Add(_menuStatusItem);

        menu.Items.Add(new ToolStripSeparator());

        var rescanItem = new ToolStripMenuItem("🔄 Kirim Ulang Spek Sekarang", null, async (s, e) =>
        {
            await RefreshSpecsAsync(showNotification: true);
        });
        menu.Items.Add(rescanItem);

        var settingsItem = new ToolStripMenuItem("⚙️ Ubah Profil Pengguna...", null, (s, e) =>
        {
            OpenProfileSettings();
        });
        menu.Items.Add(settingsItem);

        var aboutItem = new ToolStripMenuItem("ℹ️ Informasi Perangkat", null, (s, e) =>
        {
            ShowAboutDialog();
        });
        menu.Items.Add(aboutItem);

        var updateItem = new ToolStripMenuItem("⚡ Periksa Pembaruan...", null, async (s, e) =>
        {
            _trayIcon.ShowBalloonTip(2000, "Nusamba NodeCore", "Memeriksa pembaruan di server...", ToolTipIcon.Info);
            var hb = await _apiClient.SendHeartbeatAsync();
            if (hb?.AgentUpdate != null)
            {
                await AutoUpdateService.CheckAndApplyUpdateAsync(
                    hb.AgentUpdate,
                    _apiClient,
                    (msg) => _trayIcon.ShowBalloonTip(3500, "Nusamba NodeCore Update", msg, ToolTipIcon.Info),
                    isManualCheck: true
                );
            }
            else
            {
                _trayIcon.ShowBalloonTip(2500, "Nusamba NodeCore", $"Nusamba NodeCore sudah versi terbaru (v{AutoUpdateService.CurrentVersionString}).", ToolTipIcon.Info);
            }
        });
        menu.Items.Add(updateItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("❌ Keluar", null, (s, e) =>
        {
            var res = MessageBox.Show(
                "Jika Anda menutup aplikasi ini, PC akan tercatat Offline di dashboard admin.\n\nApakah Anda yakin ingin keluar?",
                "Konfirmasi Keluar",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );

            if (res == DialogResult.Yes)
            {
                ExitApplication();
            }
        });
        menu.Items.Add(exitItem);

        return menu;
    }

    private async Task OnHeartbeatTickAsync()
    {
        // Proactively probe LAN gateway, internet latency & Wi-Fi neighbors
        LanProberService.TriggerBackgroundProbe();

        var (allLanIps, _) = HardwareCollector.GetAllLanIpsAndMac();
        var (vpnIp, vpnType, rdpEnabled) = HardwareCollector.GetVpnAndRdpStatus();
        var hbRes = await _apiClient.SendHeartbeatAsync(allLanIps, vpnIp, vpnType, rdpEnabled);
        if (hbRes != null)
        {
            _config.LastHeartbeatAt = DateTime.UtcNow;
            ConfigService.Save(_config);
            string vpnDisplay = !string.IsNullOrEmpty(vpnIp) ? $" [VPN: {vpnIp}]" : "";
            string ipDisplay = !string.IsNullOrEmpty(allLanIps) ? $" [IP: {allLanIps}]" : "";
            _menuStatusItem.Text = $"🟢 Status: Online{vpnDisplay}{ipDisplay} ({DateTime.Now:HH:mm})";

            // Process remote pending deployment jobs if any
            if (hbRes.PendingJobs != null && hbRes.PendingJobs.Count > 0)
            {
                _ = Task.Run(async () =>
                {
                    await JobExecutor.ProcessJobsAsync(hbRes.PendingJobs, _apiClient, _config, (statusMsg) =>
                    {
                        try
                        {
                            _trayIcon.ShowBalloonTip(3000, "Tugas Remote IT", statusMsg, ToolTipIcon.Info);
                        }
                        catch { }
                    });
                });
            }

            // Self-Update check: Automatically upgrade Nusamba NodeCore if newer version released
            if (hbRes.AgentUpdate != null)
            {
                _ = Task.Run(async () =>
                {
                    await AutoUpdateService.CheckAndApplyUpdateAsync(
                        hbRes.AgentUpdate,
                        _apiClient,
                        (msg) =>
                        {
                            try { _trayIcon.ShowBalloonTip(3000, "Nusamba NodeCore Update", msg, ToolTipIcon.Info); } catch { }
                        }
                    );
                });
            }
        }
        else
        {
            _menuStatusItem.Text = $"🔴 Status: Gangguan Koneksi ({DateTime.Now:HH:mm})";
        }
    }

    private async Task RefreshSpecsAsync(bool showNotification)
    {
        if (showNotification)
        {
            _trayIcon.ShowBalloonTip(2000, "Memindai Spek PC", "Sedang mengumpulkan spesifikasi terbaru...", ToolTipIcon.Info);
        }

        try
        {
            var payload = await Task.Run(() => HardwareCollector.CollectAll(_config));
            var (success, message) = await _apiClient.RegisterAsync(payload);

            if (success)
            {
                _config.LastRegisteredAt = DateTime.UtcNow;
                _config.LastHeartbeatAt = DateTime.UtcNow;
                ConfigService.Save(_config);

                if (showNotification)
                {
                    _trayIcon.ShowBalloonTip(3000, "Spek Terkirim", "Data spesifikasi terbaru berhasil dikirim ke server perusahaan.", ToolTipIcon.Info);
                }
            }
            else if (showNotification)
            {
                MessageBox.Show($"Gagal mengirim spek: {message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            if (showNotification)
            {
                MessageBox.Show($"Terjadi kesalahan: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void OpenProfileSettings()
    {
        using var setupForm = new SetupForm(_config);
        if (setupForm.ShowDialog() == DialogResult.OK)
        {
            _trayIcon.Text = $"Nusamba NodeCore - {_config.UserName}";
            _menuStatusItem.Text = $"🟢 Nusamba NodeCore: Online ({_config.UserName})";
        }
    }

    private void ShowAboutDialog()
    {
        var (allLanIps, mac) = HardwareCollector.GetAllLanIpsAndMac();
        MessageBox.Show(
            $"Nusamba NodeCore Agent v3.0\n" +
            $"Endpoint Management & Remote Operations\n" +
            $"BPR Nusamba\n\n" +
            $"Hostname: {Environment.MachineName}\n" +
            $"IP LAN: {allLanIps}\n" +
            $"MAC: {mac}\n" +
            $"Pengguna: {_config.UserName}\n" +
            $"Jabatan: {_config.Position}\n" +
            $"Divisi: {_config.Division}\n" +
            $"Kantor: {_config.Office}\n" +
            $"Server: {_config.ServerUrl}\n" +
            $"Terakhir Heartbeat: {(_config.LastHeartbeatAt.HasValue ? _config.LastHeartbeatAt.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") : "-")}",
            "Nusamba NodeCore Endpoint",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information
        );
    }

    private void ExitApplication()
    {
        _remoteCheckTimer.Stop();
        _remoteCheckTimer.Dispose();
        _heartbeatTimer.Stop();
        _heartbeatTimer.Dispose();
        _specRefreshTimer.Stop();
        _specRefreshTimer.Dispose();

        _trayIcon.Visible = false;
        _trayIcon.Dispose();

        Application.Exit();
    }
}

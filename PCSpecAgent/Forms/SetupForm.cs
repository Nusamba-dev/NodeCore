using System.Drawing.Drawing2D;
using PCSpecAgent.Models;
using PCSpecAgent.Services;

namespace PCSpecAgent.Forms;

public class SetupForm : Form
{
    private readonly AppConfig _config;
    private TextBox _txtUserName = null!;
    private TextBox _txtPosition = null!;
    private ComboBox _cmbDivision = null!;
    private ComboBox _cmbOffice = null!;
    private TextBox _txtServerUrl = null!;
    private TextBox _txtOrgToken = null!;
    private CheckBox _chkAutoStart = null!;
    private Button _btnSubmit = null!;
    private Label _lblStatus = null!;
    private ProgressBar _progressBar = null!;

    public SetupForm(AppConfig config)
    {
        _config = config;
        InitializeComponent();
        LoadExistingData();
        _ = LoadMetadataFromServerAsync();
    }

    private void InitializeComponent()
    {
        Text = "Nusamba NodeCore - Registrasi Endpoint PC";
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        Size = new Size(540, 680);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        BackColor = Color.FromArgb(248, 250, 252); // Light background
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

        // Header Panel
        var pnlHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 85,
            BackColor = Color.FromArgb(15, 23, 42), // Sleek Dark Slate
            Padding = new Padding(24, 16, 24, 16)
        };

        var lblHeaderTitle = new Label
        {
            Text = "🛡️ Nusamba NodeCore Agent Setup",
            Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(20, 14)
        };

        var lblHeaderSubtitle = new Label
        {
            Text = "Silakan lengkapi profil pengguna untuk registrasi Nusamba NodeCore.",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(56, 189, 248),
            AutoSize = true,
            Location = new Point(22, 44)
        };

        pnlHeader.Controls.Add(lblHeaderTitle);
        pnlHeader.Controls.Add(lblHeaderSubtitle);
        Controls.Add(pnlHeader);

        // Main Content Panel
        var pnlContent = new Panel
        {
            Location = new Point(24, 100),
            Size = new Size(476, 520),
            BackColor = Color.White,
            Padding = new Padding(20)
        };
        pnlContent.Paint += (s, e) =>
        {
            // Border around card
            using var pen = new Pen(Color.FromArgb(226, 232, 240), 1);
            e.Graphics.DrawRectangle(pen, 0, 0, pnlContent.Width - 1, pnlContent.Height - 1);
        };
        Controls.Add(pnlContent);

        int curY = 16;
        int fieldWidth = 434;

        // 1. Nama Pengguna
        var lblName = CreateFieldLabel("Nama Lengguna (PC Milik Siapa): *", 20, curY);
        pnlContent.Controls.Add(lblName);
        curY += 24;

        _txtUserName = CreateTextBox(20, curY, fieldWidth);
        pnlContent.Controls.Add(_txtUserName);
        curY += 44;

        // 2. Jabatan
        var lblPos = CreateFieldLabel("Jabatan: *", 20, curY);
        pnlContent.Controls.Add(lblPos);
        curY += 24;

        _txtPosition = CreateTextBox(20, curY, fieldWidth);
        pnlContent.Controls.Add(_txtPosition);
        curY += 44;

        // 3. Divisi
        var lblDiv = CreateFieldLabel("Divisi / Bagian: *", 20, curY);
        pnlContent.Controls.Add(lblDiv);
        curY += 24;

        _cmbDivision = CreateComboBox(20, curY, fieldWidth);
        _cmbDivision.Items.AddRange(new object[] {
            "Operasional & Pelayanan",
            "Bisnis & Pemasaran",
            "Kredit & Pembiayaan",
            "Teknologi Informasi (IT)",
            "Kepatuhan, Manajemen Risiko & Hukum",
            "Satuan Kerja Audit Intern (SKAI)",
            "SDM & Umum",
            "Akuntansi & Keuangan",
            "Manajemen / Direksi"
        });
        pnlContent.Controls.Add(_cmbDivision);
        curY += 44;

        // 4. Kantor / Cabang
        var lblOff = CreateFieldLabel("Kantor / Cabang: *", 20, curY);
        pnlContent.Controls.Add(lblOff);
        curY += 24;

        _cmbOffice = CreateComboBox(20, curY, fieldWidth);
        _cmbOffice.Items.AddRange(new object[] {
            "Kantor Pusat",
            "Kantor Cabang Wates",
            "Kantor Cabang Sentolo",
            "Kantor Cabang Kalibawang",
            "Kantor Cabang Galur",
            "Kantor Cabang Temon",
            "Kantor Cabang Cepiring",
            "Kantor Cabang Ungaran",
            "Kantor Cabang Kaliwungu",
            "Kantor Cabang Boja",
            "Kantor Cabang Ambarawa",
            "Kantor Cabang Bergas",
            "Kantor Cabang Adiwerna",
            "Kantor Cabang Purwokerto",
            "Kantor Cabang Cilacap",
            "Kantor Cabang Pecangaan",
            "Kantor Cabang Kudus",
            "Kantor Cabang Bangsri",
            "Kantor Cabang Ampel",
            "Kantor Cabang Salatiga",
            "Kantor Cabang Banguntapan",
            "Kantor Cabang Wonosari",
            "Kantor Cabang Sleman",
            "Kantor Cabang Bantul",
            "Kantor Pusat Non Operasional"
        });
        pnlContent.Controls.Add(_cmbOffice);
        curY += 44;

        // 5. Autostart checkbox
        _chkAutoStart = new CheckBox
        {
            Text = "Jalankan otomatis saat komputer dinyalakan (Autostart)",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(30, 41, 59),
            Checked = true,
            AutoSize = true,
            Location = new Point(20, curY)
        };
        pnlContent.Controls.Add(_chkAutoStart);
        curY += 34;

        // Advance Settings (Server URL & Token)
        var grpServer = new GroupBox
        {
            Text = "Konfigurasi Server & Token",
            Location = new Point(20, curY),
            Size = new Size(fieldWidth, 90),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
            ForeColor = Color.FromArgb(100, 116, 139)
        };

        var lblServer = new Label { Text = "Server:", Location = new Point(12, 24), AutoSize = true };
        _txtServerUrl = new TextBox { Text = _config.ServerUrl, Location = new Point(65, 20), Width = 180, Font = new Font("Segoe UI", 8.5f) };

        var lblToken = new Label { Text = "Token:", Location = new Point(255, 24), AutoSize = true };
        _txtOrgToken = new TextBox { Text = _config.OrgToken, Location = new Point(300, 20), Width = 120, Font = new Font("Segoe UI", 8.5f) };

        var lblHostNote = new Label
        {
            Text = $"PC Hostname: {Environment.MachineName} (Otomatis tercatat)",
            Location = new Point(12, 56),
            AutoSize = true,
            ForeColor = Color.FromArgb(148, 163, 184),
            Font = new Font("Segoe UI", 8f)
        };

        grpServer.Controls.Add(lblServer);
        grpServer.Controls.Add(_txtServerUrl);
        grpServer.Controls.Add(lblToken);
        grpServer.Controls.Add(_txtOrgToken);
        grpServer.Controls.Add(lblHostNote);
        pnlContent.Controls.Add(grpServer);
        curY += 100;

        // Progress & Status label
        _progressBar = new ProgressBar
        {
            Location = new Point(20, curY),
            Size = new Size(fieldWidth, 8),
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 30,
            Visible = false
        };
        pnlContent.Controls.Add(_progressBar);
        curY += 12;

        _lblStatus = new Label
        {
            Text = "",
            Location = new Point(20, curY),
            Size = new Size(fieldWidth, 20),
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.FromArgb(71, 85, 105),
            TextAlign = ContentAlignment.MiddleCenter
        };
        pnlContent.Controls.Add(_lblStatus);
        curY += 24;

        // Submit Button
        _btnSubmit = new Button
        {
            Text = "Kirim & Daftarkan Spek PC",
            Location = new Point(20, curY),
            Size = new Size(fieldWidth, 42),
            BackColor = Color.FromArgb(37, 99, 235),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        _btnSubmit.FlatAppearance.BorderSize = 0;
        _btnSubmit.Click += async (s, e) => await OnSubmitAsync();
        pnlContent.Controls.Add(_btnSubmit);
    }

    private Label CreateFieldLabel(string text, int x, int y)
    {
        return new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(51, 65, 85)
        };
    }

    private TextBox CreateTextBox(int x, int y, int width)
    {
        return new TextBox
        {
            Location = new Point(x, y),
            Size = new Size(width, 28),
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 10f)
        };
    }

    private ComboBox CreateComboBox(int x, int y, int width)
    {
        return new ComboBox
        {
            Location = new Point(x, y),
            Size = new Size(width, 28),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 10f)
        };
    }

    private void LoadExistingData()
    {
        _txtUserName.Text = _config.UserName;
        _txtPosition.Text = _config.Position;
        
        int divIdx = _cmbDivision.FindStringExact(_config.Division);
        if (divIdx >= 0) _cmbDivision.SelectedIndex = divIdx;
        else if (_cmbDivision.Items.Count > 0) _cmbDivision.SelectedIndex = 0;

        string savedOffice = _config.Office?.Trim() ?? "";
        if (savedOffice.Equals("Kalibawang", StringComparison.OrdinalIgnoreCase)) savedOffice = "Kantor Cabang Kalibawang";
        if (savedOffice.Equals("Sentolo", StringComparison.OrdinalIgnoreCase)) savedOffice = "Kantor Cabang Sentolo";
        if (savedOffice.Equals("Temon", StringComparison.OrdinalIgnoreCase)) savedOffice = "Kantor Cabang Temon";
        if (savedOffice.Equals("Wates", StringComparison.OrdinalIgnoreCase)) savedOffice = "Kantor Cabang Wates";
        if (savedOffice.Equals("Galur", StringComparison.OrdinalIgnoreCase)) savedOffice = "Kantor Cabang Galur";

        int offIdx = _cmbOffice.FindStringExact(savedOffice);
        if (offIdx >= 0) _cmbOffice.SelectedIndex = offIdx;
        else if (_cmbOffice.Items.Count > 0) _cmbOffice.SelectedIndex = 0;

        _chkAutoStart.Checked = _config.AutoStartEnabled;
    }

    private async Task LoadMetadataFromServerAsync()
    {
        try
        {
            var apiClient = new ApiClient(_config);
            var meta = await apiClient.GetMetaAsync();
            if (meta?.Success == true)
            {
                if (meta.Offices != null && meta.Offices.Count > 0)
                {
                    Invoke(() =>
                    {
                        string currentSelected = _cmbOffice.SelectedItem?.ToString() ?? _cmbOffice.Text;
                        _cmbOffice.Items.Clear();
                        foreach (var office in meta.Offices)
                        {
                            _cmbOffice.Items.Add(office.Name);
                        }
                        int matchIdx = _cmbOffice.FindStringExact(currentSelected);
                        if (matchIdx >= 0) _cmbOffice.SelectedIndex = matchIdx;
                        else if (_cmbOffice.Items.Count > 0) _cmbOffice.SelectedIndex = 0;
                    });
                }
            }
        }
        catch
        {
            // Ignore offline fallback
        }
    }

    private async Task OnSubmitAsync()
    {
        string name = _txtUserName.Text.Trim();
        string position = _txtPosition.Text.Trim();
        string division = _cmbDivision.SelectedItem?.ToString() ?? _cmbDivision.Text.Trim();
        string office = _cmbOffice.SelectedItem?.ToString() ?? _cmbOffice.Text.Trim();
        string serverUrl = _txtServerUrl.Text.Trim();
        string token = _txtOrgToken.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Mohon masukkan Nama Pengguna komputer.", "Validasi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtUserName.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(position))
        {
            MessageBox.Show("Mohon masukkan Jabatan pengguna.", "Validasi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtPosition.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(division))
        {
            MessageBox.Show("Mohon pilih atau masukkan Divisi / Bagian.", "Validasi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _cmbDivision.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(office))
        {
            MessageBox.Show("Mohon pilih atau masukkan Kantor / Cabang.", "Validasi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _cmbOffice.Focus();
            return;
        }

        // Disable UI and show loading
        _btnSubmit.Enabled = false;
        _progressBar.Visible = true;
        _lblStatus.Text = "⏳ Mendeteksi spesifikasi PC & software terinstall...";

        try
        {
            _config.UserName = name;
            _config.Position = position;
            _config.Division = division;
            _config.Office = office;
            _config.ServerUrl = serverUrl;
            _config.OrgToken = token;
            _config.AutoStartEnabled = _chkAutoStart.Checked;

            // Collect hardware & software specs in background task
            var payload = await Task.Run(() => HardwareCollector.CollectAll(_config));

            _lblStatus.Text = "🌐 Mengirim data spesifikasi ke server...";

            var apiClient = new ApiClient(_config);
            var (success, message) = await apiClient.RegisterAsync(payload);

            if (success)
            {
                _config.LastRegisteredAt = DateTime.UtcNow;
                _config.LastHeartbeatAt = DateTime.UtcNow;
                ConfigService.Save(_config);

                // Configure Windows autostart
                AutoStartService.SetAutoStart(_config.AutoStartEnabled);

                MessageBox.Show(
                    "Data spesifikasi PC berhasil didaftarkan ke server!\n\n" +
                    "Aplikasi akan tetap berjalan di System Tray (sudut kanan bawah) dan mengirim heartbeat online/offline secara otomatis.",
                    "Registrasi Berhasil",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );

                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                MessageBox.Show(
                    $"Gagal mengirim data ke server:\n{message}\n\nPastikan komputer terhubung ke internet dan URL server dapat dijangkau.",
                    "Gagal Terhubung",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Terjadi kesalahan: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnSubmit.Enabled = true;
            _progressBar.Visible = false;
            _lblStatus.Text = "";
        }
    }
}

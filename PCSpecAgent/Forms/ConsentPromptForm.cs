using System.Drawing.Drawing2D;
using System.Media;

namespace PCSpecAgent.Forms;

public class ConsentPromptForm : Form
{
    public bool IsApproved { get; private set; } = false;
    public string PinCode { get; }

    private int _remainingSeconds = 45;
    private readonly System.Windows.Forms.Timer _countdownTimer;
    private readonly Label _lblTimer;

    public ConsentPromptForm(string adminName, string pinCode)
    {
        PinCode = pinCode;

        Text = "Nusamba NodeCore - Permintaan Remote Desktop";
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        Size = new Size(500, 480);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        TopMost = true;
        BackColor = Color.FromArgb(248, 250, 252);
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

        // Header
        var pnlHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 80,
            BackColor = Color.FromArgb(15, 23, 42), // Sleek Dark Slate
            Padding = new Padding(20, 14, 20, 14)
        };

        var lblHeaderTitle = new Label
        {
            Text = "🛡️ Nusamba NodeCore Remote Assistance",
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(18, 14)
        };

        var lblHeaderSubtitle = new Label
        {
            Text = "Protokol Keamanan Double-Lock: Persetujuan Pengguna & PIN",
            Font = new Font("Segoe UI", 9f, FontStyle.Regular),
            ForeColor = Color.FromArgb(56, 189, 248),
            AutoSize = true,
            Location = new Point(20, 44)
        };

        pnlHeader.Controls.Add(lblHeaderTitle);
        pnlHeader.Controls.Add(lblHeaderSubtitle);
        Controls.Add(pnlHeader);

        // Content Area
        var pnlContent = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 20, 24, 20)
        };

        var lblMessage = new Label
        {
            Text = $"Administrator IT ({adminName}) meminta izin untuk mengakses dan melihat layar komputer Anda.",
            Font = new Font("Segoe UI", 10.5f, FontStyle.Regular),
            ForeColor = Color.FromArgb(15, 23, 42),
            Location = new Point(24, 95),
            Size = new Size(440, 45)
        };
        Controls.Add(lblMessage);

        // PIN Card
        var pnlPin = new Panel
        {
            Location = new Point(24, 150),
            Size = new Size(436, 120),
            BackColor = Color.FromArgb(238, 242, 255), // Indigo light
            BorderStyle = BorderStyle.FixedSingle
        };

        var lblPinNotice = new Label
        {
            Text = "KODE PIN KEAMANAN (Berikan ke Administrator):",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(79, 70, 229),
            AutoSize = true,
            Location = new Point(20, 12)
        };

        string formattedPin = pinCode.Length == 6 ? $"{pinCode.Substring(0, 3)}  {pinCode.Substring(3, 3)}" : pinCode;
        var lblPinValue = new Label
        {
            Text = formattedPin,
            Font = new Font("Consolas", 32f, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 27, 75),
            AutoSize = true,
            Location = new Point(90, 40)
        };

        pnlPin.Controls.Add(lblPinNotice);
        pnlPin.Controls.Add(lblPinValue);
        Controls.Add(pnlPin);

        // Security Info
        var lblInfo = new Label
        {
            Text = "🔒 Layar hanya dapat diakses jika Anda mengizinkan DAN Admin memasukkan PIN di atas. Anda dapat memutuskan sesi kapan saja.",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Italic),
            ForeColor = Color.FromArgb(100, 116, 139),
            Location = new Point(24, 280),
            Size = new Size(436, 40)
        };
        Controls.Add(lblInfo);

        // Countdown Timer Label
        _lblTimer = new Label
        {
            Text = $"Permintaan otomatis dibatalkan dalam {_remainingSeconds} detik...",
            Font = new Font("Segoe UI", 9f, FontStyle.Regular),
            ForeColor = Color.FromArgb(148, 163, 184),
            AutoSize = true,
            Location = new Point(24, 335)
        };
        Controls.Add(_lblTimer);

        // Action Buttons
        var btnReject = new Button
        {
            Text = "❌ TOLAK",
            Size = new Size(130, 42),
            Location = new Point(180, 375),
            BackColor = Color.FromArgb(239, 68, 68),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        btnReject.FlatAppearance.BorderSize = 0;
        btnReject.Click += (s, e) =>
        {
            IsApproved = false;
            Close();
        };

        var btnApprove = new Button
        {
            Text = "✅ IZINKAN KONEKSI",
            Size = new Size(170, 42),
            Location = new Point(320, 375),
            BackColor = Color.FromArgb(16, 185, 129),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        btnApprove.FlatAppearance.BorderSize = 0;
        btnApprove.Click += (s, e) =>
        {
            IsApproved = true;
            Close();
        };

        Controls.Add(btnReject);
        Controls.Add(btnApprove);

        // Timer
        _countdownTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _countdownTimer.Tick += (s, e) =>
        {
            _remainingSeconds--;
            _lblTimer.Text = $"Permintaan otomatis dibatalkan dalam {_remainingSeconds} detik...";
            if (_remainingSeconds <= 0)
            {
                _countdownTimer.Stop();
                IsApproved = false;
                Close();
            }
        };

        Shown += (s, e) =>
        {
            try { SystemSounds.Asterisk.Play(); } catch { }
            _countdownTimer.Start();
        };

        FormClosing += (s, e) =>
        {
            _countdownTimer.Stop();
        };
    }
}

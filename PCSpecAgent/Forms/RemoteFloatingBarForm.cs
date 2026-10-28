namespace PCSpecAgent.Forms;

public class RemoteFloatingBarForm : Form
{
    public event Action? OnDisconnectRequested;

    private Point _dragStart;

    public RemoteFloatingBarForm(string adminName)
    {
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(380, 48);
        BackColor = Color.FromArgb(15, 23, 42); // Dark slate

        // Position at top center of primary screen
        var screen = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point((screen.Width - Width) / 2, 20);

        // Border Panel
        var pnlMain = new Panel
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(12, 0, 12, 0)
        };

        var lblStatus = new Label
        {
            Text = $"🛡️ Nusamba NodeCore Remote Aktif ({adminName})",
            ForeColor = Color.FromArgb(52, 211, 153), // Emerald green
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(14, 13)
        };

        var btnDisconnect = new Button
        {
            Text = "⏹️ Putuskan",
            Size = new Size(95, 30),
            Location = new Point(265, 8),
            BackColor = Color.FromArgb(239, 68, 68),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        btnDisconnect.FlatAppearance.BorderSize = 0;
        btnDisconnect.Click += (s, e) =>
        {
            OnDisconnectRequested?.Invoke();
            Close();
        };

        pnlMain.Controls.Add(lblStatus);
        pnlMain.Controls.Add(btnDisconnect);
        Controls.Add(pnlMain);

        // Make draggable
        pnlMain.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) _dragStart = e.Location; };
        pnlMain.MouseMove += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                Location = new Point(Location.X + (e.X - _dragStart.X), Location.Y + (e.Y - _dragStart.Y));
            }
        };

        lblStatus.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) _dragStart = e.Location; };
        lblStatus.MouseMove += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                Location = new Point(Location.X + (e.X - _dragStart.X), Location.Y + (e.Y - _dragStart.Y));
            }
        };
    }
}

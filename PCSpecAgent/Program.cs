using PCSpecAgent.Forms;
using PCSpecAgent.Models;
using PCSpecAgent.Services;

namespace PCSpecAgent;

static class Program
{
    private static Mutex? _mutex;

    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // Enforce Single Instance
        const string mutexName = "Local\\NusambaNodeCore_SingleInstance";
        _mutex = new Mutex(true, mutexName, out bool isNewInstance);

        if (!isNewInstance)
        {
            MessageBox.Show(
                "Nusamba NodeCore Agent sudah berjalan di System Tray (sudut kanan bawah layar).",
                "Nusamba NodeCore Aktif",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
            return;
        }

        try
        {
            var config = ConfigService.Load();

            bool forceSetup = args.Contains("--setup");
            bool isMinimized = args.Contains("--minimized");

            if (!config.IsConfigured || forceSetup)
            {
                using var setupForm = new SetupForm(config);
                var result = setupForm.ShowDialog();
                if (result != DialogResult.OK && !config.IsConfigured)
                {
                    // User aborted setup on first run
                    return;
                }
            }

            // Run in System Tray
            Application.Run(new TrayAppContext(config));
        }
        finally
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
    }
}
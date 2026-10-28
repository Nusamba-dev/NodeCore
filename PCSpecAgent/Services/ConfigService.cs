using System.Text.Json;
using PCSpecAgent.Models;

namespace PCSpecAgent.Services;

public class ConfigService
{
    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCSpecAgent"
    );

    private static readonly string ConfigPath = Path.Combine(AppDataDir, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json);
                if (config != null)
                {
                    if (string.IsNullOrWhiteSpace(config.MachineId))
                    {
                        config.MachineId = Guid.NewGuid().ToString("N");
                    }
                    return config;
                }
            }
        }
        catch
        {
            // Fallback to fresh config if read/parse fails
        }

        return new AppConfig();
    }

    public static void Save(AppConfig config)
    {
        try
        {
            if (!Directory.Exists(AppDataDir))
            {
                Directory.CreateDirectory(AppDataDir);
            }

            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving config: {ex.Message}");
        }
    }
}

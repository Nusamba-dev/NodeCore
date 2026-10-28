namespace PCSpecAgent.Models;

public class AppConfig
{
    public string MachineId { get; set; } = Guid.NewGuid().ToString("N");
    public string UserName { get; set; } = "";
    public string Position { get; set; } = "";
    public string Division { get; set; } = "";
    public string Office { get; set; } = "";
    public string ServerUrl { get; set; } = "https://devcollect.nusamba.com";
    public string OrgToken { get; set; } = "nusamba-pc-spec-2026";
    public bool AutoStartEnabled { get; set; } = true;
    public string? AdminUser { get; set; }
    public string? AdminPass { get; set; }
    public DateTime? LastRegisteredAt { get; set; }
    public DateTime? LastHeartbeatAt { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(UserName) && !string.IsNullOrWhiteSpace(Office);
}

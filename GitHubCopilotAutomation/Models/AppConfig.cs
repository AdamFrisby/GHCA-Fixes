namespace GitHubCopilotAutomation.Models;

public class AppConfig
{
    public string GitHubToken { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string Repository { get; set; } = string.Empty;
    public int ScanIntervalMinutes { get; set; } = 60;
    public int MaxRetries { get; set; } = 3;
    public int BaseDelaySeconds { get; set; } = 60;
    public double BackoffMultiplier { get; set; } = 2.0;
    public string[] CopilotUsernames { get; set; } = { "@copilot", "@apps/copilot-pull-request-reviewer" };
    public long CopilotUserId { get; set; } = 198982749;
}
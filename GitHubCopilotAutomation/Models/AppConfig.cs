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
    
    // Copilot agent concurrency and backoff settings
    public int MaxConcurrentAgents { get; set; } = 2;
    public int AgentStartValidationMinutes { get; set; } = 5;
    public int CopilotBackoffIncrementMinutes { get; set; } = 15;
    public int CopilotSuccessResetMinutes { get; set; } = 2;
}
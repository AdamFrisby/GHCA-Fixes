namespace GitHubCopilotAutomation.Models;

public enum CopilotWorkState
{
    Unknown,
    Started,
    InProgress,
    Finished,
    FinishedWithFailure
}

public class CopilotWorkStatus
{
    public CopilotWorkState State { get; set; }
    public DateTime? LastEventTime { get; set; }
    public string? LastEventSource { get; set; }
    public bool HasRecentActivity { get; set; }
}
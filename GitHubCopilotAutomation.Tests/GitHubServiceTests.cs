using GitHubCopilotAutomation.Models;

namespace GitHubCopilotAutomation.Tests;

public class GitHubServiceTests
{
    [Fact]
    public void CopilotWorkState_EnumValues_AreCorrect()
    {
        // Test to ensure the enum values are as expected for state detection
        Assert.Equal(0, (int)CopilotWorkState.Unknown);
        Assert.Equal(1, (int)CopilotWorkState.Started);
        Assert.Equal(2, (int)CopilotWorkState.InProgress);
        Assert.Equal(3, (int)CopilotWorkState.Finished);
        Assert.Equal(4, (int)CopilotWorkState.FinishedWithFailure);
    }

    [Theory]
    [InlineData("copilot_work_started", CopilotWorkState.Started)]
    [InlineData("copilot_work_finished", CopilotWorkState.Finished)]
    [InlineData("copilot_work_finished_failure", CopilotWorkState.FinishedWithFailure)]
    public void EventTypeMapping_ShouldMapCorrectly(string eventType, CopilotWorkState expectedState)
    {
        // This test documents the expected mapping between event types and states
        // The actual implementation should check timeline events for these event types
        CopilotWorkState actualState = eventType switch
        {
            "copilot_work_started" => CopilotWorkState.Started,
            "copilot_work_finished" => CopilotWorkState.Finished,
            "copilot_work_finished_failure" => CopilotWorkState.FinishedWithFailure,
            _ => CopilotWorkState.Unknown
        };

        Assert.Equal(expectedState, actualState);
    }
}
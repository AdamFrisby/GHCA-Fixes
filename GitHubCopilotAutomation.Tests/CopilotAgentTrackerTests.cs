using GitHubCopilotAutomation.Models;
using GitHubCopilotAutomation.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GitHubCopilotAutomation.Tests;

public class CopilotAgentTrackerTests
{
    private readonly AppConfig _config;
    private readonly CopilotAgentTracker _tracker;

    public CopilotAgentTrackerTests()
    {
        _config = new AppConfig
        {
            MaxConcurrentAgents = 2,
            AgentStartValidationMinutes = 5,
            CopilotBackoffIncrementMinutes = 15,
            CopilotSuccessResetMinutes = 2
        };
        var options = new OptionsWrapper<AppConfig>(_config);
        var logger = new TestLogger<CopilotAgentTracker>();
        
        _tracker = new CopilotAgentTracker(options, logger);
    }

    [Fact]
    public async Task CanStartNewAgent_WhenNoActiveAgents_ReturnsTrue()
    {
        // Act
        var result = await _tracker.CanStartNewAgentAsync();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task CanStartNewAgent_WhenMaxAgentsReached_ReturnsFalse()
    {
        // Arrange
        await _tracker.TrackAgentStartedAsync(1);
        await _tracker.TrackAgentStartedAsync(2);

        // Act
        var result = await _tracker.CanStartNewAgentAsync();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task TrackAgentStarted_IncrementsActiveCount()
    {
        // Act
        await _tracker.TrackAgentStartedAsync(1);

        // Assert
        Assert.Equal(1, _tracker.GetActiveAgentCount());
    }

    [Fact]
    public async Task TrackAgentFinished_DecrementsActiveCount()
    {
        // Arrange
        await _tracker.TrackAgentStartedAsync(1);

        // Act
        await _tracker.TrackAgentFinishedAsync(1, success: true);

        // Assert
        Assert.Equal(0, _tracker.GetActiveAgentCount());
    }

    [Fact]
    public async Task GetBackoffDelay_WithNoFailures_ReturnsZero()
    {
        // Act
        var delay = await _tracker.GetBackoffDelayAsync();

        // Assert
        Assert.Equal(TimeSpan.Zero, delay);
    }

    [Fact]
    public async Task GetBackoffDelay_WithOneFailure_Returns15Minutes()
    {
        // Arrange
        await _tracker.TrackAgentStartedAsync(1);
        await _tracker.TrackAgentFinishedAsync(1, success: false);

        // Act
        var delay = await _tracker.GetBackoffDelayAsync();

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(15), delay);
    }

    [Fact]
    public async Task GetBackoffDelay_WithTwoFailures_Returns30Minutes()
    {
        // Arrange
        await _tracker.TrackAgentStartedAsync(1);
        await _tracker.TrackAgentFinishedAsync(1, success: false);
        await _tracker.TrackAgentStartedAsync(2);
        await _tracker.TrackAgentFinishedAsync(2, success: false);

        // Act
        var delay = await _tracker.GetBackoffDelayAsync();

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(30), delay);
    }

    [Fact]
    public async Task GetBackoffDelay_WithOneSuccess_Returns2Minutes()
    {
        // Arrange
        await _tracker.TrackAgentStartedAsync(1);
        await _tracker.TrackAgentFinishedAsync(1, success: true);

        // Act
        var delay = await _tracker.GetBackoffDelayAsync();

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(2), delay);
    }

    [Fact]
    public async Task GetBackoffDelay_WithTwoSuccesses_ReturnsZero()
    {
        // Arrange
        await _tracker.TrackAgentStartedAsync(1);
        await _tracker.TrackAgentFinishedAsync(1, success: true);
        await _tracker.TrackAgentStartedAsync(2);
        await _tracker.TrackAgentFinishedAsync(2, success: true);

        // Act
        var delay = await _tracker.GetBackoffDelayAsync();

        // Assert
        Assert.Equal(TimeSpan.Zero, delay);
    }

    [Fact]
    public async Task SuccessResetsFailureCount()
    {
        // Arrange
        await _tracker.TrackAgentStartedAsync(1);
        await _tracker.TrackAgentFinishedAsync(1, success: false);
        
        // Verify failure causes backoff
        var failureDelay = await _tracker.GetBackoffDelayAsync();
        Assert.Equal(TimeSpan.FromMinutes(15), failureDelay);

        // Act - success should reset failures
        await _tracker.TrackAgentStartedAsync(2);
        await _tracker.TrackAgentFinishedAsync(2, success: true);

        // Assert
        var successDelay = await _tracker.GetBackoffDelayAsync();
        Assert.Equal(TimeSpan.FromMinutes(2), successDelay);
    }

    [Fact]
    public async Task ConcurrencyIsTrackedAcrossAllPRs()
    {
        // Arrange - Start agents for different PRs
        await _tracker.TrackAgentStartedAsync(100);  // PR #100
        await _tracker.TrackAgentStartedAsync(200);  // PR #200
        
        // Assert - Should have 2 active agents total across all PRs
        Assert.Equal(2, _tracker.GetActiveAgentCount());
        
        // Assert - Should not be able to start another agent (max is 2)
        var canStartAnother = await _tracker.CanStartNewAgentAsync();
        Assert.False(canStartAnother);
        
        // Act - Finish one agent
        await _tracker.TrackAgentFinishedAsync(100, success: true);
        
        // Assert - Should now have 1 active agent and be able to start another
        Assert.Equal(1, _tracker.GetActiveAgentCount());
        var canStartAfterFinish = await _tracker.CanStartNewAgentAsync();
        Assert.True(canStartAfterFinish);
        
        // Act - Start agent for a third PR
        await _tracker.TrackAgentStartedAsync(300);  // PR #300
        
        // Assert - Should now have 2 active agents again (PR #200 and #300)
        Assert.Equal(2, _tracker.GetActiveAgentCount());
        var canStartWhenFull = await _tracker.CanStartNewAgentAsync();
        Assert.False(canStartWhenFull);
    }

    private class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
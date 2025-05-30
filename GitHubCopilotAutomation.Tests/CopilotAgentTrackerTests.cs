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

    private class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
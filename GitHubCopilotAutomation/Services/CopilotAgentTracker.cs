using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GitHubCopilotAutomation.Models;

namespace GitHubCopilotAutomation.Services;

public interface ICopilotAgentTracker
{
    Task<bool> CanStartNewAgentAsync();
    Task TrackAgentStartedAsync(int pullRequestNumber);
    Task TrackAgentFinishedAsync(int pullRequestNumber, bool success);
    Task<TimeSpan> GetBackoffDelayAsync();
    int GetActiveAgentCount();
}

public class CopilotAgentTracker : ICopilotAgentTracker
{
    private readonly AppConfig _config;
    private readonly ILogger<CopilotAgentTracker> _logger;
    private readonly Dictionary<int, AgentInfo> _activeAgents = new();
    private readonly object _lock = new();
    
    private int _consecutiveFailures = 0;
    private int _consecutiveSuccesses = 0;
    private DateTime? _lastFailureTime;

    public CopilotAgentTracker(IOptions<AppConfig> config, ILogger<CopilotAgentTracker> logger)
    {
        _config = config.Value;
        _logger = logger;
    }

    public Task<bool> CanStartNewAgentAsync()
    {
        lock (_lock)
        {
            var activeCount = _activeAgents.Count;
            var canStart = activeCount < _config.MaxConcurrentAgents;
            
            _logger.LogDebug("Checking if new agent can start: {ActiveCount}/{MaxCount}, CanStart: {CanStart}", 
                activeCount, _config.MaxConcurrentAgents, canStart);
                
            return Task.FromResult(canStart);
        }
    }

    public Task TrackAgentStartedAsync(int pullRequestNumber)
    {
        lock (_lock)
        {
            _activeAgents[pullRequestNumber] = new AgentInfo
            {
                PullRequestNumber = pullRequestNumber,
                StartTime = DateTime.UtcNow,
                IsValidated = false
            };
            
            _logger.LogInformation("Started tracking agent for PR #{PullRequestNumber}. Active agents: {Count}", 
                pullRequestNumber, _activeAgents.Count);
        }
        
        // Start validation timer
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMinutes(_config.AgentStartValidationMinutes));
            ValidateAgentStartup(pullRequestNumber);
        });
        
        return Task.CompletedTask;
    }

    public Task TrackAgentFinishedAsync(int pullRequestNumber, bool success)
    {
        lock (_lock)
        {
            if (_activeAgents.Remove(pullRequestNumber))
            {
                _logger.LogInformation("Agent finished for PR #{PullRequestNumber}. Success: {Success}. Active agents: {Count}", 
                    pullRequestNumber, success, _activeAgents.Count);
                
                if (success)
                {
                    _consecutiveSuccesses++;
                    _consecutiveFailures = 0;
                    _logger.LogDebug("Consecutive successes: {Count}", _consecutiveSuccesses);
                }
                else
                {
                    _consecutiveFailures++;
                    _consecutiveSuccesses = 0;
                    _lastFailureTime = DateTime.UtcNow;
                    _logger.LogWarning("Agent failed for PR #{PullRequestNumber}. Consecutive failures: {Count}", 
                        pullRequestNumber, _consecutiveFailures);
                }
            }
            else
            {
                _logger.LogWarning("Attempted to finish tracking for PR #{PullRequestNumber} but it was not being tracked", 
                    pullRequestNumber);
            }
        }
        
        return Task.CompletedTask;
    }

    public Task<TimeSpan> GetBackoffDelayAsync()
    {
        lock (_lock)
        {
            if (_consecutiveFailures == 0)
            {
                // No failures - check for success-based reset
                if (_consecutiveSuccesses >= 2)
                {
                    return Task.FromResult(TimeSpan.Zero);
                }
                else if (_consecutiveSuccesses == 1)
                {
                    return Task.FromResult(TimeSpan.FromMinutes(_config.CopilotSuccessResetMinutes));
                }
                return Task.FromResult(TimeSpan.Zero);
            }

            // Calculate exponential backoff for failures
            var backoffMinutes = _consecutiveFailures * _config.CopilotBackoffIncrementMinutes;
            var delay = TimeSpan.FromMinutes(backoffMinutes);
            
            _logger.LogInformation("Calculated backoff delay: {DelayMinutes} minutes for {FailureCount} consecutive failures", 
                delay.TotalMinutes, _consecutiveFailures);
                
            return Task.FromResult(delay);
        }
    }

    public int GetActiveAgentCount()
    {
        lock (_lock)
        {
            return _activeAgents.Count;
        }
    }

    private Task ValidateAgentStartup(int pullRequestNumber)
    {
        lock (_lock)
        {
            if (_activeAgents.TryGetValue(pullRequestNumber, out var agentInfo))
            {
                agentInfo.IsValidated = true;
                _logger.LogDebug("Agent for PR #{PullRequestNumber} validated after {Minutes} minutes", 
                    pullRequestNumber, _config.AgentStartValidationMinutes);
            }
        }
        
        return Task.CompletedTask;
    }

    private class AgentInfo
    {
        public int PullRequestNumber { get; set; }
        public DateTime StartTime { get; set; }
        public bool IsValidated { get; set; }
    }
}
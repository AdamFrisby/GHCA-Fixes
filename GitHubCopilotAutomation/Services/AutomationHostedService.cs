using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GitHubCopilotAutomation.Models;

namespace GitHubCopilotAutomation.Services;

public class AutomationHostedService : BackgroundService
{
    private readonly IAutomationService _automationService;
    private readonly ICopilotAgentTracker _agentTracker;
    private readonly AppConfig _config;
    private readonly ILogger<AutomationHostedService> _logger;
    private int _retryCount = 0;

    public AutomationHostedService(
        IAutomationService automationService,
        ICopilotAgentTracker agentTracker,
        IOptions<AppConfig> config,
        ILogger<AutomationHostedService> logger)
    {
        _automationService = automationService;
        _agentTracker = agentTracker;
        _config = config.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GitHub Copilot Automation Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Check Copilot agent backoff before proceeding
                var copilotBackoffDelay = await _agentTracker.GetBackoffDelayAsync();
                if (copilotBackoffDelay > TimeSpan.Zero)
                {
                    _logger.LogInformation("Copilot agent backoff in effect. Waiting {DelayMinutes} minutes before next scan", 
                        copilotBackoffDelay.TotalMinutes);
                    await Task.Delay(copilotBackoffDelay, stoppingToken);
                    continue;
                }

                _logger.LogInformation("Starting pull request scan... Active agents: {ActiveCount}", 
                    _agentTracker.GetActiveAgentCount());
                await _automationService.ProcessPullRequestsAsync(interactive: false);
                _retryCount = 0; // Reset retry count on success
                
                _logger.LogInformation("Pull request scan completed. Next scan in {Minutes} minutes", _config.ScanIntervalMinutes);
                await Task.Delay(TimeSpan.FromMinutes(_config.ScanIntervalMinutes), stoppingToken);
            }
            catch (Octokit.RateLimitExceededException ex)
            {
                var delayMinutes = CalculateExponentialBackoff();
                _logger.LogWarning("Rate limit exceeded. Retrying in {DelayMinutes} minutes. Reset time: {ResetTime}", 
                    delayMinutes, ex.Reset.ToString("yyyy-MM-dd HH:mm:ss UTC"));
                
                await Task.Delay(TimeSpan.FromMinutes(delayMinutes), stoppingToken);
            }
            catch (Exception ex)
            {
                var delayMinutes = CalculateExponentialBackoff();
                _logger.LogError(ex, "Error during pull request scan. Retrying in {DelayMinutes} minutes", delayMinutes);
                
                await Task.Delay(TimeSpan.FromMinutes(delayMinutes), stoppingToken);
            }
        }
    }

    private double CalculateExponentialBackoff()
    {
        _retryCount++;
        var delaySeconds = _config.BaseDelaySeconds * Math.Pow(_config.BackoffMultiplier, _retryCount - 1);
        return Math.Min(delaySeconds / 60.0, 60.0); // Cap at 60 minutes
    }
}
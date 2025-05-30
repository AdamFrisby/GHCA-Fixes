using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;
using GitHubCopilotAutomation.Models;

namespace GitHubCopilotAutomation.Services;

public interface IAutomationService
{
    Task ProcessPullRequestsAsync(bool interactive = false);
}

public class AutomationService : IAutomationService
{
    private readonly IGitHubService _gitHubService;
    private readonly ICopilotAgentTracker _agentTracker;
    private readonly AppConfig _config;
    private readonly ILogger<AutomationService> _logger;

    public AutomationService(
        IGitHubService gitHubService, 
        ICopilotAgentTracker agentTracker,
        IOptions<AppConfig> config, 
        ILogger<AutomationService> logger)
    {
        _gitHubService = gitHubService;
        _agentTracker = agentTracker;
        _config = config.Value;
        _logger = logger;
    }

    public async Task ProcessPullRequestsAsync(bool interactive = false)
    {
        try
        {
            // Check if we can proceed with agent management
            var backoffDelay = await _agentTracker.GetBackoffDelayAsync();
            if (backoffDelay > TimeSpan.Zero)
            {
                _logger.LogInformation("Agent backoff in effect. Delaying for {DelayMinutes} minutes", backoffDelay.TotalMinutes);
                return;
            }

            var pullRequests = await _gitHubService.GetDraftPullRequestsAsync();
            _logger.LogInformation("Found {Count} draft pull requests assigned to Copilot. Active agents: {ActiveCount}", 
                pullRequests.Count(), _agentTracker.GetActiveAgentCount());

            // Update agent states based on current PR statuses
            await UpdateAgentStatesAsync(pullRequests);

            foreach (var pr in pullRequests)
            {
                // Check if we can start a new agent for this PR
                if (!await _agentTracker.CanStartNewAgentAsync())
                {
                    _logger.LogInformation("Maximum concurrent agents ({MaxCount}) reached across all PRs. Current active: {ActiveCount}. Skipping remaining PRs", 
                        _config.MaxConcurrentAgents, _agentTracker.GetActiveAgentCount());
                    break;
                }

                await ProcessSinglePullRequestAsync(pr, interactive);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing pull requests");
            throw;
        }
    }

    private async Task UpdateAgentStatesAsync(IEnumerable<PullRequest> pullRequests)
    {
        foreach (var pr in pullRequests)
        {
            try
            {
                var workStatus = await _gitHubService.DetectCopilotWorkStatusAsync(pr.Number);
                
                if (workStatus.State == CopilotWorkState.Finished)
                {
                    await _agentTracker.TrackAgentFinishedAsync(pr.Number, success: true);
                }
                else if (workStatus.State == CopilotWorkState.FinishedWithFailure)
                {
                    await _agentTracker.TrackAgentFinishedAsync(pr.Number, success: false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update agent state for PR #{Number}", pr.Number);
            }
        }
    }

    private async Task ProcessSinglePullRequestAsync(PullRequest pr, bool interactive)
    {
        try
        {
            _logger.LogInformation("Processing PR #{Number}: {Title}", pr.Number, pr.Title);

            // Get the linked issue
            var linkedIssue = await _gitHubService.GetLinkedIssueAsync(pr);
            if (linkedIssue == null)
            {
                _logger.LogWarning("No linked issue found for PR #{Number}", pr.Number);
                return;
            }

            // Check current Copilot work status
            var workStatus = await _gitHubService.DetectCopilotWorkStatusAsync(pr.Number);
            var comments = await _gitHubService.GetPullRequestCommentsAsync(pr.Number);
            
            var lastCopilotComment = comments
                .Where(c => c.User != null && 
                           (_config.CopilotUsernames.Contains($"@{c.User.Login}") || 
                            c.User.Id == _config.CopilotUserId))
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefault();

            // Determine action based on Copilot work status
            string? commentToPost = null;
            string action = "";
            bool willStartNewAgent = false;

            if (workStatus.State == CopilotWorkState.FinishedWithFailure)
            {
                commentToPost = "@copilot please continue";
                action = "Request Copilot to continue after failure";
                willStartNewAgent = true;
            }
            else if (workStatus.State == CopilotWorkState.Finished)
            {
                // Check if we've already requested review too many times
                var reviewRequestCount = comments.Count(c => 
                    c.Body.Contains("Please review the code in this PR against the original specification"));

                if (reviewRequestCount < _config.MaxRetries)
                {
                    commentToPost = $"@copilot Please review the code in this PR against the original specification in #{linkedIssue.Number}, and verify your fix completely satisfies this issue.";
                    action = $"Request Copilot to review against issue #{linkedIssue.Number} (attempt {reviewRequestCount + 1}/{_config.MaxRetries})";
                    willStartNewAgent = true;
                }
                else
                {
                    _logger.LogInformation("Already requested review {MaxRetries} times for PR #{Number}", _config.MaxRetries, pr.Number);
                    return;
                }
            }
            else if (workStatus.State == CopilotWorkState.Unknown || workStatus.State == CopilotWorkState.Started)
            {
                _logger.LogInformation("No actionable copilot status found for PR #{Number} (State: {State})", pr.Number, workStatus.State);
                return;
            }

            if (!string.IsNullOrEmpty(commentToPost))
            {
                if (interactive)
                {
                    if (GetUserConsentAsync(pr, linkedIssue, action, commentToPost))
                    {
                        await _gitHubService.PostCommentAsync(pr.Number, commentToPost);
                        _logger.LogInformation("Posted comment to PR #{Number}", pr.Number);
                        
                        if (willStartNewAgent)
                        {
                            await _agentTracker.TrackAgentStartedAsync(pr.Number);
                        }
                    }
                    else
                    {
                        _logger.LogInformation("User declined to post comment to PR #{Number}", pr.Number);
                    }
                }
                else
                {
                    await _gitHubService.PostCommentAsync(pr.Number, commentToPost);
                    _logger.LogInformation("Posted comment to PR #{Number}", pr.Number);
                    
                    if (willStartNewAgent)
                    {
                        await _agentTracker.TrackAgentStartedAsync(pr.Number);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing PR #{Number}", pr.Number);
        }
    }

    private bool GetUserConsentAsync(PullRequest pr, Issue linkedIssue, string action, string comment)
    {
        Console.WriteLine();
        Console.WriteLine("=== Pull Request Review ===");
        Console.WriteLine($"PR #{pr.Number}: {pr.Title}");
        Console.WriteLine($"URL: {pr.HtmlUrl}");
        Console.WriteLine($"Linked Issue #{linkedIssue.Number}: {linkedIssue.Title}");
        Console.WriteLine($"Action: {action}");
        Console.WriteLine($"Comment to post: {comment}");
        Console.WriteLine();
        
        while (true)
        {
            Console.Write("Do you want to post this comment? (y/n): ");
            var response = Console.ReadLine()?.Trim().ToLowerInvariant();
            
            if (response == "y" || response == "yes")
                return true;
            if (response == "n" || response == "no")
                return false;
                
            Console.WriteLine("Please enter 'y' for yes or 'n' for no.");
        }
    }
}
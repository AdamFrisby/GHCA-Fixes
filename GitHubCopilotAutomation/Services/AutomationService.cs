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
    private readonly AppConfig _config;
    private readonly ILogger<AutomationService> _logger;

    public AutomationService(IGitHubService gitHubService, IOptions<AppConfig> config, ILogger<AutomationService> logger)
    {
        _gitHubService = gitHubService;
        _config = config.Value;
        _logger = logger;
    }

    public async Task ProcessPullRequestsAsync(bool interactive = false)
    {
        try
        {
            var pullRequests = await _gitHubService.GetDraftPullRequestsAsync();
            _logger.LogInformation("Found {Count} draft pull requests assigned to Copilot", pullRequests.Count());

            foreach (var pr in pullRequests)
            {
                await ProcessSinglePullRequestAsync(pr, interactive);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing pull requests");
            throw;
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

            // Get timeline events
            var events = await _gitHubService.GetPullRequestTimelineAsync(pr.Number);
            
            // For now, we'll work with generic events and look for specific patterns
            // Note: The exact "copilot_work_finished_failure" and "copilot_work_finished" events
            // may not be standard GitHub events. We'll check for comments from copilot instead
            var comments = await _gitHubService.GetPullRequestCommentsAsync(pr.Number);
            
            var lastCopilotComment = comments
                .Where(c => c.User != null && 
                           (_config.CopilotUsernames.Contains($"@{c.User.Login}") || 
                            c.User.Id == _config.CopilotUserId))
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefault();

            // Check for failure indicators in the last copilot comment or PR state
            bool indicatesFailure = lastCopilotComment?.Body?.Contains("failed") == true ||
                                   lastCopilotComment?.Body?.Contains("error") == true ||
                                   lastCopilotComment?.Body?.Contains("unable") == true;

            bool indicatesCompletion = lastCopilotComment?.Body?.Contains("completed") == true ||
                                      lastCopilotComment?.Body?.Contains("finished") == true ||
                                      lastCopilotComment?.Body?.Contains("done") == true;

            string? commentToPost = null;
            string action = "";

            if (indicatesFailure)
            {
                commentToPost = "@copilot please continue";
                action = "Request Copilot to continue after failure";
            }
            else if (indicatesCompletion)
            {
                // Check if we've already requested review too many times
                var reviewRequestCount = comments.Count(c => 
                    c.Body.Contains("Please review the code in this PR against the original specification"));

                if (reviewRequestCount < _config.MaxRetries)
                {
                    commentToPost = $"@copilot Please review the code in this PR against the original specification in #{linkedIssue.Number}, and verify your fix completely satisfies this issue.";
                    action = $"Request Copilot to review against issue #{linkedIssue.Number} (attempt {reviewRequestCount + 1}/{_config.MaxRetries})";
                }
                else
                {
                    _logger.LogInformation("Already requested review {MaxRetries} times for PR #{Number}", _config.MaxRetries, pr.Number);
                    return;
                }
            }
            else if (lastCopilotComment == null)
            {
                _logger.LogInformation("No copilot activity found for PR #{Number}", pr.Number);
                return;
            }
            else
            {
                _logger.LogInformation("No actionable copilot status found for PR #{Number}", pr.Number);
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
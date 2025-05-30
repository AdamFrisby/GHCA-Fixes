using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;
using GitHubCopilotAutomation.Models;
using GitHubCopilotAutomation.Utils;

namespace GitHubCopilotAutomation.Services;

public interface IGitHubService
{
    Task<IEnumerable<PullRequest>> GetDraftPullRequestsAsync();
    Task<Issue?> GetLinkedIssueAsync(PullRequest pullRequest);
    Task<IEnumerable<IssueEvent>> GetPullRequestTimelineAsync(int pullRequestNumber);
    Task PostCommentAsync(int pullRequestNumber, string comment);
    Task<IEnumerable<IssueComment>> GetPullRequestCommentsAsync(int pullRequestNumber);
    Task<CopilotWorkStatus> DetectCopilotWorkStatusAsync(int pullRequestNumber);
}

public class GitHubService : IGitHubService
{
    private readonly GitHubClient _client;
    private readonly AppConfig _config;
    private readonly ILogger<GitHubService> _logger;

    public GitHubService(IOptions<AppConfig> config, ILogger<GitHubService> logger)
    {
        _config = config.Value;
        _logger = logger;
        
        _client = new GitHubClient(new ProductHeaderValue("GitHubCopilotAutomation"));
        if (!string.IsNullOrEmpty(_config.GitHubToken))
        {
            _client.Credentials = new Credentials(_config.GitHubToken);
        }
    }

    public async Task<IEnumerable<PullRequest>> GetDraftPullRequestsAsync()
    {
        try
        {
            var pullRequests = await _client.PullRequest.GetAllForRepository(_config.Owner, _config.Repository, 
                new PullRequestRequest { State = ItemStateFilter.Open });

            return pullRequests.Where(pr => pr.Draft && IsCopilotAssigned(pr));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get draft pull requests");
            throw;
        }
    }

    public async Task<Issue?> GetLinkedIssueAsync(PullRequest pullRequest)
    {
        try
        {
            // Look for "Fixes #123" pattern in PR body
            var issueNumber = GitHubUtils.ExtractIssueNumber(pullRequest.Body);
            if (issueNumber.HasValue)
            {
                return await _client.Issue.Get(_config.Owner, _config.Repository, issueNumber.Value);
            }

            // Also check linked issues via GitHub's linking API
            // Note: This might require additional API calls or different endpoints
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get linked issue for PR {PullRequestNumber}", pullRequest.Number);
            return null;
        }
    }

    public async Task<IEnumerable<IssueEvent>> GetPullRequestTimelineAsync(int pullRequestNumber)
    {
        try
        {
            return await _client.Issue.Events.GetAllForIssue(_config.Owner, _config.Repository, pullRequestNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get timeline for PR {PullRequestNumber}", pullRequestNumber);
            throw;
        }
    }

    public async Task PostCommentAsync(int pullRequestNumber, string comment)
    {
        try
        {
            await _client.Issue.Comment.Create(_config.Owner, _config.Repository, pullRequestNumber, comment);
            _logger.LogInformation("Posted comment to PR {PullRequestNumber}", pullRequestNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post comment to PR {PullRequestNumber}", pullRequestNumber);
            throw;
        }
    }

    public async Task<IEnumerable<IssueComment>> GetPullRequestCommentsAsync(int pullRequestNumber)
    {
        try
        {
            return await _client.Issue.Comment.GetAllForIssue(_config.Owner, _config.Repository, pullRequestNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get comments for PR {PullRequestNumber}", pullRequestNumber);
            throw;
        }
    }

    private bool IsCopilotAssigned(PullRequest pullRequest)
    {
        // Check if any assignee matches copilot criteria
        if (pullRequest.Assignees?.Any(assignee => 
            GitHubUtils.IsCopilotUser(assignee.Login, assignee.Id, _config.CopilotUsernames, _config.CopilotUserId)) == true)
        {
            return true;
        }

        // Check if author is copilot
        if (pullRequest.User != null && 
            GitHubUtils.IsCopilotUser(pullRequest.User.Login, pullRequest.User.Id, _config.CopilotUsernames, _config.CopilotUserId))
        {
            return true;
        }

        return false;
    }

    public async Task<CopilotWorkStatus> DetectCopilotWorkStatusAsync(int pullRequestNumber)
    {
        try
        {
            var comments = await GetPullRequestCommentsAsync(pullRequestNumber);
            var events = await GetPullRequestTimelineAsync(pullRequestNumber);

            var status = new CopilotWorkStatus
            {
                State = CopilotWorkState.Unknown,
                HasRecentActivity = false
            };

            // Check comments for Copilot work indicators
            var copilotComments = comments
                .Where(c => c.User != null && 
                           (_config.CopilotUsernames.Contains($"@{c.User.Login}") || 
                            c.User.Id == _config.CopilotUserId))
                .OrderByDescending(c => c.CreatedAt)
                .ToList();

            var lastCopilotComment = copilotComments.FirstOrDefault();
            
            if (lastCopilotComment != null)
            {
                status.LastEventTime = lastCopilotComment.CreatedAt.DateTime;
                status.LastEventSource = $"Comment by {lastCopilotComment.User?.Login}";
                status.HasRecentActivity = (DateTime.UtcNow - lastCopilotComment.CreatedAt.DateTime).TotalMinutes < 30;

                // Detect work state from comment content
                var body = lastCopilotComment.Body?.ToLowerInvariant() ?? "";
                
                if (body.Contains("copilot_work_started") || 
                    body.Contains("starting work") || 
                    body.Contains("beginning to work"))
                {
                    status.State = CopilotWorkState.Started;
                }
                else if (body.Contains("copilot_work_finished_failure") || 
                         body.Contains("copilot_work_finished_error") ||
                         body.Contains("failed") || 
                         body.Contains("error") || 
                         body.Contains("unable"))
                {
                    status.State = CopilotWorkState.FinishedWithFailure;
                }
                else if (body.Contains("copilot_work_finished") || 
                         body.Contains("completed") || 
                         body.Contains("finished") || 
                         body.Contains("done"))
                {
                    status.State = CopilotWorkState.Finished;
                }
                else
                {
                    status.State = CopilotWorkState.InProgress;
                }
            }

            // Also check timeline events for Copilot-related activities
            var recentEvents = events
                .Where(e => e.CreatedAt > DateTime.UtcNow.AddHours(-24))
                .OrderByDescending(e => e.CreatedAt)
                .ToList();

            foreach (var eventItem in recentEvents)
            {
                // Check if the event is from Copilot
                if (eventItem.Actor != null && 
                    GitHubUtils.IsCopilotUser(eventItem.Actor.Login, eventItem.Actor.Id, _config.CopilotUsernames, _config.CopilotUserId))
                {
                    if (eventItem.CreatedAt.DateTime > (status.LastEventTime ?? DateTime.MinValue))
                    {
                        status.LastEventTime = eventItem.CreatedAt.DateTime;
                        status.LastEventSource = $"Event: {eventItem.Event} by {eventItem.Actor.Login}";
                        status.HasRecentActivity = (DateTime.UtcNow - eventItem.CreatedAt.DateTime).TotalMinutes < 30;
                    }
                }
            }

            _logger.LogDebug("Detected Copilot work status for PR #{PullRequestNumber}: {State}, Last event: {LastEvent}", 
                pullRequestNumber, status.State, status.LastEventSource);

            return status;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to detect Copilot work status for PR {PullRequestNumber}", pullRequestNumber);
            return new CopilotWorkStatus { State = CopilotWorkState.Unknown };
        }
    }
}
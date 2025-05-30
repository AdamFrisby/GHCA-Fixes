using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;
using GitHubCopilotAutomation.Models;
using System.Text.RegularExpressions;

namespace GitHubCopilotAutomation.Services;

public interface IGitHubService
{
    Task<IEnumerable<PullRequest>> GetDraftPullRequestsAsync();
    Task<Issue?> GetLinkedIssueAsync(PullRequest pullRequest);
    Task<IEnumerable<IssueEvent>> GetPullRequestTimelineAsync(int pullRequestNumber);
    Task PostCommentAsync(int pullRequestNumber, string comment);
    Task<IEnumerable<IssueComment>> GetPullRequestCommentsAsync(int pullRequestNumber);
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
            var issueNumber = ExtractIssueNumber(pullRequest.Body);
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
            _config.CopilotUsernames.Contains($"@{assignee.Login}") || 
            assignee.Id == _config.CopilotUserId) == true)
        {
            return true;
        }

        // Check if author is copilot
        if (pullRequest.User != null && 
            (_config.CopilotUsernames.Contains($"@{pullRequest.User.Login}") || 
             pullRequest.User.Id == _config.CopilotUserId))
        {
            return true;
        }

        return false;
    }

    private int? ExtractIssueNumber(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        // Look for patterns like "Fixes #123", "Closes #456", etc.
        var pattern = @"(?:Fixes|Closes|Resolves)\s+#(\d+)";
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        
        if (match.Success && int.TryParse(match.Groups[1].Value, out int issueNumber))
        {
            return issueNumber;
        }

        return null;
    }
}
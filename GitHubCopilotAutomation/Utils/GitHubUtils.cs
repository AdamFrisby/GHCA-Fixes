using System.Text.RegularExpressions;

namespace GitHubCopilotAutomation.Utils;

public static class GitHubUtils
{
    public static int? ExtractIssueNumber(string? text)
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

    public static bool IsCopilotUser(string username, long userId, string[] copilotUsernames, long copilotUserId)
    {
        return copilotUsernames.Contains($"@{username}") || userId == copilotUserId;
    }
}
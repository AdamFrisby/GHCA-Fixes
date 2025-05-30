using GitHubCopilotAutomation.Utils;

namespace GitHubCopilotAutomation.Tests;

public class GitHubUtilsTests
{
    [Fact]
    public void ExtractIssueNumber_ValidPattern_ReturnsIssueNumber()
    {
        // Arrange
        var text = "This PR fixes #123 and improves the code.";

        // Act
        var result = GitHubUtils.ExtractIssueNumber(text);

        // Assert
        Assert.Equal(123, result);
    }

    [Fact]
    public void ExtractIssueNumber_ClosesPattern_ReturnsIssueNumber()
    {
        // Arrange
        var text = "Closes #456";

        // Act
        var result = GitHubUtils.ExtractIssueNumber(text);

        // Assert
        Assert.Equal(456, result);
    }

    [Fact]
    public void ExtractIssueNumber_ResolvesPattern_ReturnsIssueNumber()
    {
        // Arrange
        var text = "Resolves #789";

        // Act
        var result = GitHubUtils.ExtractIssueNumber(text);

        // Assert
        Assert.Equal(789, result);
    }

    [Fact]
    public void ExtractIssueNumber_NoPattern_ReturnsNull()
    {
        // Arrange
        var text = "This is just a regular PR description.";

        // Act
        var result = GitHubUtils.ExtractIssueNumber(text);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ExtractIssueNumber_EmptyString_ReturnsNull()
    {
        // Arrange
        var text = "";

        // Act
        var result = GitHubUtils.ExtractIssueNumber(text);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ExtractIssueNumber_NullString_ReturnsNull()
    {
        // Arrange
        string? text = null;

        // Act
        var result = GitHubUtils.ExtractIssueNumber(text);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void IsCopilotUser_MatchingUsername_ReturnsTrue()
    {
        // Arrange
        var username = "copilot";
        var userId = 123L;
        var copilotUsernames = new[] { "@copilot", "@apps/copilot-pull-request-reviewer" };
        var copilotUserId = 198982749L;

        // Act
        var result = GitHubUtils.IsCopilotUser(username, userId, copilotUsernames, copilotUserId);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsCopilotUser_MatchingUserId_ReturnsTrue()
    {
        // Arrange
        var username = "someuser";
        var userId = 198982749L;
        var copilotUsernames = new[] { "@copilot", "@apps/copilot-pull-request-reviewer" };
        var copilotUserId = 198982749L;

        // Act
        var result = GitHubUtils.IsCopilotUser(username, userId, copilotUsernames, copilotUserId);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsCopilotUser_NoMatch_ReturnsFalse()
    {
        // Arrange
        var username = "someuser";
        var userId = 123L;
        var copilotUsernames = new[] { "@copilot", "@apps/copilot-pull-request-reviewer" };
        var copilotUserId = 198982749L;

        // Act
        var result = GitHubUtils.IsCopilotUser(username, userId, copilotUsernames, copilotUserId);

        // Assert
        Assert.False(result);
    }
}
# Usage Examples

## Basic Setup

1. **Clone and build:**
   ```bash
   git clone <repository>
   cd GHCA-Fixes/GitHubCopilotAutomation
   dotnet build
   ```

2. **Create configuration:**
   ```bash
   cp appsettings.sample.json appsettings.json
   # Edit appsettings.json with your settings
   ```

## Command Line Examples

### Service Mode (Continuous Monitoring)

Run with configuration file:
```bash
dotnet run -- service --config appsettings.json
```

Run with command line parameters:
```bash
dotnet run -- service \
  --token "github_pat_your_token_here" \
  --owner "your-username" \
  --repo "your-repository" \
  --interval 30
```

### Interactive Mode (Manual Approval)

Run with configuration file:
```bash
dotnet run -- interactive --config appsettings.json
```

Run with command line parameters:
```bash
dotnet run -- interactive \
  --token "github_pat_your_token_here" \
  --owner "your-username" \
  --repo "your-repository"
```

## Sample Interactive Session

```
GitHub Copilot Automation - Interactive Mode
This will scan for draft PRs assigned to Copilot and ask for your approval before posting comments.

=== Pull Request Review ===
PR #123: Fix issue with user authentication
URL: https://github.com/owner/repo/pull/123
Linked Issue #45: Users cannot log in with SSO
Action: Request Copilot to review against issue #45 (attempt 1/3)
Comment to post: @copilot Please review the code in this PR against the original specification in #45, and verify your fix completely satisfies this issue.

Do you want to post this comment? (y/n): y
Posted comment to PR #123
Interactive scan completed.
```

## Testing

Run unit tests:
```bash
cd GitHubCopilotAutomation.Tests
dotnet test
```

## Deployment

Build for production:
```bash
dotnet publish -c Release -o ./publish
```

Run published version:
```bash
./publish/GitHubCopilotAutomation service --config appsettings.json
```

## Environment Variables

You can also set configuration via environment variables:
```bash
export GitHubToken="github_pat_your_token_here"
export Owner="your-username"
export Repository="your-repository"
dotnet run -- service
```
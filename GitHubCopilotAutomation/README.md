# GitHub Copilot Automation Engine

A C# service application that automates GitHub Copilot workflow management by monitoring draft pull requests and managing their lifecycle based on Copilot work status.

## Features

- **Service Mode**: Runs continuously, scanning for draft PRs at configurable intervals
- **Interactive Mode**: Manual approval required before posting comments
- **Rate Limiting**: Automatic exponential backoff for GitHub API rate limits
- **Configurable**: JSON configuration with command-line overrides
- **Copilot Integration**: Detects PRs assigned to Copilot and manages their workflow

## Installation

1. Clone the repository
2. Navigate to the `GitHubCopilotAutomation` directory
3. Run `dotnet restore` to install dependencies
4. Run `dotnet build` to compile the application

## Configuration

Create an `appsettings.json` file or use command-line arguments:

```json
{
  "GitHubToken": "your_github_token_here",
  "Owner": "repository_owner",
  "Repository": "repository_name",
  "ScanIntervalMinutes": 60,
  "MaxRetries": 3,
  "BaseDelaySeconds": 60,
  "BackoffMultiplier": 2.0,
  "CopilotUsernames": ["@copilot", "@apps/copilot-pull-request-reviewer"],
  "CopilotUserId": 198982749
}
```

### Configuration Options

- **GitHubToken**: GitHub Personal Access Token with repo permissions
- **Owner**: Repository owner (username or organization)
- **Repository**: Repository name
- **ScanIntervalMinutes**: How often to scan for PRs (default: 60 minutes)
- **MaxRetries**: Maximum review requests per PR (default: 3)
- **BaseDelaySeconds**: Base delay for exponential backoff (default: 60 seconds)
- **BackoffMultiplier**: Multiplier for exponential backoff (default: 2.0)
- **CopilotUsernames**: Array of Copilot usernames to detect
- **CopilotUserId**: Numeric ID of Copilot user

## Usage

### Service Mode

Run continuously with periodic scanning:

```bash
dotnet run -- service --token "your_token" --owner "owner" --repo "repository"
```

Or with a config file:

```bash
dotnet run -- service --config "path/to/config.json"
```

### Interactive Mode

Run once with manual approval for each action:

```bash
dotnet run -- interactive --token "your_token" --owner "owner" --repo "repository"
```

### Command Line Options

Both modes support these options:
- `--config`: Path to configuration file
- `--token`: GitHub personal access token
- `--owner`: Repository owner
- `--repo`: Repository name

Service mode additionally supports:
- `--interval`: Scan interval in minutes

## How It Works

1. **Scans Draft PRs**: Finds open draft pull requests assigned to Copilot
2. **Detects Linked Issues**: Extracts issue numbers from PR descriptions (e.g., "Fixes #123")
3. **Analyzes Copilot Activity**: Checks comments for completion or failure indicators
4. **Takes Action**:
   - If failure detected: Posts "@copilot please continue"
   - If completion detected: Posts review request (up to MaxRetries times)
5. **Rate Limiting**: Implements exponential backoff for API rate limits

## Requirements

- .NET 8.0 or later
- GitHub Personal Access Token with repository permissions
- Access to the target GitHub repository

## Logging

The application uses Microsoft.Extensions.Logging and outputs to the console. Logs include:
- PR processing status
- API interactions
- Error handling
- Rate limiting information

## Error Handling

- **Rate Limiting**: Automatic exponential backoff with configurable parameters
- **API Failures**: Logged with retry logic
- **Missing Configuration**: Clear error messages for required settings
- **Network Issues**: Graceful handling with appropriate delays

## Development

To extend or modify the application:

1. **Models**: Add new configuration options in `Models/AppConfig.cs`
2. **GitHub Integration**: Extend `Services/GitHubService.cs` for new API calls
3. **Automation Logic**: Modify `Services/AutomationService.cs` for workflow changes
4. **CLI**: Update `Program.cs` for new command-line options
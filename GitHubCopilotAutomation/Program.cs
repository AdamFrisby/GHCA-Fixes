using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using GitHubCopilotAutomation.Models;
using GitHubCopilotAutomation.Services;

// Create the root command
var rootCommand = new RootCommand("GitHub Copilot Automation Engine");

// Service mode command
var configOption = new Option<string?>("--config", "Path to configuration file");
var tokenOption = new Option<string?>("--token", "GitHub personal access token");
var ownerOption = new Option<string?>("--owner", "Repository owner");
var repoOption = new Option<string?>("--repo", "Repository name");
var intervalOption = new Option<int?>("--interval", "Scan interval in minutes");

var serviceCommand = new Command("service", "Run in service mode (periodic scanning)");
serviceCommand.AddOption(configOption);
serviceCommand.AddOption(tokenOption);
serviceCommand.AddOption(ownerOption);
serviceCommand.AddOption(repoOption);
serviceCommand.AddOption(intervalOption);

serviceCommand.SetHandler(async (string? configPath, string? token, string? owner, string? repo, int? interval) =>
{
    await RunServiceMode(configPath, token, owner, repo, interval);
}, configOption, tokenOption, ownerOption, repoOption, intervalOption);

// Interactive mode command
var interactiveCommand = new Command("interactive", "Run in interactive mode (manual approval)");
interactiveCommand.AddOption(configOption);
interactiveCommand.AddOption(tokenOption);
interactiveCommand.AddOption(ownerOption);
interactiveCommand.AddOption(repoOption);

interactiveCommand.SetHandler(async (string? configPath, string? token, string? owner, string? repo) =>
{
    await RunInteractiveMode(configPath, token, owner, repo);
}, configOption, tokenOption, ownerOption, repoOption);

rootCommand.AddCommand(serviceCommand);
rootCommand.AddCommand(interactiveCommand);

return await rootCommand.InvokeAsync(args);

static async Task RunServiceMode(string? configPath, string? token, string? owner, string? repo, int? interval)
{
    var host = CreateHost(configPath, token, owner, repo, interval);
    await host.RunAsync();
}

static async Task RunInteractiveMode(string? configPath, string? token, string? owner, string? repo)
{
    var host = CreateHost(configPath, token, owner, repo, null, serviceMode: false);
    
    using var scope = host.Services.CreateScope();
    var automationService = scope.ServiceProvider.GetRequiredService<IAutomationService>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    
    logger.LogInformation("Starting interactive mode...");
    Console.WriteLine("GitHub Copilot Automation - Interactive Mode");
    Console.WriteLine("This will scan for draft PRs assigned to Copilot and ask for your approval before posting comments.");
    Console.WriteLine();

    try
    {
        await automationService.ProcessPullRequestsAsync(interactive: true);
        Console.WriteLine("Interactive scan completed.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error during interactive scan");
        Console.WriteLine($"Error: {ex.Message}");
    }
}

static IHost CreateHost(string? configPath, string? token, string? owner, string? repo, int? interval, bool serviceMode = true)
{
    var builder = Host.CreateDefaultBuilder()
        .ConfigureAppConfiguration((context, config) =>
        {
            config.AddJsonFile("appsettings.json", optional: true);
            if (!string.IsNullOrEmpty(configPath))
            {
                config.AddJsonFile(configPath);
            }
        })
        .ConfigureServices((context, services) =>
        {
            // Configure AppConfig with command line overrides
            services.Configure<AppConfig>(appConfig =>
            {
                context.Configuration.Bind(appConfig);
                
                // Override with command line arguments if provided
                if (!string.IsNullOrEmpty(token))
                    appConfig.GitHubToken = token;
                if (!string.IsNullOrEmpty(owner))
                    appConfig.Owner = owner;
                if (!string.IsNullOrEmpty(repo))
                    appConfig.Repository = repo;
                if (interval.HasValue)
                    appConfig.ScanIntervalMinutes = interval.Value;
            });

            services.AddSingleton<IGitHubService, GitHubService>();
            services.AddSingleton<ICopilotAgentTracker, CopilotAgentTracker>();
            services.AddSingleton<IAutomationService, AutomationService>();
            
            if (serviceMode)
            {
                services.AddHostedService<AutomationHostedService>();
            }
        })
        .ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
        });

    return builder.Build();
}

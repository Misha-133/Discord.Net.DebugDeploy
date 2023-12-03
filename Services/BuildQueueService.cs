using System.Diagnostics;
using Discord.Net.DebugDeploy.EventProcessors;
using LibGit2Sharp;
using Octokit.Webhooks;

namespace Discord.Net.DebugDeploy.Services;

public class BuildQueueService(ILogger<BuildQueueService> logger, IConfiguration configuration, WebhookEventProcessor webhook) : IHostedService
{
    private string _repo;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Build queue service starting");

        _repo = configuration["Repository"]!;
        var path = Path.Combine(Environment.CurrentDirectory, "repo/");

        if (!Directory.Exists(path) || Directory.GetFiles(path).Length == 0)
        {
            logger.LogInformation("Cloning repo");
            Repository.Clone(_repo, path);
        }
        else
        {
            logger.LogInformation("Pulling repo");
            using (var repository = new Repository(Path.Combine(path)))
            {
                PullOptions options = new();
                var signature = new LibGit2Sharp.Signature(
                    new Identity("dnet_tool", "dnet_tool"), DateTimeOffset.Now);
                Commands.Pull(repository, signature, options);
            }
        }

        logger.LogInformation("Starting restoring");

        var p = Process.Start("dotnet", $"restore {path}");

        p.OutputDataReceived += (sender, args) =>
        {
            logger.LogInformation(args.Data ?? string.Empty);
        };

        await p.WaitForExitAsync(cancellationToken);
        logger.LogInformation("Restore complete");

        _ = Task.Run(() => BuildLoop(cancellationToken), cancellationToken);

        logger.LogInformation("Build queue service started");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        
    }

    public async Task BuildLoop(CancellationToken token)
    {
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token))
        {
            var queue = ((GithubWebhookEventProcessor)webhook).BuildQueue;

            if (queue.TryDequeue(out var branch))
            {
                logger.LogInformation($"Building {branch}");
                var path = Path.Combine(Environment.CurrentDirectory, "repo/");
                var p = Process.Start("dotnet", $"build {path} -c Release --output {Path.Combine(Environment.CurrentDirectory, "builds", branch)} --version-suffix {branch}");

                p.OutputDataReceived += (sender, args) =>
                {
                    logger.LogInformation(args.Data ?? string.Empty);
                };

                await p.WaitForExitAsync(token);
                logger.LogInformation($"Build {branch} complete");
            }

            await Task.Delay(1000, token);
        }
    }
}
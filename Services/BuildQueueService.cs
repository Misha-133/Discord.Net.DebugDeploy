using System.Diagnostics;
using LibGit2Sharp;

namespace Discord.Net.DebugDeploy.Services;

public class BuildQueueService(ILogger<BuildQueueService> logger, IConfiguration configuration) : IHostedService
{
    public Queue<string> BuildQueue { get; } = new();

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
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        
    }
}
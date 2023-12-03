using System.Diagnostics;
using System.IO;
using System.Threading;

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

        logger.LogInformation("Starting restore");

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
            try
            {
                var queue = ((GithubWebhookEventProcessor)webhook).BuildQueue;

                if (queue.TryDequeue(out var branch))
                {
                    logger.LogInformation($"Building {branch}");

                    var path = Path.Combine(Environment.CurrentDirectory, "repo/");

                    try
                    {
                        using (var repository = new Repository(Path.Combine(path)))
                        {
                            var remote = repository.Network.Remotes["origin"];
                            var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);
                            Commands.Fetch(repository, remote.Name, refSpecs, null, "fetch");

                            Commands.Checkout(repository, branch);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to checkout branch");
                        continue;
                    }


                    logger.LogInformation("Starting restore");

                    var p = Process.Start("dotnet", $"restore {path}");

                    p.OutputDataReceived += (sender, args) => { logger.LogDebug(args.Data ?? string.Empty); };

                    await p.WaitForExitAsync(token);

                    logger.LogInformation("Restore complete");


                    var suffix = branch.Replace('/', '-');

                    p = Process.Start("dotnet", $"build {Path.Combine(path, "Discord.Net.sln")} --no-restore -c Release -v minimal --version-suffix {suffix} -p:TreatWarningsAsErrors=False");

                    p.OutputDataReceived += (sender, args) => { logger.LogDebug(args.Data ?? string.Empty); };

                    await p.WaitForExitAsync(token);

                    logger.LogInformation($"Build {branch} complete");


                    foreach (var (exe, args) in PackSteps)
                    {
                        p = Process.Start(exe, string.Format(args, suffix));

                        p.OutputDataReceived += (sender, args) => { logger.LogDebug(args.Data ?? string.Empty); };

                        await p.WaitForExitAsync(token);
                    }

                    logger.LogInformation($"Pack {branch} complete");


                    foreach (var file in FindNugetFiles(path, suffix))
                    {
                        p = Process.Start("dotnet", $"nuget push \"{file}\" -s {configuration["Nuget"]} -k {configuration["NugetKey"]}");

                        p.OutputDataReceived += (sender, args) => { logger.LogDebug(args.Data ?? string.Empty); };

                        await p.WaitForExitAsync(token);
                        File.Delete(file);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Run failed");
            }
        }
    }

    private (string, string)[] PackSteps =
    {
        ("dotnet", "pack \"repo/src/Discord.Net.Core/Discord.Net.Core.csproj\" --no-restore --no-build -v minimal --version-suffix {0}"),
        ("dotnet", "pack \"repo/src/Discord.Net.Rest/Discord.Net.Rest.csproj\" --no-restore --no-build -v minimal --version-suffix {0}"),
        ("dotnet", "pack \"repo/src/Discord.Net.WebSocket/Discord.Net.WebSocket.csproj\" --no-restore --no-build -v minimal --version-suffix {0}"),
        ("dotnet", "pack \"repo/src/Discord.Net.Commands/Discord.Net.Commands.csproj\" --no-restore --no-build -v minimal --version-suffix {0}"),
        ("dotnet", "pack \"repo/src/Discord.Net.Webhook/Discord.Net.Webhook.csproj\" --no-restore --no-build -v minimal --version-suffix {0}"),
        ("dotnet", "pack \"repo/src/Discord.Net.Interactions/Discord.Net.Interactions.csproj\" --no-restore --no-build -v minimal --version-suffix {0}"),
        //("dotnet", "nuget pack \"repo/src/Discord.Net/Discord.Net.nuspec\" -suffix {0}")
    };

    private string[] FindNugetFiles(string path, string branch)
    {
        var files = Directory.GetFiles(path, $"*{branch}.nupkg", SearchOption.AllDirectories);
        return files;
    }
}
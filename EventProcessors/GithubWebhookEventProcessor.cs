using Discord.Net.DebugDeploy.Services;
using Octokit.Webhooks;
using Octokit.Webhooks.Events;

namespace Discord.Net.DebugDeploy.EventProcessors;

public sealed class GithubWebhookEventProcessor(ILogger<GithubWebhookEventProcessor> logger) : WebhookEventProcessor
{
    public Queue<string> BuildQueue { get; } = new();

    protected override async Task ProcessPushWebhookAsync(WebhookHeaders headers, PushEvent pushEvent)
    {
        logger.LogInformation("Received push");

        var cleanedRef = pushEvent.Ref.Replace("refs/heads/", string.Empty);
        BuildQueue.Enqueue(cleanedRef);
    }
}
using Octokit.Webhooks;
using Octokit.Webhooks.Events;

namespace Discord.Net.DebugDeploy.EventProcessors;

public sealed class GithubWebhookEventProcessor(ILogger<GithubWebhookEventProcessor> logger) : WebhookEventProcessor
{
    protected override async Task ProcessPushWebhookAsync(WebhookHeaders headers, PushEvent pushEvent)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(5500);
            var h = headers;
            var e = pushEvent;
            Console.WriteLine("Push event processed!");
        });
    }
}
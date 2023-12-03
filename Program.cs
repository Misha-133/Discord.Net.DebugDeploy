using Discord.Net.DebugDeploy.EventProcessors;
using Discord.Net.DebugDeploy.Services;
using Octokit.Webhooks;
using Octokit.Webhooks.AspNetCore;


var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables("DNET_");

builder.Services.AddSingleton<WebhookEventProcessor, GithubWebhookEventProcessor>();

builder.Services.AddHostedService<BuildQueueService>();

builder.Services.AddControllers();

var app = builder.Build();

app.MapGitHubWebhooks("/api/github/webhooks", app.Configuration["GithubSecret"]!);


app.MapControllers();

app.Run();

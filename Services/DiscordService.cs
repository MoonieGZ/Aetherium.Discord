using System.Reflection;
using System.Text.RegularExpressions;
using Aetherium.Options;
using Aetherium.Util;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Options;

namespace Aetherium.Services;

public class DiscordService(
        DiscordShardedClient discordShardedClient,
        IOptions<DiscordOptions> discordBotOptions,
        InteractionService interactionService,
        CommandService commandService,
        IServiceProvider serviceProvider,
        LogAdapter<BaseSocketClient> adapter,
        ILogger<DiscordService> logger)
    // ReSharper disable once GrammarMistakeInComment
    // InteractiveService interactiveService
    : BackgroundService
{
    private int _shardsReady;
    private TaskCompletionSource<bool>? _taskCompletionSource;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            discordShardedClient.Log += adapter.Log;
            discordShardedClient.ShardDisconnected += OnShardDisconnected;
            discordShardedClient.MessageReceived += OnMessageReceived;

            PrepareClientAwaiter();
            await discordShardedClient.LoginAsync(TokenType.Bot, discordBotOptions.Value.Token);
            await discordShardedClient.StartAsync();
            await WaitForReadyAsync(stoppingToken);

            await commandService.AddModulesAsync(Assembly.GetEntryAssembly(), serviceProvider);
            await interactionService.AddModulesAsync(Assembly.GetEntryAssembly(), serviceProvider);

            await discordShardedClient.Rest.DeleteAllGlobalCommandsAsync();
            await discordShardedClient.GetGuild(discordBotOptions.Value.LogServerId).DeleteApplicationCommandsAsync();

            await interactionService.RegisterCommandsToGuildAsync(discordBotOptions.Value.LogServerId);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Exception in DiscordStartupService");
        }
    }

    private async Task OnMessageReceived(SocketMessage arg)
    {
        if (arg.Channel.Id != discordBotOptions.Value.HoyoLabAutoChannelId)
            return;
        if (!arg.Author.IsWebhook)
            return;

        if (arg.Embeds.Count == 1)
        {
            ulong destinationChannel = 0;
            var game = string.Empty;

            switch (arg.Author.Username)
            {
                case "HoyoLab":
                    switch (arg.Embeds.First().Author!.Value.Name)
                    {
                        case "Paimon":
                            destinationChannel = discordBotOptions.Value.GIChannelId;
                            game = "gi";
                            break;
                        case "PomPom":
                            destinationChannel = discordBotOptions.Value.HSRChannelId;
                            game = "hsr";
                            break;
                        case "Eous":
                            destinationChannel = discordBotOptions.Value.ZZZChannelId;
                            game = "zzz";
                            break;
                    }

                    break;
                case "Paimon":
                    destinationChannel = discordBotOptions.Value.GIChannelId;
                    game = "gi";
                    break;
                case "PomPom":
                    destinationChannel = discordBotOptions.Value.HSRChannelId;
                    game = "hsr";
                    break;
                case "Eous":
                    destinationChannel = discordBotOptions.Value.ZZZChannelId;
                    game = "zzz";
                    break;
            }

            var embed = arg.Embeds.First();

            if (embed.Title.Contains("Code Redeem"))
            {
                if (embed.Description.Contains("Moons") && embed.Description.Contains("Code Successfully Redeemed"))
                {
                    const string codePattern = @"Code:\s*(\S+)";
                    const string rewardsPattern = @"Rewards:\s*(.*)";
                    
#pragma warning disable SYSLIB1045
                    var codeMatch = Regex.Match(embed.Description, codePattern);
                    var rewardsMatch = Regex.Match(embed.Description, rewardsPattern);
#pragma warning restore SYSLIB1045
                    
                    var code = codeMatch.Success ? codeMatch.Groups[1].Value : "Code not found";
                    var rewards = rewardsMatch.Success ? rewardsMatch.Groups[1].Value : "Rewards not found";

                    if(code != "Code not found")
                        await SendToKlys(game, code, rewards);
                }
            }

            if (destinationChannel != 0)
                await discordShardedClient.GetGuild(discordBotOptions.Value.AetheriumServerId)
                    .GetTextChannel(destinationChannel)
                    .SendMessageAsync(string.IsNullOrEmpty(arg.Content) ? null : arg.Content,
                        embed: arg.Embeds.First());
            else
                logger.LogWarning("Unknown webhook author: {author}", arg.Author.Username);
        }
    }

    private async Task SendToKlys(string game, string code, string rewards)
    {
        var message = $"**Code:** {code}\n **Rewards:** {rewards}";
        
        switch (game)
        {
            case "gi":
                await DiscordWebhookService.SendMessageAsync(discordBotOptions.Value.KlysGIChannelWebhook, message);
                break;
            case "hsr":
                await DiscordWebhookService.SendMessageAsync(discordBotOptions.Value.KlysHSRChannelWebhook, message);
                break;
            case "zzz":
                // Ignored.
                break;
        }
    }

    private async Task OnShardDisconnected(Exception arg1, DiscordSocketClient arg2)
    {
        logger.LogError(arg1, "Disconnected from gateway.");

        if (arg1.InnerException is GatewayReconnectException &&
            arg1.InnerException.Message == "Server missed last heartbeat")
        {
            await arg2.StopAsync();
            await Task.Delay(10000);
            await arg2.StartAsync();
        }
    }

    private Task OnShardReady(DiscordSocketClient discordClient)
    {
        logger.LogInformation(
            "Connected as {name}#{discriminator}", discordClient.CurrentUser.Username,
            discordClient.CurrentUser.DiscriminatorValue);

        discordShardedClient.SetStatusAsync(UserStatus.Online);
        discordShardedClient.SetGameAsync("Waiting for a re-run.");

        _shardsReady++;

        if (_shardsReady != discordShardedClient.Shards.Count)
            return Task.CompletedTask;

        _taskCompletionSource!.TrySetResult(true);
        discordShardedClient.ShardReady -= OnShardReady;

        return Task.CompletedTask;
    }

    private void PrepareClientAwaiter()
    {
        _taskCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _shardsReady = 0;

        discordShardedClient.ShardReady += OnShardReady;
    }

    private Task WaitForReadyAsync(CancellationToken cancellationToken)
    {
        if (_taskCompletionSource is null)
            throw new InvalidOperationException(
                "The sharded client has not been registered correctly. Did you use ConfigureDiscordShardedHost on your HostBuilder?");

        if (_taskCompletionSource.Task.IsCompleted)
            return _taskCompletionSource.Task;

        var registration = cancellationToken.Register(
            state => { ((TaskCompletionSource<bool>)state!).TrySetResult(true); },
            _taskCompletionSource);

        return _taskCompletionSource.Task.ContinueWith(_ => registration.DisposeAsync(), cancellationToken);
    }
}

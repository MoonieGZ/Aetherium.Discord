using Discord;

namespace Aetherium.Options;

public class DiscordOptions
{
    public string? Token { get; set; }
    public ulong LogServerId { get; set; }
    public ulong AetheriumServerId { get; set; }
    public ulong LogChannelId { get; set; }
    public ulong HoyoLabAutoChannelId { get; set; }
    public ulong GIChannelId { get; set; }
    public ulong HSRChannelId { get; set; }
    public ulong ZZZChannelId { get; set; }
    public string KlysGIChannelWebhook { get; set; } = "";
    public string KlysHSRChannelWebhook { get; set; } = "";

    public Func<LogMessage, Exception?, string> LogFormat { get; set; } =
        (message, _) => $"{message.Source}: {message.Message}";
}

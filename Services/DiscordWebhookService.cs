using System.Text;
using System.Text.Json;

namespace Aetherium.Services;

// ReSharper disable once ClassNeverInstantiated.Global
public class DiscordWebhookService
{
    private static HttpClient? _httpClient;

    public static async Task SendMessageAsync(string webhookUrl, string message)
    {
        _httpClient ??= new HttpClient();

        var payload = new
        {
            content = message
        };

        var jsonPayload = JsonSerializer.Serialize(payload);
        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(webhookUrl, content);
        response.EnsureSuccessStatusCode();
    }
}
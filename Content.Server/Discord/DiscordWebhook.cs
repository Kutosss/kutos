using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Content.Server.Discord;

public sealed class DiscordWebhook : IPostInjectInit
{
    [Dependency] private readonly ILogManager _log = default!;

    private const string BaseUrl = "https://discord.com/api/v10/webhooks";
    private readonly HttpClient _http = new();
    private ISawmill _sawmill = default!;

    private string GetUrl(WebhookIdentifier identifier)
    {
        return $"{BaseUrl}/{identifier.Id}/{identifier.Token}";
    }
    
    /// <summary>
    ///     Создает URL для вебхука, используя только токен.
    /// </summary>
    /// <param name="token">Токен вебхука</param>
    /// <returns>URL для вебхука</returns>
    private string GetUrlFromToken(string token)
    {
        // Важно! Токен, который мы получили, скорее всего уже включает в себя ID и токен
        // Обычно токен для вебхука Discord выглядит так:
        // https://discord.com/api/webhooks/1234567890123456789/abcdefghijklmnopqrstuvwxyz
        // Где:
        // 1234567890123456789 - ID вебхука
        // abcdefghijklmnopqrstuvwxyz - токен вебхука
        
        // Проверяем, содержит ли токен URL с ID
        if (token.Contains("/api/webhooks/"))
        {
            // Просто используем URL как есть, если это полный URL
            return token;
        }
        
        // Проверяем, содержит ли токен разделитель ID/token
        if (token.Contains('/'))
        {
            var parts = token.Split('/');
            if (parts.Length >= 2)
            {
                var id = parts[^2];
                var actualToken = parts[^1];
                return $"{BaseUrl}/{id}/{actualToken}";
            }
        }
        
        // Иначе просто пробуем использовать как есть
        // Это не должно работать для Discord, но оставим для обратной совместимости
        _sawmill.Warning($"Webhook token format may be incorrect: {token}. Expected format: ID/TOKEN");
        return $"{BaseUrl}/0/{token}";
    }

    /// <summary>
    ///     Gets the webhook data from the given webhook url.
    /// </summary>
    /// <param name="url">The url to get the data from.</param>
    /// <returns>The webhook data returned from the url.</returns>
    public async Task<WebhookData?> GetWebhook(string url)
    {
        try
        {
            return await _http.GetFromJsonAsync<WebhookData>(url);
        }
        catch
        {
            _sawmill.Error($"Error getting discord webhook data. Stack trace:\n{Environment.StackTrace}");
            return null;
        }
    }

    /// <summary>
    ///     Gets the webhook data from the given webhook url.
    /// </summary>
    /// <param name="url">The url to get the data from.</param>
    /// <param name="onComplete">The delegate to invoke with the obtained data, if any.</param>
    public async void GetWebhook(string url, Action<WebhookData> onComplete)
    {
        if (await GetWebhook(url) is { } data)
            onComplete(data);
    }

    /// <summary>
    ///     Tries to get the webhook data from the given webhook url if it is not null or whitespace.
    /// </summary>
    /// <param name="url">The url to get the data from.</param>
    /// <param name="onComplete">The delegate to invoke with the obtained data, if any.</param>
    public async void TryGetWebhook(string url, Action<WebhookData> onComplete)
    {
        if (await GetWebhook(url) is { } data)
            onComplete(data);
    }

    /// <summary>
    ///     Creates a new webhook message with the given identifier and payload.
    /// </summary>
    /// <param name="identifier">The identifier for the webhook url.</param>
    /// <param name="payload">The payload to create the message from.</param>
    /// <returns>The response from Discord's API.</returns>
    public async Task<HttpResponseMessage> CreateMessage(WebhookIdentifier identifier, WebhookPayload payload)
    {
        var url = $"{GetUrl(identifier)}?wait=true";
        return await _http.PostAsJsonAsync(url, payload, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
    }
    
    /// <summary>
    ///     Creates a new webhook message using only the token and payload.
    /// </summary>
    /// <param name="token">Токен вебхука</param>
    /// <param name="payload">The payload to create the message from.</param>
    /// <returns>The response from Discord's API.</returns>
    public async Task<HttpResponseMessage> CreateMessageWithToken(string token, WebhookPayload payload)
    {
        var url = $"{GetUrlFromToken(token)}?wait=true";
        _sawmill.Debug($"Sending webhook to URL: {url}");
        
        try 
        {
            var response = await _http.PostAsJsonAsync(url, payload, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
            
            // Логируем результат
            if (response.IsSuccessStatusCode)
            {
                _sawmill.Info($"Webhook sent successfully. Status: {response.StatusCode}");
                var content = await response.Content.ReadAsStringAsync();
                _sawmill.Debug($"Response content: {content}");
            }
            else
            {
                _sawmill.Error($"Failed to send webhook. Status: {response.StatusCode}");
                var content = await response.Content.ReadAsStringAsync();
                _sawmill.Error($"Error response: {content}");
            }
            
            return response;
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Exception when sending webhook: {ex}");
            throw;
        }
    }

    /// <summary>
    ///     Deletes a webhook message with the given identifier and message id.
    /// </summary>
    /// <param name="identifier">The identifier for the webhook url.</param>
    /// <param name="messageId">The message id to delete.</param>
    /// <returns>The response from Discord's API.</returns>
    public async Task<HttpResponseMessage> DeleteMessage(WebhookIdentifier identifier, ulong messageId)
    {
        var url = $"{GetUrl(identifier)}/messages/{messageId}";
        return await _http.DeleteAsync(url);
    }

    /// <summary>
    ///     Creates a new webhook message with the given identifier, message id and payload.
    /// </summary>
    /// <param name="identifier">The identifier for the webhook url.</param>
    /// <param name="messageId">The message id to edit.</param>
    /// <param name="payload">The payload used to edit the message.</param>
    /// <returns>The response from Discord's API.</returns>
    public async Task<HttpResponseMessage> EditMessage(WebhookIdentifier identifier, ulong messageId, WebhookPayload payload)
    {
        var url = $"{GetUrl(identifier)}/messages/{messageId}";
        return await _http.PatchAsJsonAsync(url, payload, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
    }

    void IPostInjectInit.PostInject()
    {
        _sawmill = _log.GetSawmill("DISCORD");
    }
}

using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Shared.Log;

namespace Content.Server.Discord;

public sealed class DiscordWebhook : IPostInjectInit
{
    [Dependency] private readonly ILogManager _log = default!;
    [Dependency] private readonly IConfigurationManager _configManager = default!;

    private const string BaseUrl = "https://discord.com/api/v10/webhooks";
    private readonly HttpClient _http = new();
    private ISawmill _sawmill = default!;
    
    /// <summary>
    /// Получает текущий уровень логирования из CVar
    /// </summary>
    private int LogLevel => _configManager.GetCVar(CCVars.DiscordWebhookLogLevel);
    
    /// <summary>
    /// Логирование с учетом настроенного уровня
    /// </summary>
    private void LogError(string message)
    {
        // Ошибки логируем всегда, независимо от уровня
        _sawmill.Error(message);
    }
    
    /// <summary>
    /// Логирование с учетом настроенного уровня
    /// </summary>
    private void LogWarning(string message)
    {
        // Предупреждения логируем всегда, независимо от уровня
        _sawmill.Warning(message);
    }
    
    /// <summary>
    /// Логирование с учетом настроенного уровня
    /// </summary>
    private void LogInfo(string message)
    {
        // Информационные сообщения логируем только если уровень >= 1
        if (LogLevel >= 1)
            _sawmill.Info(message);
    }
    
    /// <summary>
    /// Логирование с учетом настроенного уровня
    /// </summary>
    private void LogDebug(string message)
    {
        // Отладочные сообщения логируем только если уровень >= 2
        if (LogLevel >= 2)
            _sawmill.Debug(message);
    }

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
                
                // Проверяем, что ID является числовым значением
                if (ulong.TryParse(id, out _))
                {
                    return $"{BaseUrl}/{id}/{actualToken}";
                }
                
                LogWarning($"Invalid webhook ID format: {id}. ID should be a number.");
            }
        }
        
        // Если формат не соответствует ожидаемому, выбрасываем исключение
        LogError($"Webhook token format is incorrect: {token}. Expected format: ID/TOKEN or full URL.");
        throw new FormatException($"Invalid webhook token format: {token}. Expected format: ID/TOKEN or full URL.");
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
            LogError($"Error getting discord webhook data. Stack trace:\n{Environment.StackTrace}");
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
        LogDebug($"Sending webhook to URL: {url}");
        
        try 
        {
            var response = await _http.PostAsJsonAsync(url, payload, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
            
            // Логируем результат
            if (response.IsSuccessStatusCode)
            {
                LogInfo($"Webhook sent successfully. Status: {response.StatusCode}");
                var content = await response.Content.ReadAsStringAsync();
                LogDebug($"Response content: {content}");
            }
            else
            {
                LogError($"Failed to send webhook. Status: {response.StatusCode}");
                var content = await response.Content.ReadAsStringAsync();
                LogError($"Error response: {content}");
            }
            
            return response;
        }
        catch (Exception ex)
        {
            LogError($"Exception when sending webhook: {ex}");
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
        LogDebug("Discord webhook service initialized");
    }
}

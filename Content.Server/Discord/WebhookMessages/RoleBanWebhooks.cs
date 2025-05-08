using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Content.Server.Administration.Managers;
using Content.Shared.Database;
using Robust.Server.Player;
using Robust.Shared.IoC;
using Robust.Shared.Log;

namespace Content.Server.Discord.WebhookMessages;

/// <summary>
/// Отправляет уведомления о ролевых банах через Discord вебхуки.
/// Использует тот же файл конфигурации, что и BanWebhooks.
/// </summary>
public sealed class RoleBanWebhooks : IPostInjectInit
{
    [Dependency] private readonly DiscordWebhook _webhook = default!;
    [Dependency] private readonly IBanManager _banManager = default!;
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    private ISawmill _sawmill = default!;
    private string _webhookToken = string.Empty;
    private bool _enabled;
    
    // Путь к конфигурационному файлу с токеном вебхука (тот же, что используется для обычных банов)
    private const string WebhookConfigPath = "config/discord_webhook.cfg";

    void IPostInjectInit.PostInject()
    {
        _sawmill = _logManager.GetSawmill("discord.roleban_webhooks");
        
        // Загружаем токен из конфигурационного файла
        LoadWebhookToken();
        
        // Проверяем, что токен загружен
        if (string.IsNullOrEmpty(_webhookToken))
        {
            _sawmill.Warning("Discord webhook token is not set. Role ban notifications will not be sent.");
            _enabled = false;
        }
        else
        {
            _enabled = true;
            _sawmill.Info("Role ban Discord webhook initialized successfully");
        }

        // Подписываемся на событие ролевых банов
        _banManager.RoleBanAdded += OnRoleBanAdded;
    }

    /// <summary>
    /// Загружает токен вебхука из конфигурационного файла.
    /// </summary>
    private void LoadWebhookToken()
    {
        try
        {
            // Проверяем, существует ли файл конфигурации
            if (!System.IO.File.Exists(WebhookConfigPath))
            {
                _sawmill.Warning($"Discord webhook config file not found at {WebhookConfigPath}.");
                return;
            }

            // Читаем файл конфигурации
            var lines = System.IO.File.ReadAllLines(WebhookConfigPath);
            foreach (var line in lines)
            {
                // Пропускаем комментарии и пустые строки
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;
                
                // Ищем строку с токеном
                if (line.StartsWith("webhook_url="))
                {
                    _webhookToken = line.Substring("webhook_url=".Length).Trim();
                    _sawmill.Debug("Discord webhook URL loaded from config file");
                    return;
                }
            }
            
            _sawmill.Warning($"No webhook_url found in {WebhookConfigPath}");
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Error loading Discord webhook configuration: {ex}");
        }
    }

    /// <summary>
    /// Метод для отправки уведомления о новом ролевом бане.
    /// Должен вызываться из BanManager при добавлении ролевого бана.
    /// </summary>
    public async void OnRoleBanAdded(string targetName, string role, string adminName, string reason, DateTimeOffset banTime, DateTimeOffset? expirationTime)
    {
        if (!_enabled || string.IsNullOrEmpty(_webhookToken))
            return;
        
        try
        {
            await SendRoleBanWebhook(targetName, role, adminName, reason, banTime, expirationTime);
            _sawmill.Info($"Role ban webhook sent for user: {targetName}, role: {role}");
        }
        catch (Exception e)
        {
            _sawmill.Error($"Error sending role ban webhook: {e}");
        }
    }
    
    private async Task SendRoleBanWebhook(string targetName, string role, string adminName, string reason, 
        DateTimeOffset banTime, DateTimeOffset? expirationTime)
    {
        var embed = new WebhookEmbed
        {
            Title = $"Джоббан: {role}",
            Description = FormatRoleBanInfo(targetName, role, adminName, reason, banTime, expirationTime),
            Color = 0xFFA500 // Оранжевый цвет для ролевых банов
        };

        var payload = new WebhookPayload
        {
            Username = "Вахтер",
            Embeds = new List<WebhookEmbed> { embed }
        };

        await _webhook.CreateMessageWithToken(_webhookToken, payload);
    }

    private string FormatRoleBanInfo(string targetName, string role, string adminName, string reason, 
        DateTimeOffset banTime, DateTimeOffset? expirationTime)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine($"**Игрок:** {targetName}");
        sb.AppendLine($"**Роль:** {role}");
        sb.AppendLine($"**Администратор:** {adminName}");
        sb.AppendLine($"**Причина:** {reason}");
        sb.AppendLine($"**Выдан:** {banTime}");
        
        if (expirationTime.HasValue)
        {
            sb.AppendLine($"**Истекает:** {expirationTime.Value}");
        }
        else
        {
            sb.AppendLine("**Истекает:** Никогда");
        }

        return sb.ToString();
    }
} 
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Server.Player;

namespace Content.Server.Discord.WebhookMessages;

public sealed class BanWebhooks : IPostInjectInit
{
    [Dependency] private readonly DiscordWebhook _webhook = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly IBanManager _banManager = default!;
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    private ISawmill _sawmill = default!;
    private string _webhookToken = string.Empty;
    private bool _enabled;
    
    // Путь к конфигурационному файлу с токеном вебхука
    private const string WebhookConfigPath = "config/discord_webhook.cfg";

    void IPostInjectInit.PostInject()
    {
        _sawmill = _logManager.GetSawmill("discord.ban_webhooks");
        
        // Пытаемся загрузить токен из конфигурационного файла
        LoadWebhookToken();
        
        // Проверяем, что токен загружен
        if (string.IsNullOrEmpty(_webhookToken))
        {
            _sawmill.Warning("Discord webhook token is not set. Ban notifications will not be sent.");
            _enabled = false;
        }
        else
        {
            _enabled = true;
            _sawmill.Info("Ban Discord webhook initialized successfully");
        }

        // Подписываемся на события банов
        _banManager.BanAdded += OnBanAdded;
    }

    /// <summary>
    /// Загружает токен вебхука из конфигурационного файла.
    /// Если файл не существует, создает пример файла конфигурации.
    /// </summary>
    private void LoadWebhookToken()
    {
        try
        {
            // Проверяем, существует ли файл конфигурации
            if (!File.Exists(WebhookConfigPath))
            {
                // Создаем директорию, если не существует
                Directory.CreateDirectory(Path.GetDirectoryName(WebhookConfigPath) ?? "config");
                
                // Создаем пример файла конфигурации
                File.WriteAllText(WebhookConfigPath, 
                    "# Discord webhook URL for ban notifications\n" +
                    "# Replace with your actual webhook URL\n" +
                    "# Example: https://discord.com/api/webhooks/YOUR_WEBHOOK_ID/YOUR_WEBHOOK_TOKEN\n" +
                    "webhook_url=");
                
                _sawmill.Warning($"Discord webhook config file created at {WebhookConfigPath}. Please edit it with your actual webhook URL.");
                return;
            }

            // Читаем файл конфигурации
            var lines = File.ReadAllLines(WebhookConfigPath);
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

    private async void OnBanAdded(ServerBanDef ban)
    {
        if (!_enabled || string.IsNullOrEmpty(_webhookToken))
            return;
        
        try
        {
            await SendBanWebhook(ban);
            _sawmill.Info($"Ban webhook sent for user: {ban.UserId}");
        }
        catch (Exception e)
        {
            _sawmill.Error($"Error sending ban webhook: {e}");
        }
    }
    
    private async Task SendBanWebhook(ServerBanDef ban)
    {
        var embed = new WebhookEmbed
        {
            Title = "Серверный бан",
            Description = await FormatBanInfo(ban),
            Color = 0xE74C3C // Красный цвет
        };

        var payload = new WebhookPayload
        {
            Username = "Вахтер",
            Embeds = new List<WebhookEmbed> { embed }
        };

        await _webhook.CreateMessageWithToken(_webhookToken, payload);
    }

    private async Task<string> FormatBanInfo(ServerBanDef ban)
    {
        var sb = new StringBuilder();
        
        // Пытаемся получить имя игрока вместо его ID
        string playerName = "Неизвестно";
        if (ban.UserId != null)
        {
            // Сначала пробуем получить имя из текущей сессии (если игрок онлайн)
            if (_playerManager.TryGetSessionById(ban.UserId.Value, out var player))
            {
                playerName = player.Name;
            }
            else
            {
                // Если игрок оффлайн, пытаемся получить его имя из базы данных
                try 
                {
                    var dbRecord = await IoCManager.Resolve<IServerDbManager>().GetPlayerRecordByUserId(ban.UserId.Value);
                    if (dbRecord != null && !string.IsNullOrEmpty(dbRecord.LastSeenUserName))
                    {
                        playerName = dbRecord.LastSeenUserName;
                    }
                }
                catch (Exception ex)
                {
                    _sawmill.Error($"Ошибка при получении имени игрока из базы данных: {ex}");
                }
            }
        }
        
        sb.AppendLine($"**Игрок:** {playerName}");
        
        // IP-адрес и HWID не отображаются по запросу
        
        // Пытаемся получить имя администратора вместо его ID
        string adminName = "Система";
        if (ban.BanningAdmin != null)
        {
            // Сначала пробуем получить имя администратора из текущей сессии
            if (_playerManager.TryGetSessionById(ban.BanningAdmin.Value, out var admin))
            {
                adminName = admin.Name;
            }
            else
            {
                // Если администратор оффлайн, пытаемся получить его имя из базы данных
                try 
                {
                    var dbRecord = await IoCManager.Resolve<IServerDbManager>().GetPlayerRecordByUserId(ban.BanningAdmin.Value);
                    if (dbRecord != null && !string.IsNullOrEmpty(dbRecord.LastSeenUserName))
                    {
                        adminName = dbRecord.LastSeenUserName;
                    }
                }
                catch (Exception ex)
                {
                    _sawmill.Error($"Ошибка при получении имени администратора из базы данных: {ex}");
                }
            }
        }
        
        sb.AppendLine($"**Администратор:** {adminName}");
        sb.AppendLine($"**Причина:** {ban.Reason}");
        // Уровень серьезности не отображается по запросу
        sb.AppendLine($"**Выдан:** {ban.BanTime}");
        
        if (ban.ExpirationTime.HasValue)
        {
            sb.AppendLine($"**Истекает:** {ban.ExpirationTime.Value}");
        }
        else
        {
            sb.AppendLine("**Истекает:** Никогда");
        }

        return sb.ToString();
    }
} 
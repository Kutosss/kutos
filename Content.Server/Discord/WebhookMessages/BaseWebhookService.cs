using System;
using System.IO;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Content.Server.Discord;

namespace Content.Server.Discord.WebhookMessages;

/// <summary>
/// Базовый класс для сервисов отправки сообщений через Discord вебхуки.
/// Реализует общую логику загрузки настроек и обработки токенов.
/// </summary>
public abstract class BaseWebhookService : IPostInjectInit
{
    [Dependency] protected readonly DiscordWebhook Webhook = default!;
    [Dependency] private readonly ILogManager _logManager = default!;
    
    protected string WebhookToken = string.Empty;
    protected bool Enabled;
    protected ISawmill Sawmill = default!;
    
    /// <summary>
    /// Путь к конфигурационному файлу с токеном вебхука
    /// </summary>
    protected virtual string WebhookConfigPath => "config/discord_webhook.cfg";
    
    /// <summary>
    /// Имя для логгера (sawmill)
    /// </summary>
    protected abstract string SawmillName { get; }
    
    public virtual void PostInject()
    {
        Sawmill = _logManager.GetSawmill(SawmillName);
        
        // Загружаем токен вебхука
        LoadWebhookToken();
        
        if (string.IsNullOrEmpty(WebhookToken))
        {
            Sawmill.Warning("Discord webhook token is not set. Notifications will not be sent.");
            Enabled = false;
        }
        else
        {
            Enabled = true;
            Sawmill.Info("Discord webhook initialized successfully");
        }
        
        // Подписки на события должны быть реализованы в производных классах
    }
    
    /// <summary>
    /// Загружает токен вебхука из конфигурационного файла.
    /// </summary>
    protected virtual void LoadWebhookToken()
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
                    "# Discord webhook URL for notifications\n" +
                    "# Replace with your actual webhook URL\n" +
                    "# Example: https://discord.com/api/webhooks/YOUR_WEBHOOK_ID/YOUR_WEBHOOK_TOKEN\n" +
                    "webhook_url=");
                
                Sawmill.Warning($"Discord webhook config file created at {WebhookConfigPath}. Please edit it with your actual webhook URL.");
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
                    WebhookToken = line.Substring("webhook_url=".Length).Trim();
                    Sawmill.Debug("Discord webhook URL loaded from config file");
                    return;
                }
            }
            
            Sawmill.Warning($"No webhook_url found in {WebhookConfigPath}");
        }
        catch (Exception ex)
        {
            Sawmill.Error($"Error loading Discord webhook configuration: {ex}");
        }
    }
} 
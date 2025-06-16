using System;
using System.IO;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Configuration;
using Content.Server.Discord;
using Content.Shared.CCVar;

namespace Content.Server.Discord.WebhookMessages;

/// <summary>
/// Базовый класс для сервисов отправки сообщений через Discord вебхуки.
/// Реализует общую логику и обработки логов.
/// </summary>
/// <remarks>
/// Для настройки Discord вебхука необходимо добавить URL в server_config.toml:
/// 
/// [discord]
/// webhook_url = "https://discord.com/api/webhooks/YOUR_WEBHOOK_ID/YOUR_WEBHOOK_TOKEN"
/// webhook_enabled = true
/// webhook_log_level = 1
/// 
/// Или использовать команды в консоли сервера:
/// setcvar discord.webhook_url "https://discord.com/api/webhooks/YOUR_WEBHOOK_ID/YOUR_WEBHOOK_TOKEN"
/// setcvar discord.webhook_enabled true
/// setcvar discord.webhook_log_level 1
/// </remarks>
public abstract class BaseWebhookService : IPostInjectInit
{
    [Dependency] protected readonly DiscordWebhook Webhook = default!;
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly IConfigurationManager _configManager = default!;
    
    protected ISawmill Sawmill = default!;
    
    /// <summary>
    /// Имя для логгера (sawmill)
    /// </summary>
    protected abstract string SawmillName { get; }
    
    /// <summary>
    /// Получает текущий уровень логирования из CVar
    /// </summary>
    protected int LogLevel => _configManager.GetCVar(CCVars.DiscordWebhookLogLevel);
    
    /// <summary>
    /// Проверяет, включены ли вебхуки глобально
    /// </summary>
    protected bool WebhooksEnabled => _configManager.GetCVar(CCVars.DiscordWebhookEnabled);
    
    public virtual void PostInject()
    {
        Sawmill = _logManager.GetSawmill(SawmillName);
        
        // Проверяем глобальное состояние вебхуков
        if (!WebhooksEnabled)
        {
            LogInfo("Discord webhooks are globally disabled");
            return;
        }
        
        LogInfo("Discord webhook service initialized");
        
        // Подписки на события должны быть реализованы в производных классах
    }
    
    /// <summary>
    /// Логирование с учетом настроенного уровня
    /// </summary>
    protected void LogError(string message)
    {
        // Ошибки логируем всегда, независимо от уровня
        Sawmill.Error(message);
    }
    
    /// <summary>
    /// Логирование с учетом настроенного уровня
    /// </summary>
    protected void LogWarning(string message)
    {
        // Предупреждения логируем всегда, независимо от уровня
        Sawmill.Warning(message);
    }
    
    /// <summary>
    /// Логирование с учетом настроенного уровня
    /// </summary>
    protected void LogInfo(string message)
    {
        // Информационные сообщения логируем только если уровень >= 1
        if (LogLevel >= 1)
            Sawmill.Info(message);
    }
    
    /// <summary>
    /// Логирование с учетом настроенного уровня
    /// </summary>
    protected void LogDebug(string message)
    {
        // Отладочные сообщения логируем только если уровень >= 2
        if (LogLevel >= 2)
            Sawmill.Debug(message);
    }
} 
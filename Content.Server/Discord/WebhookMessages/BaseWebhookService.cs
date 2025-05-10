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
/// Реализует общую логику загрузки настроек и обработки токенов.
/// </summary>
public abstract class BaseWebhookService : IPostInjectInit
{
    [Dependency] protected readonly DiscordWebhook Webhook = default!;
    [Dependency] protected readonly ILogManager LogManager = default!;
    [Dependency] protected readonly IConfigurationManager ConfigManager = default!;
    
    protected ISawmill Sawmill = default!;
    protected bool Enabled => ConfigManager.GetCVar(CCVars.Discord.WebhookEnabled);
    
    /// <summary>
    /// Имя для логгера (sawmill)
    /// </summary>
    protected abstract string SawmillName { get; }
    
    /// <summary>
    /// Получает токен вебхука (URL) для отправки сообщений. Должен быть реализован в наследниках.
    /// </summary>
    protected abstract string WebhookToken { get; }

    public virtual void PostInject()
    {
        Sawmill = LogManager.GetSawmill(SawmillName);
        
        if (!Enabled)
        {
            Log(LogLevel.Warning, "Webhook service is disabled");
            return;
        }
        
        if (string.IsNullOrEmpty(WebhookToken))
        {
            Log(LogLevel.Warning, "Webhook token is not set");
            return;
        }
        
        Log(LogLevel.Info, "Webhook service initialized");
    }
    
    /// <summary>
    /// Логирование с указанным уровнем
    /// </summary>
    protected void Log(LogLevel level, string message)
    {
        switch (level)
        {
            case LogLevel.Error:
                Sawmill.Error(message);
                break;
            case LogLevel.Warning:
                Sawmill.Warning(message);
                break;
            case LogLevel.Info:
                Sawmill.Info(message);
                break;
            case LogLevel.Debug:
                Sawmill.Debug(message);
                break;
        }
    }
} 
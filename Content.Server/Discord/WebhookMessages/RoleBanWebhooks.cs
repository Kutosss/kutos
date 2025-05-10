using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Shared.Database;
using Robust.Server.Player;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Content.Server.Configuration;

namespace Content.Server.Discord.WebhookMessages;

/// <summary>
/// Отправляет уведомления о ролевых банах через Discord вебхуки.
/// Использует тот же файл конфигурации, что и BanWebhooks.
/// </summary>
public sealed class RoleBanWebhooks : BaseWebhookService
{
    [Dependency] private readonly IBanManager _banManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IServerDbManager _dbManager = default!;
    [Dependency] private readonly IConfigManager _configManager = default!;

    /// <inheritdoc/>
    protected override string SawmillName => "discord.roleban_webhooks";
    
    /// <inheritdoc/>
    protected override string WebhookToken => _configManager.GetCVar(Content.Shared.CCVar.CCVars.DiscordBanWebhookUrl);

    /// <inheritdoc/>
    public override void PostInject()
    {
        base.PostInject();

        // Логируем, что сервис инициализируется
        LogInfo("Initializing RoleBanWebhooks service");

        // Подписываемся на событие ролевых банов
        _banManager.RoleBanAdded += OnRoleBanAdded;
        
        // Логируем, что подписка на событие выполнена
        LogInfo("Subscribed to RoleBanAdded event");
    }

    /// <summary>
    /// Обработчик события добавления нового ролевого бана.
    /// </summary>
    public void OnRoleBanAdded(string targetName, string role, string adminName, string reason, DateTimeOffset banTime, DateTimeOffset? expirationTime)
    {
        // Логируем вызов обработчика события
        LogDebug($"OnRoleBanAdded called: target={targetName}, role={role}, admin={adminName}");
        
        if (!Enabled || string.IsNullOrEmpty(WebhookToken))
        {
            LogWarning($"Webhook not enabled or token is empty. Enabled={Enabled}, TokenEmpty={string.IsNullOrEmpty(WebhookToken)}");
            return;
        }
        
        // Используем Task.Run для асинхронной обработки без блокировки потока
        Task.Run(async () => {
            try
            {
                LogInfo($"Sending role ban webhook for user: {targetName}, role: {role}");
                await SendRoleBanWebhook(targetName, role, adminName, reason, banTime, expirationTime);
                LogInfo($"Role ban webhook sent for user: {targetName}, role: {role}");
            }
            catch (Exception e)
            {
                LogError($"Error sending role ban webhook: {e}");
            }
        });
    }
    
    private async Task SendRoleBanWebhook(string targetName, string role, string adminName, string reason, 
        DateTimeOffset banTime, DateTimeOffset? expirationTime)
    {
        // Больше не получаем ID бана из базы данных
        
        var embed = new WebhookEmbed
        {
            Title = "Джоббан",
            Description = FormatRoleBanInfo(targetName, role, adminName, reason, banTime, expirationTime),
            Color = 0xFFA500 // Оранжевый цвет для ролевых банов
        };

        var payload = new WebhookPayload
        {
            Username = "Вахтер",
            Embeds = new List<WebhookEmbed> { embed }
        };

        await Webhook.CreateMessageWithToken(WebhookToken, payload);
    }
    
    private string FormatRoleBanInfo(string targetName, string role, string adminName, string reason, 
        DateTimeOffset banTime, DateTimeOffset? expirationTime)
    {
        var sb = new StringBuilder();
        
        // Удаляем строку с ID бана
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
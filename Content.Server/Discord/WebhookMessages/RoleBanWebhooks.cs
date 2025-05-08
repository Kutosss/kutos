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
public sealed class RoleBanWebhooks : BaseWebhookService
{
    [Dependency] private readonly IBanManager _banManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    /// <inheritdoc/>
    protected override string SawmillName => "discord.roleban_webhooks";

    /// <inheritdoc/>
    public override void PostInject()
    {
        base.PostInject();

        // Подписываемся на событие ролевых банов
        _banManager.RoleBanAdded += OnRoleBanAdded;
    }

    /// <summary>
    /// Обработчик события добавления нового ролевого бана.
    /// </summary>
    public void OnRoleBanAdded(string targetName, string role, string adminName, string reason, DateTimeOffset banTime, DateTimeOffset? expirationTime)
    {
        if (!Enabled || string.IsNullOrEmpty(WebhookToken))
            return;
        
        // Используем Task.Run для асинхронной обработки без блокировки потока
        Task.Run(async () => {
            try
            {
                await SendRoleBanWebhook(targetName, role, adminName, reason, banTime, expirationTime);
                Sawmill.Info($"Role ban webhook sent for user: {targetName}, role: {role}");
            }
            catch (Exception e)
            {
                Sawmill.Error($"Error sending role ban webhook: {e}");
            }
        });
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

        await Webhook.CreateMessageWithToken(WebhookToken, payload);
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
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Shared.Database;
using Content.Shared.CCVar;
using Robust.Server.Player;
using Robust.Shared.IoC;
using Robust.Shared.Configuration;
using Robust.Shared.Log;

namespace Content.Server.Discord.WebhookMessages;

/// <summary>
/// Отправляет уведомления о банах на сервере через Discord вебхуки.
/// </summary>
public sealed class BanWebhooks : BaseWebhookService
{
    [Dependency] private readonly IBanManager _banManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IServerDbManager _dbManager = default!;
    [Dependency] private readonly IConfigurationManager _configManager = default!;

    /// <inheritdoc/>
    protected override string SawmillName => "discord.ban_webhooks";

    /// <inheritdoc/>
    protected override string WebhookToken => _configManager.GetCVar(CCVars.Discord.BanWebhookUrl);

    /// <inheritdoc/>
    public override void PostInject()
    {
        base.PostInject();
        _banManager.BanAdded += OnBanAdded;
        Log(LogLevel.Info, "Subscribed to BanAdded event");
    }

    /// <summary>
    /// Обработчик события добавления нового бана.
    /// </summary>
    private void OnBanAdded(ServerBanDef ban)
    {
        if (!Enabled || string.IsNullOrEmpty(WebhookToken))
        {
            Log(LogLevel.Warning, "Webhook not enabled or token is empty");
            return;
        }
        
        Task.Run(async () => {
            try
            {
                await SendBanWebhook(ban);
                Log(LogLevel.Info, $"Ban webhook sent for user: {ban.UserId}");
            }
            catch (Exception e)
            {
                Log(LogLevel.Error, $"Error sending ban webhook: {e}");
            }
        });
    }
    
    /// <summary>
    /// Отправляет уведомление о бане через Discord вебхук.
    /// </summary>
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

        await Webhook.CreateMessageWithToken(WebhookToken, payload);
    }

    /// <summary>
    /// Форматирует информацию о бане для отображения в Discord.
    /// </summary>
    private async Task<string> FormatBanInfo(ServerBanDef ban)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine($"**ID бана:** {ban.Id ?? 0}");
        
        string playerName = "Неизвестно";
        string adminName = "Система";
        
        if (ban.UserId != null)
        {
            if (_playerManager.TryGetSessionById(ban.UserId.Value, out var player))
            {
                playerName = player.Name;
            }
            else
            {
                var dbRecord = await _dbManager.GetPlayerRecordByUserId(ban.UserId.Value);
                if (dbRecord?.LastSeenUserName != null)
                {
                    playerName = dbRecord.LastSeenUserName;
                }
            }
        }
        
        if (ban.BanningAdmin != null)
        {
            if (_playerManager.TryGetSessionById(ban.BanningAdmin.Value, out var admin))
            {
                adminName = admin.Name;
            }
            else
            {
                var dbRecord = await _dbManager.GetPlayerRecordByUserId(ban.BanningAdmin.Value);
                if (dbRecord?.LastSeenUserName != null)
                {
                    adminName = dbRecord.LastSeenUserName;
                }
            }
        }
        
        sb.AppendLine($"**Игрок:** {playerName}");
        sb.AppendLine($"**Администратор:** {adminName}");
        sb.AppendLine($"**Причина:** {ban.Reason}");
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
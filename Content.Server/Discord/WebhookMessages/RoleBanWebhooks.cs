using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Shared.Database;
using Content.Shared.CCVar;
using Robust.Server.Player;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Configuration;

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
    [Dependency] private readonly IConfigurationManager _configManager = default!;

    private readonly Dictionary<string, List<(string role, string adminName, string reason, DateTimeOffset banTime, DateTimeOffset? expirationTime)>> _pendingBans = new();
    private readonly object _pendingBansLock = new();
    private const int MaxDelayMs = 2000; // Максимальная задержка перед отправкой
    private const int MinDelayMs = 500; // Минимальная задержка перед отправкой

    /// <inheritdoc/>
    protected override string SawmillName => "discord.roleban_webhooks";
    
    /// <inheritdoc/>
    protected override string WebhookToken => _configManager.GetCVar(CCVars.Discord.BanWebhookUrl);

    /// <inheritdoc/>
    public override void PostInject()
    {
        base.PostInject();
        _banManager.RoleBanAdded += OnRoleBanAdded;
        Log(LogLevel.Info, "Subscribed to RoleBanAdded event");
    }

    /// <summary>
    /// Обработчик события добавления нового ролевого бана.
    /// </summary>
    public void OnRoleBanAdded(string targetName, string role, string adminName, string reason, DateTimeOffset banTime, DateTimeOffset? expirationTime)
    {
        if (!Enabled || string.IsNullOrEmpty(WebhookToken))
        {
            Log(LogLevel.Warning, "Webhook not enabled or token is empty");
            return;
        }

        lock (_pendingBansLock)
        {
            if (!_pendingBans.ContainsKey(targetName))
            {
                _pendingBans[targetName] = new List<(string, string, string, DateTimeOffset, DateTimeOffset?)>();
                Task.Delay(MinDelayMs).ContinueWith(_ => ProcessPendingBans(targetName));
            }
            
            _pendingBans[targetName].Add((role, adminName, reason, banTime, expirationTime));
        }
    }

    private async void ProcessPendingBans(string targetName)
    {
        await Task.Delay(MaxDelayMs - MinDelayMs);

        List<(string role, string adminName, string reason, DateTimeOffset banTime, DateTimeOffset? expirationTime)>? bans;
        lock (_pendingBansLock)
        {
            if (!_pendingBans.TryGetValue(targetName, out bans) || bans == null)
                return;
            
            _pendingBans.Remove(targetName);
        }

        try
        {
            var roles = new List<string>();
            foreach (var ban in bans)
            {
                var roleName = ban.role;
                if (roleName.StartsWith("Job:", StringComparison.Ordinal))
                {
                    roleName = roleName[4..];
                }
                
                // Получаем последний бан для этой роли из базы данных
                var recentBans = await _dbManager.GetRecentRoleBansAsync(roleName, targetName, ban.banTime);
                var banId = recentBans.FirstOrDefault()?.Id;
                
                roles.Add(banId.HasValue ? $"{roleName} (#{banId})" : roleName);
            }
            
            var firstBan = bans.First();
            await SendRoleBanWebhook(targetName, roles, firstBan.adminName, firstBan.reason, firstBan.banTime, firstBan.expirationTime);
            Log(LogLevel.Info, $"Role ban webhook sent for user: {targetName}, roles: {string.Join(", ", roles)}");
        }
        catch (Exception e)
        {
            Log(LogLevel.Error, $"Error sending role ban webhook: {e}");
        }
    }
    
    public async Task SendRoleBanWebhook(string targetName, List<string> roles, string adminName, string reason, 
        DateTimeOffset banTime, DateTimeOffset? expirationTime)
    {
        var embed = new WebhookEmbed
        {
            Title = "Джоббан",
            Description = FormatRoleBanInfo(targetName, roles, adminName, reason, banTime, expirationTime),
            Color = 0xFFA500 // Оранжевый цвет для ролевых банов
        };

        var payload = new WebhookPayload
        {
            Username = "Вахтер",
            Embeds = new List<WebhookEmbed> { embed }
        };

        await Webhook.CreateMessageWithToken(WebhookToken, payload);
    }
    
    private string FormatRoleBanInfo(string targetName, List<string> roles, string adminName, string reason, 
        DateTimeOffset banTime, DateTimeOffset? expirationTime)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine($"**Игрок:** {targetName}");
        sb.AppendLine($"**Роли:** {string.Join(", ", roles)}");
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
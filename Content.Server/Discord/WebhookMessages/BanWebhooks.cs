using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Shared.Database;
using Robust.Server.Player;
using Robust.Shared.IoC;

namespace Content.Server.Discord.WebhookMessages;

/// <summary>
/// Отправляет уведомления о банах на сервере через Discord вебхуки.
/// </summary>
public sealed class BanWebhooks : BaseWebhookService
{
    [Dependency] private readonly IBanManager _banManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IServerDbManager _dbManager = default!;

    /// <inheritdoc/>
    protected override string SawmillName => "discord.ban_webhooks";

    /// <inheritdoc/>
    public override void PostInject()
    {
        base.PostInject();
        
        // Подписываемся на события банов
        _banManager.BanAdded += OnBanAdded;
    }

    /// <summary>
    /// Обработчик события добавления нового бана.
    /// </summary>
    /// <param name="ban">Информация о бане</param>
    private void OnBanAdded(ServerBanDef ban)
    {
        if (!Enabled || string.IsNullOrEmpty(WebhookToken))
            return;
        
        // Используем Task.Run для асинхронной обработки без блокировки потока
        Task.Run(async () => {
            try
            {
                await SendBanWebhook(ban);
                Sawmill.Info($"Ban webhook sent for user: {ban.UserId}");
            }
            catch (Exception e)
            {
                Sawmill.Error($"Error sending ban webhook: {e}");
            }
        });
    }
    
    /// <summary>
    /// Отправляет уведомление о бане через Discord вебхук.
    /// </summary>
    /// <param name="ban">Информация о бане</param>
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
    /// <param name="ban">Информация о бане</param>
    /// <returns>Отформатированная строка с информацией о бане</returns>
    private async Task<string> FormatBanInfo(ServerBanDef ban)
    {
        var sb = new StringBuilder();
        
        // Пытаемся получить имя игрока и администратора
        string playerName = "Неизвестно";
        string adminName = "Система";
        
        // Создаем список задач для параллельного выполнения запросов к БД
        var dbTasks = new List<Task>();
        Task playerTask = Task.CompletedTask;
        Task adminTask = Task.CompletedTask;
        
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
                playerTask = Task.Run(async () => {
                    try 
                    {
                        var dbRecord = await _dbManager.GetPlayerRecordByUserId(ban.UserId.Value);
                        if (dbRecord != null && !string.IsNullOrEmpty(dbRecord.LastSeenUserName))
                        {
                            playerName = dbRecord.LastSeenUserName;
                        }
                    }
                    catch (Exception ex)
                    {
                        Sawmill.Error($"Ошибка при получении имени игрока из базы данных: {ex}");
                    }
                });
                dbTasks.Add(playerTask);
            }
        }
        
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
                adminTask = Task.Run(async () => {
                    try 
                    {
                        var dbRecord = await _dbManager.GetPlayerRecordByUserId(ban.BanningAdmin.Value);
                        if (dbRecord != null && !string.IsNullOrEmpty(dbRecord.LastSeenUserName))
                        {
                            adminName = dbRecord.LastSeenUserName;
                        }
                    }
                    catch (Exception ex)
                    {
                        Sawmill.Error($"Ошибка при получении имени администратора из базы данных: {ex}");
                    }
                });
                dbTasks.Add(adminTask);
            }
        }
        
        // Ожидаем завершения всех запросов к БД
        if (dbTasks.Count > 0)
        {
            await Task.WhenAll(dbTasks);
        }
        
        // Формируем сообщение для Discord
        sb.AppendLine($"**Игрок:** {playerName}");
        
        // IP-адрес и HWID не отображаются по запросу
        
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
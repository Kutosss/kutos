using Content.Server.Discord.WebhookMessages;
using Robust.Shared.IoC;

namespace Content.Server.Discord;

/// <summary>
/// Класс для регистрации всех Discord вебхук сервисов в IoC контейнере
/// </summary>
public static class DiscordWebhookRegistration
{
    /// <summary>
    /// Регистрирует все Discord веб-хук сервисы
    /// </summary>
    /// <param name="factory">IoC контейнер</param>
    public static void Register(IDependencyCollection collection)
    {
        // Основной сервис для отправки запросов в Discord
        collection.Register<DiscordWebhook>();
        
        // Сервисы для отправки уведомлений
        collection.Register<BanWebhooks>();
        collection.Register<RoleBanWebhooks>();
        
        // Здесь можно добавить другие сервисы для уведомлений
    }
} 
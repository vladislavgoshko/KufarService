using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace TelegramService.Services
{
    /// <summary>
    /// Сервис для управления уведомлениями об объявлениях.
    /// Периодически проверяет новые объявления и отправляет их пользователю.
    /// </summary>
    internal class AdNotificationService
    {
        private readonly ITelegramBotClient _botClient;
        private readonly HttpClient _httpClient;
        private readonly ConcurrentDictionary<string, ActiveNotification> _activeNotifications;
        private string CoreApiUrl = "http://localhost:5001";
        private const int CheckIntervalSeconds = 5;

        private class ActiveNotification
        {
            public string TrackingId { get; set; }
            public long ChatId { get; set; }
            public CancellationTokenSource CancellationTokenSource { get; set; }
            public Task NotificationTask { get; set; }
            public DateTime StartedAt { get; set; }
        }

        public class NewAdvertisementResponse
        {
            public bool Success { get; set; }
            public int Count { get; set; }
            public List<AdvertisementData> Advertisements { get; set; }
        }

        public class AdvertisementData
        {
            [System.Text.Json.Serialization.JsonPropertyName("ad_id")]
            public long Id { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("subject")]
            public string Title { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("body")]
            public string Body { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("body_short")]
            public string BodyShort { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("price")]
            public decimal? Price { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("currency")]
            public string Currency { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("region")]
            public string Region { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("list_time")]
            public DateTime CreatedAt { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("ad_link")]
            public string AdLink { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("imageUrl")]
            public string ImageUrl { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("images")]
            public List<ImageData> Images { get; set; }
        }

        public class ImageData
        {
            [System.Text.Json.Serialization.JsonPropertyName("id")]
            public string Id { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("url")]
            public string Url { get; set; }
        }

        public AdNotificationService(ITelegramBotClient botClient, ApiSettings apiSettings)
        {
            _botClient = botClient;
            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
            _httpClient = new HttpClient(handler);
            _activeNotifications = new ConcurrentDictionary<string, ActiveNotification>();
            CoreApiUrl = apiSettings.BaseUrl;
        }

        /// <summary>
        /// Начинает отправку уведомлений об объявлениях пользователю.
        /// </summary>
        public async Task StartNotificationsAsync(string trackingId, long chatId)
        {
            // Если уже отправляем уведомления, остановим старые
            if (_activeNotifications.ContainsKey(trackingId))
            {
                await StopNotificationsAsync(trackingId);
            }

            var cts = new CancellationTokenSource();
            var notification = new ActiveNotification
            {
                TrackingId = trackingId,
                ChatId = chatId,
                CancellationTokenSource = cts,
                StartedAt = DateTime.Now.AddMinutes(-5) // TODO убрать -5 минут
            };

            // Создаем и запускаем фоновую задачу
            notification.NotificationTask = NotificationLoopAsync(notification, cts.Token);

            _activeNotifications.TryAdd(trackingId, notification);

            Console.WriteLine($"[AdNotificationService] Уведомления начаты. TrackingId: {trackingId}, ChatId: {chatId}");
        }

        /// <summary>
        /// Останавливает отправку уведомлений.
        /// </summary>
        public async Task StopNotificationsAsync(string trackingId)
        {
            if (_activeNotifications.TryRemove(trackingId, out var notification))
            {
                notification.CancellationTokenSource.Cancel();
                try
                {
                    await notification.NotificationTask;
                }
                catch (OperationCanceledException)
                {
                    // Ожидаемое исключение при отмене
                }

                Console.WriteLine($"[AdNotificationService] Уведомления остановлены. TrackingId: {trackingId}");
            }
        }

        /// <summary>
        /// Основной цикл проверки и отправки уведомлений.
        /// </summary>
        private async Task NotificationLoopAsync(ActiveNotification notification, CancellationToken ct)
        {
            var delayMs = CheckIntervalSeconds * 1000;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        // Получаем новые объявления из CoreAPI
                        var newAds = await GetNewAdvertisementsAsync(notification.TrackingId, ct);

                        // Если есть новые объявления, отправляем их
                        if (newAds.Count > 0)
                        {
                            Console.WriteLine(
                                $"[AdNotificationService] Найдено новых объявлений: {newAds.Count} " +
                                $"для ChatId: {notification.ChatId}");

                            foreach (var ad in newAds)
                            {
                                try
                                {
                                    await SendAdvertisementAsync(notification.ChatId, ad, ct);
                                    // Небольшая задержка между отправками
                                    await Task.Delay(500, ct);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"[AdNotificationService] Ошибка при отправке объявления: {ex.Message}");
                                }
                            }
                        }

                        // Ожидаем перед следующей проверкой
                        await Task.Delay(delayMs, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[AdNotificationService] Ошибка в цикле уведомлений: {ex.Message}");
                        await Task.Delay(delayMs, ct);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Нормальное завершение при отмене
            }
        }

        /// <summary>
        /// Получает новые объявления из CoreAPI.
        /// </summary>
        private async Task<List<AdvertisementData>> GetNewAdvertisementsAsync(string trackingId, CancellationToken ct)
        {
            try
            {
                var url = $"{CoreApiUrl}/api/adtracking/new-ads/{trackingId}";
                var response = await _httpClient.GetAsync(url, ct);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(ct);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var result = JsonSerializer.Deserialize<NewAdvertisementResponse>(content, options);

                    return result?.Advertisements ?? new List<AdvertisementData>();
                }
                else
                {
                    Console.WriteLine($"[AdNotificationService] Ошибка при получении объявлений. Статус: {response.StatusCode}");
                    return new List<AdvertisementData>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AdNotificationService] Исключение при получении объявлений: {ex.Message}");
                return new List<AdvertisementData>();
            }
        }

        /// <summary>
        /// Отправляет объявление пользователю в Telegram.
        /// </summary>
        private async Task SendAdvertisementAsync(long chatId, AdvertisementData ad, CancellationToken ct)
        {
            try
            {
                var price = ad.Price.HasValue ? $"{ad.Price} {ad.Currency}" : "По договоренности";
                var message = $"✨ <b>Новое объявление!</b>\n\n" +
                             $"<b>{ad.Title}</b>\n" +
                             $"💰 Цена: {price}\n" +
                             $"📍 Место: {ad.Region}\n" +
                             $"⏰ Дата: {ad.CreatedAt:dd.MM.yyyy HH:mm}\n\n" +
                             $"<a href=\"{ad.AdLink}\">Открыть объявление →</a>";

                await _botClient.SendMessage(chatId, message, parseMode: ParseMode.Html, cancellationToken: ct);

                Console.WriteLine($"[AdNotificationService] Объявление отправлено. ChatId: {chatId}, AdId: {ad.Id}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AdNotificationService] Ошибка при отправке сообщения: {ex.Message}");
            }
        }
    }
}

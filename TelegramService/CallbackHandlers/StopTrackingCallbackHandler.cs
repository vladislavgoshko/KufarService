using System;
using System.Text;
using System.Text.RegularExpressions;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramService.Services;

namespace TelegramService.CallbackHandlers
{
    internal class StopTrackingCallbackHandler : CallbackQueryHandlerBase
    {
        private readonly AdNotificationService _adNotificationService;
        private readonly HttpClient _httpClient;
        private Regex _stopTrackingRegex = new(@"^stop_tracking_(?<id>.+)$", RegexOptions.Compiled);
        private string CoreApiUrl = "http://localhost:5001";

        public StopTrackingCallbackHandler(AdNotificationService adNotificationService, ApiSettings apiSettings)
        {
            this._adNotificationService = adNotificationService;
            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
            _httpClient = new HttpClient(handler);
            CoreApiUrl = apiSettings.BaseUrl;
        }

        protected override bool CanHandle(CallbackQuery callbackQuery) => _stopTrackingRegex.IsMatch(callbackQuery!.Data!);

        protected override async Task ExecuteAsync(CallbackQuery callback, ITelegramBotClient botClient, CancellationToken ct)
        {
            var match = _stopTrackingRegex.Match(callback.Data!);
            string trackingId = match.Groups["id"].Value;

            await botClient.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

            try
            {
                // Останавливаем уведомления локально
                await _adNotificationService.StopNotificationsAsync(trackingId);

                // Останавливаем отслеживание через CoreAPI
                var stopRequest = new { trackingId = trackingId };
                var json = System.Text.Json.JsonSerializer.Serialize(stopRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{CoreApiUrl}/api/adtracking/stop", content, ct);

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[StopTrackingCallbackHandler] Отслеживание остановлено. TrackingId: {trackingId}");

                    await botClient.EditMessageText(
                        chatId: callback.Message!.Chat.Id,
                        messageId: callback.Message!.Id,
                        text: "⏹️ Отслеживание остановлено.",
                        cancellationToken: ct
                    );
                }
                else
                {
                    Console.WriteLine($"[StopTrackingCallbackHandler] Ошибка при остановке отслеживания. Статус: {response.StatusCode}");
                    await botClient.SendMessage(callback.Message!.Chat.Id, "❌ Ошибка при остановке отслеживания.", cancellationToken: ct);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StopTrackingCallbackHandler] Исключение: {ex.Message}");
                await botClient.SendMessage(callback.Message!.Chat.Id, $"❌ Ошибка: {ex.Message}", cancellationToken: ct);
            }
        }
    }
}

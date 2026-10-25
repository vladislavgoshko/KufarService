using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramService.Services;

namespace TelegramService.CallbackHandlers
{
    internal class StartActualTrackingCallbackHandler : CallbackQueryHandlerBase
    {
        private readonly UserSessionService _sessionService;
        private readonly AdNotificationService _adNotificationService;
        private readonly HttpClient _httpClient;
        private string CoreApiUrl;

        public StartActualTrackingCallbackHandler(
            UserSessionService sessionService,
            AdNotificationService adNotificationService,
            ApiSettings apiSettings)
        {
            _sessionService = sessionService;
            _adNotificationService = adNotificationService;

            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
            _httpClient = new HttpClient(handler);
            CoreApiUrl = apiSettings.BaseUrl;
        }

        protected override bool CanHandle(CallbackQuery callbackQuery) => callbackQuery!.Data == "start_actual_tracking";

        public override async Task ExecuteAsync(CallbackQuery callback, ITelegramBotClient botClient, CancellationToken ct)
        {
            long chatId = callback.Message!.Chat.Id;
            var session = _sessionService.GetOrCreateSession(chatId);

            if (!session.SubcategoryId.HasValue || !session.CityId.HasValue)
            {
                await botClient.AnswerCallbackQuery(callback.Id, "❌ Ошибка: выберите категорию и город!", showAlert: true, cancellationToken: ct);
                return;
            }

            await botClient.AnswerCallbackQuery(callback.Id, "Запуск процесса отслеживания...", cancellationToken: ct);

            int subcategoryId = session.SubcategoryId.Value;
            int categoryId = session.CategoryId ?? (subcategoryId / 1000 * 1000);
            string trackingId = $"{chatId}_{subcategoryId}";

            try
            {
                // Формируем фильтр по городу
                string filterValue = $"ar={session.CityId.Value}";

                var startRequest = new
                {
                    trackingId = trackingId,
                    categoryId = categoryId,
                    subcategoryId = subcategoryId,
                    filter = filterValue // Передаем сформированную строку "ar=7"
                };

                var json = System.Text.Json.JsonSerializer.Serialize(startRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{CoreApiUrl}/api/adtracking/start", content, ct);

                if (response.IsSuccessStatusCode)
                {
                    await _adNotificationService.StartNotificationsAsync(trackingId, chatId);
                    _sessionService.ClearSession(chatId);

                    var resultMarkup = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("⏹️ Остановить отслеживание", $"stop_tracking_{trackingId}") },
                        new[] { InlineKeyboardButton.WithCallbackData("Назад в главное меню", "back_to_menu") }
                    });

                    var message = callback.Message!;

                    await botClient.EditMessageText(
                        chatId: message.Chat.Id,
                        messageId: message.Id,
                        text: $"🚀 *Отслеживание объявлений успешно запущено!*\n\n" +
                              $"📁 *Категория:* {subcategoryId}\n" +
                              $"📍 *Город:* {session.City}\n\n" +
                              $"Новые объявления будут отправляться автоматически каждые 5 секунд.",
                        parseMode: ParseMode.Markdown,
                        cancellationToken: ct,
                        replyMarkup: resultMarkup
                    );
                }
                else
                {
                    await botClient.SendMessage(chatId, "❌ Произошла ошибка на сервере при попытке запустить отслеживание.", cancellationToken: ct);
                }
            }
            catch (Exception ex)
            {
                await botClient.SendMessage(chatId, $"❌ Ошибка сети: {ex.Message}", cancellationToken: ct);
            }
        }
    }
}
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramService.Services;

namespace TelegramService.CallbackHandlers
{
    internal class SubcategoryCallbackHandler : CallbackQueryHandlerBase
    {
        private readonly CategoryService _categoryService;
        private readonly AdNotificationService _adNotificationService;
        private readonly HttpClient _httpClient;
        private Regex _subcategoryRegex = new(@"^subcategory(?<id>\d+$)", RegexOptions.Compiled);
        private string CoreApiUrl = "http://localhost:5001";

        public SubcategoryCallbackHandler(CategoryService categoryService, AdNotificationService adNotificationService, ApiSettings apiSettings)
        {
            this._categoryService = categoryService;
            this._adNotificationService = adNotificationService;
            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
            _httpClient = new HttpClient(handler);
            CoreApiUrl = apiSettings.BaseUrl;
        }

        protected override bool CanHandle(CallbackQuery callbackQuery) => _subcategoryRegex.IsMatch(callbackQuery!.Data!);

        protected override async Task ExecuteAsync(CallbackQuery callback, ITelegramBotClient botClient, CancellationToken ct)
        {
            await botClient.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

            var match = _subcategoryRegex.Match(callback.Data!);
            int subcategoryId = -1;
            if (match.Success)
            {
                string idString = match.Groups["id"].Value;
                int.TryParse(idString, out subcategoryId);
            }

            // Получаем ID категории (первые цифры перед последними 4 цифрами - ID подкатегории)
            int categoryId = subcategoryId / 1000 * 1000;
            long chatId = callback.Message!.Chat.Id;
            string trackingId = $"{chatId}_{subcategoryId}";

            try
            {
                // Начинаем отслеживание через CoreAPI
                var startRequest = new
                {
                    trackingId = trackingId,
                    categoryId = categoryId,
                    subcategoryId = subcategoryId
                };

                var json = System.Text.Json.JsonSerializer.Serialize(startRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{CoreApiUrl}/api/adtracking/start", content, ct);

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[SubcategoryCallbackHandler] Отслеживание начато для ChatId: {chatId}");

                    // Начинаем отправку уведомлений об объявлениях
                    await _adNotificationService.StartNotificationsAsync(trackingId, chatId);

                    IEnumerable<IEnumerable<InlineKeyboardButton>> buttons = new[] {
                        new InlineKeyboardButton[]
                        {
                            InlineKeyboardButton.WithCallbackData("⏹️ Остановить отслеживание", $"stop_tracking_{trackingId}"),
                            InlineKeyboardButton.WithCallbackData("Назад", $"category{categoryId}")
                        }
                    };

                    var _subcategoryMarkup = new InlineKeyboardMarkup(buttons);

                    var message = callback.Message!;

                    await botClient.EditMessageText(
                        chatId: message.Chat.Id,
                        messageId: message.Id,
                        text: $"🔍 Отслеживаю объявления из подкатегории {subcategoryId}\n" +
                              $"Новые объявления будут отправляться каждые 5 секунд.",
                        cancellationToken: ct,
                        replyMarkup: _subcategoryMarkup
                    );
                }
                else
                {
                    Console.WriteLine($"[SubcategoryCallbackHandler] Ошибка при начале отслеживания. Статус: {response.StatusCode}");
                    await botClient.SendMessage(chatId, "❌ Ошибка при начале отслеживания объявлений.", cancellationToken: ct);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SubcategoryCallbackHandler] Исключение: {ex.Message}");
                await botClient.SendMessage(chatId, $"❌ Ошибка: {ex.Message}", cancellationToken: ct);
            }
        }
    }
}

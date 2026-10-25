using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramService.Services;

namespace TelegramService.CallbackHandlers
{
    internal class CreateNewSearchCallbackHandler : CallbackQueryHandlerBase
    {
        private readonly UserSessionService _sessionService;

        public CreateNewSearchCallbackHandler(UserSessionService sessionService)
        {
            _sessionService = sessionService;
        }

        protected override bool CanHandle(CallbackQuery callbackQuery) => callbackQuery!.Data == "create_new_search";

        public override async Task ExecuteAsync(CallbackQuery callback, ITelegramBotClient botClient, CancellationToken ct)
        {
            await botClient.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

            long chatId = callback.Message!.Chat.Id;
            var session = _sessionService.GetOrCreateSession(chatId);

            // Текстовый статус настроек
            string categoryText = session.SubcategoryId.HasValue
                ? $"✅ Выбрана (ID: {session.SubcategoryId})"
                : "❌ Не выбрана";

            string cityText = !string.IsNullOrEmpty(session.City)
                ? $"✅ {session.City}"
                : "❌ Не выбран";

            string text = $"⚙️ *Настройка нового отслеживания*\n\n" +
                          $"📁 *Подкатегория:* {categoryText}\n" +
                          $"📍 *Город:* {cityText}\n\n" +
                          $"Выберите параметры для изменения:";

            var buttons = new List<InlineKeyboardButton[]>();

            buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("📁 Выбрать категорию", "choose_category") });
            buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("📍 Выбрать город", "choose_city") });

            // Показываем кнопку "Начать отслеживание" только когда все параметры выбраны
            if (session.SubcategoryId.HasValue && !string.IsNullOrEmpty(session.City))
            {
                buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("🚀 Начать отслеживание", "start_actual_tracking") });
            }

            buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("Назад в главное меню", "back_to_menu") });

            var menu = new InlineKeyboardMarkup(buttons);
            var message = callback.Message!;

            await botClient.EditMessageText(
                chatId: message.Chat.Id,
                messageId: message.Id,
                text: text,
                parseMode: ParseMode.Markdown,
                cancellationToken: ct,
                replyMarkup: menu
            );
        }
    }
}
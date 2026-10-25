using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramService.Helpers;

namespace TelegramService.CallbackHandlers
{
    internal class ChooseCityCallbackHandler : CallbackQueryHandlerBase
    {
        protected override bool CanHandle(CallbackQuery callbackQuery) => callbackQuery!.Data == "choose_city";

        public override async Task ExecuteAsync(CallbackQuery callback, ITelegramBotClient botClient, CancellationToken ct)
        {
            await botClient.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

            // Словарь городов с их официальными ID для фильтра "ar"
            var cities = new Dictionary<string, int>
            {
                { "Минск", 7 },
                { "Гомель", 5 },
                { "Могилев", 13 },
                { "Витебск", 18 },
                { "Гродно", 9 },
                { "Брест", 1 }
            };

            // Генерируем callback-data вида "city_7", "city_5" и т.д.
            var rawButtons = cities.Select(kvp =>
                (InlineKeyboardButton)(kvp.Key, $"city_{kvp.Value}", KeyboardButtonStyle.Primary)
            );

            var groupedButtons = KeyboardHelper.GroupInlineButtons(rawButtons);

            groupedButtons.Add(new List<InlineKeyboardButton>
            {
                ("Назад", "create_new_search", KeyboardButtonStyle.Danger)
            });

            var citiesMarkup = new InlineKeyboardMarkup(groupedButtons);
            var message = callback.Message!;

            await botClient.EditMessageText(
                chatId: message.Chat.Id,
                messageId: message.Id,
                text: "Выбери город для поиска объявлений:",
                cancellationToken: ct,
                replyMarkup: citiesMarkup
            );
        }
    }
}
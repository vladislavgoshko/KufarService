using System;
using System.Collections.Generic;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramService.BusinessLogic;

namespace TelegramService.CallbackHandlers
{
    internal class CreateNewSearchCallbackHandler : IBotHandler
    {
        private List<Category> _categories;

        public CreateNewSearchCallbackHandler()
        {
            _categories = new CategoryService().GetCategories(); // сделать потом инъекцию
        }

        public bool CanHandle(Update update) => update.Type == UpdateType.CallbackQuery && update.CallbackQuery!.Data == "create_new_search";


        public async Task ExecuteAsync(Update update, ITelegramBotClient botClient, CancellationToken ct)
        {
            var callback = update.CallbackQuery!;

            await botClient.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

            IEnumerable<IEnumerable<InlineKeyboardButton>> buttons = _categories.Select(category => new InlineKeyboardButton[]
            {
                (category.Title, "category" + category.Id.ToString(), KeyboardButtonStyle.Primary)
            });

            buttons = buttons.Append(new InlineKeyboardButton[]{
                ("Назад", "back_to_menu", KeyboardButtonStyle.Danger) }
            );

            var _categoriesMarkup = new InlineKeyboardMarkup(buttons);

            var message = callback.Message!;

            await botClient.EditMessageText(
                chatId: message.Chat.Id,
                messageId: message.Id,
                text: "Выбери категорию:",
                cancellationToken: ct,
                replyMarkup: _categoriesMarkup
                );
        }
    }
}

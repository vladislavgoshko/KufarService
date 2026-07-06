using System;
using System.Collections.Generic;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramService.BusinessLogic;
using TelegramService.Services;

namespace TelegramService.CallbackHandlers
{
    internal class CreateNewSearchCallbackHandler : CallbackQueryHandlerBase
    {
        private readonly CategoryService _categoryService;
        private List<Category> _categories;

        public CreateNewSearchCallbackHandler(CategoryService categoryService)
        {
            //_categories = new CategoryService(apiURLBase).GetCategories(); // #TODO сделать потом инъекцию
            _categoryService = categoryService;
            _categories = _categoryService.GetCategories();
        }

        protected override bool CanHandle(CallbackQuery callbackQuery) => callbackQuery!.Data == "create_new_search";


        protected override async Task ExecuteAsync(CallbackQuery callback, ITelegramBotClient botClient, CancellationToken ct)
        {

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

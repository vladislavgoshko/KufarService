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
    internal class CategoryCallbackHandler : CallbackQueryHandlerBase
    {
        private readonly CategoryService _categoryService;
        private Regex _categoryRegex = new(@"^category(?<id>\d+$)", RegexOptions.Compiled);

        public CategoryCallbackHandler(CategoryService categoryService)
        {
            this._categoryService = categoryService;
        }

        protected override bool CanHandle(CallbackQuery callbackQuery) =>  _categoryRegex.IsMatch(callbackQuery!.Data!);

        protected override async Task ExecuteAsync(CallbackQuery callback, ITelegramBotClient botClient, CancellationToken ct)
        {

            await botClient.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

            var match = _categoryRegex.Match(callback.Data!);
            int id = -1;
            if (match.Success)
            {
                string idString = match.Groups["id"].Value;

                int.TryParse(idString, out id);
            }

            var subcategories = _categoryService.GetSubcategories(id);
            IEnumerable<IEnumerable<InlineKeyboardButton>> buttons = subcategories.Select(subcategory => new InlineKeyboardButton[]
            {
                (subcategory.Title, $"subcategory{subcategory.Id}", KeyboardButtonStyle.Primary)
            });

            buttons = buttons.Append(new InlineKeyboardButton[]{
                ("Назад", "create_new_search", KeyboardButtonStyle.Danger) }
            );

            var _subcategoriesMarkup = new InlineKeyboardMarkup(buttons);

            var message = callback.Message!;

            await botClient.EditMessageText(
                chatId: message.Chat.Id,
                messageId: message.Id,
                text: "Выбери подкатегорию:",
                cancellationToken: ct,
                replyMarkup: _subcategoriesMarkup
                );
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramService.BusinessLogic;

namespace TelegramService.CallbackHandlers
{
    internal class SubcategoryCallbackHandler : IBotHandler
    {
        private readonly CategoryService _categoryService;
        private Regex _subcategoryRegex = new(@"^subcategory(?<id>\d+$)", RegexOptions.Compiled);

        public SubcategoryCallbackHandler(CategoryService categoryService)
        {
            this._categoryService = categoryService;
        }

        public bool CanHandle(Update update) => update.Type == UpdateType.CallbackQuery && _subcategoryRegex.IsMatch(update.CallbackQuery!.Data!);

        public async Task ExecuteAsync(Update update, ITelegramBotClient botClient, CancellationToken ct)
        {
            var callback = update.CallbackQuery!;

            await botClient.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

            var match = _subcategoryRegex.Match(callback.Data!);
            int id = -1;
            if (match.Success)
            {
                string idString = match.Groups["id"].Value;

                int.TryParse(idString, out id);
            }

            var subcategories = _categoryService.GetSubcategories(id);
            IEnumerable<IEnumerable<InlineKeyboardButton>> buttons = new[] {
                new InlineKeyboardButton[]
                {
                    ("Назад", $"category{_categoryService.GetCategoryIdBySubcategory(id)}", KeyboardButtonStyle.Danger)
                }
            };

            var _subcategoryMarkup = new InlineKeyboardMarkup(buttons);

            var message = callback.Message!;

            await botClient.EditMessageText(
                chatId: message.Chat.Id,
                messageId: message.Id,
                text: $"Подкатегория: {id}",
                cancellationToken: ct,
                replyMarkup: _subcategoryMarkup
                );
        }
    }
}

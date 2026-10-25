using System;
using System.Collections.Generic;
using System.Linq; // Добавлено для LINQ
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramService.Helpers; // Подключаем наш хелпер
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

        protected override bool CanHandle(CallbackQuery callbackQuery) => _categoryRegex.IsMatch(callbackQuery!.Data!);

        public override async Task ExecuteAsync(CallbackQuery callback, ITelegramBotClient botClient, CancellationToken ct)
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

            // 1. Создаем плоский перечисляемый список кнопок подкатегорий
            var rawButtons = subcategories.Select(subcategory =>
                (InlineKeyboardButton)(subcategory.Title, $"subcategory{subcategory.Id}", KeyboardButtonStyle.Primary)
            );

            // 2. Группируем их с помощью общего хелпера
            var groupedButtons = KeyboardHelper.GroupInlineButtons(rawButtons);

            // 3. Добавляем кнопку "Назад" отдельной строкой в самый конец
            groupedButtons.Add(new List<InlineKeyboardButton>
            {
                ("Назад", "create_new_search", KeyboardButtonStyle.Danger)
            });

            var subcategoriesMarkup = new InlineKeyboardMarkup(groupedButtons);

            var message = callback.Message!;

            await botClient.EditMessageText(
                chatId: message.Chat.Id,
                messageId: message.Id,
                text: "Выбери подкатегорию:",
                cancellationToken: ct,
                replyMarkup: subcategoriesMarkup
            );
        }
    }
}
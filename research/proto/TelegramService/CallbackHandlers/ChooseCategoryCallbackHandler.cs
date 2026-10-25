using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramService.BusinessLogic;
using TelegramService.Helpers;
using TelegramService.Services;

namespace TelegramService.CallbackHandlers
{
    internal class ChooseCategoryCallbackHandler : CallbackQueryHandlerBase
    {
        private readonly CategoryService _categoryService;
        private List<Category> _categories;

        public ChooseCategoryCallbackHandler(CategoryService categoryService)
        {
            _categoryService = categoryService;
            _categories = _categoryService.GetCategories();
        }

        protected override bool CanHandle(CallbackQuery callbackQuery) => callbackQuery!.Data == "choose_category";

        public override async Task ExecuteAsync(CallbackQuery callback, ITelegramBotClient botClient, CancellationToken ct)
        {
            await botClient.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

            var rawButtons = _categories.Select(category =>
                (InlineKeyboardButton)(category.Title, "category" + category.Id.ToString(), KeyboardButtonStyle.Primary)
            );

            var groupedButtons = KeyboardHelper.GroupInlineButtons(rawButtons);

            // Кнопка "Назад" теперь ведет обратно в меню настройки поиска (create_new_search)
            groupedButtons.Add(new List<InlineKeyboardButton>
            {
                ("Назад", "create_new_search", KeyboardButtonStyle.Danger)
            });

            var categoriesMarkup = new InlineKeyboardMarkup(groupedButtons);
            var message = callback.Message!;

            await botClient.EditMessageText(
                chatId: message.Chat.Id,
                messageId: message.Id,
                text: "Выбери категорию:",
                cancellationToken: ct,
                replyMarkup: categoriesMarkup
            );
        }
    }
}
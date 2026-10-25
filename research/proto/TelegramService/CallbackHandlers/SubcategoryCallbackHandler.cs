using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramService.Services;

namespace TelegramService.CallbackHandlers
{
    internal class SubcategoryCallbackHandler : CallbackQueryHandlerBase
    {
        private readonly UserSessionService _sessionService;
        private Regex _subcategoryRegex = new(@"^subcategory(?<id>\d+$)", RegexOptions.Compiled);

        public SubcategoryCallbackHandler(UserSessionService sessionService)
        {
            _sessionService = sessionService;
        }

        protected override bool CanHandle(CallbackQuery callbackQuery) => _subcategoryRegex.IsMatch(callbackQuery!.Data!);

        public override async Task ExecuteAsync(CallbackQuery callback, ITelegramBotClient botClient, CancellationToken ct)
        {
            await botClient.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

            var match = _subcategoryRegex.Match(callback.Data!);
            if (match.Success && int.TryParse(match.Groups["id"].Value, out int subcategoryId))
            {
                int categoryId = subcategoryId / 1000 * 1000;
                long chatId = callback.Message!.Chat.Id;

                // Сохраняем выбор в сессию
                var session = _sessionService.GetOrCreateSession(chatId);
                session.CategoryId = categoryId;
                session.SubcategoryId = subcategoryId;
            }

            // Перенаправляем пользователя обратно в меню создания отслеживания
            var settingsMenu = new CreateNewSearchCallbackHandler(_sessionService);
            await settingsMenu.ExecuteAsync(callback, botClient, ct);
        }
    }
}
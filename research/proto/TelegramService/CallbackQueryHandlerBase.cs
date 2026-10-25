using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace TelegramService
{
    public abstract class CallbackQueryHandlerBase : IBotHandler
    {
        // Реализация метода из интерфейса IBotHandler
        public bool CanHandle(Update update)
        {
            return update.Type == UpdateType.CallbackQuery
                   && update.CallbackQuery != null
                   && CanHandle(update.CallbackQuery);
        }

        // Реализация метода из интерфейса IBotHandler
        public async Task ExecuteAsync(Update update, ITelegramBotClient botClient, CancellationToken ct)
        {
            if (update.CallbackQuery != null)
            {
                await ExecuteAsync(update.CallbackQuery, botClient, ct);
            }
        }

        // Абстрактные методы, которые будут реализовывать конкретные обработчики
        protected abstract bool CanHandle(CallbackQuery callbackQuery);
        public abstract Task ExecuteAsync(CallbackQuery callbackQuery, ITelegramBotClient botClient, CancellationToken ct);
    }
}
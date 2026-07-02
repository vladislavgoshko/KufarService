using Telegram.Bot;
using Telegram.Bot.Types;

namespace TelegramService
{
    public class UpdateRouter
    {
        private readonly IEnumerable<IBotHandler> _handlers;

        public UpdateRouter(IEnumerable<IBotHandler> handlers)
        {
            _handlers = handlers;
        }

        public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken ct)
        {
            // Ищем первый подходящий обработчик
            var handler = _handlers.FirstOrDefault(h => h.CanHandle(update));
            if (handler != null)
            {
                await handler.ExecuteAsync(update, botClient, ct);
            }
        }
    }
}

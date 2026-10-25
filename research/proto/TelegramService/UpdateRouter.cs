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
            try
            {
                // Ищем первый подходящий обработчик
                var handler = _handlers.FirstOrDefault(h => CanHandleSafely(h, update));
                if (handler != null)
                {
                    await handler.ExecuteAsync(update, botClient, ct);
                }
                else
                {
                    Console.WriteLine($"Нет обработчика для Update Type: {update.Type}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при обработке Update: {ex.GetType().Name} - {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                // Не пробрасываем исключение дальше - бот продолжит работу
            }
        }

        /// <summary>
        /// Безопасная проверка, может ли обработчик обработать апдейт
        /// </summary>
        private bool CanHandleSafely(IBotHandler handler, Update update)
        {
            try
            {
                return handler.CanHandle(update);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка в CanHandle для {handler.GetType().Name}: {ex.Message}");
                return false;
            }
        }
    }
}

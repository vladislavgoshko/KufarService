using Telegram.Bot;
using Telegram.Bot.Types;

public interface IBotHandler
{
    // Проверяет, должен ли этот класс обрабатывать данный апдейт
    bool CanHandle(Update update);
    Task ExecuteAsync(Update update, ITelegramBotClient botClient, CancellationToken ct);
}
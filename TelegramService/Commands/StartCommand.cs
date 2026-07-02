using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
public class StartCommand : IBotHandler
{
    private readonly InlineKeyboardMarkup _menu = new(new[]
    {
        new InlineKeyboardButton[] { ("🔍 Создать отслеживание", "create_new_search", KeyboardButtonStyle.Success) },
        new InlineKeyboardButton[] { ("📋 Текущие заказы", "view_existing", KeyboardButtonStyle.Primary) },
        new[] { InlineKeyboardButton.WithCallbackData("ℹ️ О боте", "about"), ("Выход", "exit", KeyboardButtonStyle.Danger)}
    });

    public bool CanHandle(Update update) =>
        update.Message?.Text == "/start";

    public async Task ExecuteAsync(Update update, ITelegramBotClient botClient, CancellationToken ct)
    {
        await botClient.SendMessage(
            chatId: update.Message!.Chat.Id,
            text: "Добро пожаловать! Выберите действие:",
            replyMarkup: _menu,
            cancellationToken: ct);
    }
}

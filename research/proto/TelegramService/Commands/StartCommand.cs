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


    private readonly ReplyKeyboardMarkup _replyMenu = new(new[]
    {
        new[] { new KeyboardButton("🔍 Начать поиск"), new KeyboardButton("📋 Мои заказы") },
        new[] { new KeyboardButton("ℹ️ Помощь") }
    })
    {
        ResizeKeyboard = true // Делаем кнопки компактными
    };

    public bool CanHandle(Update update) =>
        update.Message?.Text == "/start";

    public async Task ExecuteAsync(Update update, ITelegramBotClient botClient, CancellationToken ct)
    {
        var chatId = update.Message!.Chat.Id;

        await botClient.SendMessage(
            chatId: chatId,
            text: "Клавиатура быстрого доступа под полем ввода активирована. 👇",
            replyMarkup: _replyMenu,
            cancellationToken: ct);

        await botClient.SendMessage(
            chatId: update.Message!.Chat.Id,
            text: "Добро пожаловать! Выберите действие:",
            replyMarkup: _menu,
            cancellationToken: ct);
    }
}

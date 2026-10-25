using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramService;

public class BackToMenuCallbackHandler : CallbackQueryHandlerBase
{
    private readonly InlineKeyboardMarkup _menu = new(new[]
    {
        new[] { InlineKeyboardButton.WithCallbackData("🔍 Создать отслеживание", "create_new_search") },
        new[] { InlineKeyboardButton.WithCallbackData("📋 Текущие заказы", "view_existing") },
        new[] { InlineKeyboardButton.WithCallbackData("ℹ️ О боте", "about"), InlineKeyboardButton.WithCallbackData("Выход", "exit") }
    });

    // Проверяем только то, что относится к логике этого хэндлера
    protected override bool CanHandle(CallbackQuery callbackQuery) =>
        callbackQuery.Data == "back_to_menu";

    // Работаем напрямую с CallbackQuery
    public override async Task ExecuteAsync(CallbackQuery callback, ITelegramBotClient botClient, CancellationToken ct)
    {
        await botClient.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        var message = callback.Message!;

        await botClient.EditMessageText(
            chatId: message.Chat.Id,
            messageId: message.Id,
            text: "Добро пожаловать! Выберите действие:",
            cancellationToken: ct,
            replyMarkup: _menu
        );
    }
}
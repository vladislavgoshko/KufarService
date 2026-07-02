using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using static System.Net.Mime.MediaTypeNames;

public class BackToMenuCallbackHandler : IBotHandler
{
    private readonly InlineKeyboardMarkup _menu = new(new[]
    {
        new[] { InlineKeyboardButton.WithCallbackData("🔍 Создать отслеживание", "create_new_search") },
        new[] { InlineKeyboardButton.WithCallbackData("📋 Текущие заказы", "view_existing") },
        new[] { InlineKeyboardButton.WithCallbackData("ℹ️ О боте", "about"), ("Выход", "exit", KeyboardButtonStyle.Danger)}
    });
    public bool CanHandle(Update update) => update.Type == UpdateType.CallbackQuery && update.CallbackQuery!.Data == "back_to_menu";

    public async Task ExecuteAsync(Update update, ITelegramBotClient botClient, CancellationToken ct)
    {
        var callback = update.CallbackQuery!;

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
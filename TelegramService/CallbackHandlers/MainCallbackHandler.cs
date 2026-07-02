using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramService.BusinessLogic;
public class MainCallbackHandler : IBotHandler
{
    InlineKeyboardMarkup _back_to_menu = new[]
    {
        new InlineKeyboardButton[] {
            ("Назад", "back_to_menu", KeyboardButtonStyle.Danger)
        }
    };
    
    public bool CanHandle(Update update) => update.Type == UpdateType.CallbackQuery;

    public async Task ExecuteAsync(Update update, ITelegramBotClient botClient, CancellationToken ct)
    {
        var callback = update.CallbackQuery!;

        await botClient.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        

        var message = callback.Message!;

        if (callback.Data == "exit")
        {
            await botClient.DeleteMessage(message.Chat.Id, message.Id, ct);
            return;
        }

        string responseText = callback.Data switch
        {
            "create_new_search" => "Начинаем настройку нового отслеживания...",
            "view_existing" => "Загружаю ваш список из базы данных API...",
            "about" => "Этот бот — интерфейс для работы с нашей системой управления.",
            _ => "Неизвестная команда"
        };


        await botClient.EditMessageText(
            chatId: message.Chat.Id,
            messageId: message.Id,
            text: responseText,
            cancellationToken: ct,
            replyMarkup: _back_to_menu
            );

        //await botClient.SendMessage(
        //    chatId: callback.Message!.Chat.Id,
        //    text: responseText,
        //    cancellationToken: ct);
    }
}
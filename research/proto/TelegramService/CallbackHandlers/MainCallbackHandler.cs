using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramService;
using TelegramService.BusinessLogic;
public class MainCallbackHandler : CallbackQueryHandlerBase
{
    InlineKeyboardMarkup _back_to_menu = new[]
    {
        new InlineKeyboardButton[] {
            ("Назад", "back_to_menu", KeyboardButtonStyle.Danger)
        }
    };

    protected override bool CanHandle(CallbackQuery callbackQuery) => true;

    public override async Task ExecuteAsync(CallbackQuery callback, ITelegramBotClient botClient, CancellationToken ct)
    {

        await botClient.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        

        var message = callback.Message!;

        if (callback.Data == "exit")
        {
            try
            {
                await botClient.DeleteMessage(message.Chat.Id, message.Id, ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Не удалось удалить сообщение: {ex.Message}");
                // Если не удаётся удалить, пытаемся отредактировать
                try
                {
                    await botClient.EditMessageText(
                        chatId: message.Chat.Id,
                        messageId: message.Id,
                        text: "✅ Закрыто",
                        cancellationToken: ct,
                        replyMarkup: null
                    );
                }
                catch
                {
                    // Если и это не работает, просто игнорируем
                }
            }
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
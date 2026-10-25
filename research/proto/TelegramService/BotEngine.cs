using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;
using TelegramService;

internal class BotEngine
{
    private readonly ITelegramBotClient _botClient;
    private readonly UpdateRouter _router;

    public BotEngine(ITelegramBotClient botClient, UpdateRouter router)
    {
        _botClient = botClient;
        _router = router;
    }

    public async Task ListenForMessageAsync()
    {
        using var cts = new CancellationTokenSource();

        _botClient.StartReceiving(
            updateHandler: async (client, update, ct) =>
                await _router.HandleUpdateAsync(client, update, ct),
            errorHandler: HandlePollingErrorAsync,
            receiverOptions: new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() },
            cancellationToken: cts.Token
        );

        var me = await _botClient.GetMe();
        Console.WriteLine($"Бот @{me.Username} запущен через DI-контейнер.");
        Console.ReadLine();
    }

    private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception ex, HandleErrorSource source, CancellationToken ct)
    {
        Console.WriteLine($"Ошибка: {ex.Message}");
        return Task.CompletedTask;
    }
}
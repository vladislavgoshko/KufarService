using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace KufarBot.Infrastructure.TelegramClient; // Changed to avoid namespace conflict with Telegram.Bot

public interface ITelegramSender
{
    Task SendTextMessageAsync(long chatId, string htmlMessage, CancellationToken ct = default);

    /// <summary>
    /// Sends a photo with an HTML caption. Throws the original exception when Telegram
    /// cannot deliver the photo (e.g. bad URL) — the caller must decide on fallback.
    /// </summary>
    Task SendPhotoAsync(long chatId, string photoUrl, string htmlCaption, CancellationToken ct = default);
}

public class TelegramBotClientSender : ITelegramSender
{
    private readonly ITelegramBotClient _botClient;

    public TelegramBotClientSender(ITelegramBotClient botClient)
    {
        _botClient = botClient ?? throw new ArgumentNullException(nameof(botClient));
    }

    public async Task SendTextMessageAsync(long chatId, string htmlMessage, CancellationToken ct = default)
    {
        await _botClient.SendMessage(
            chatId: chatId,
            text: htmlMessage,
            parseMode: ParseMode.Html,
            cancellationToken: ct
        );
    }

    public async Task SendPhotoAsync(long chatId, string photoUrl, string htmlCaption, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(photoUrl);

        await _botClient.SendPhoto(
            chatId: chatId,
            photo: new InputFileUrl(photoUrl),
            caption: htmlCaption,
            parseMode: ParseMode.Html,
            cancellationToken: ct
        );
    }
}

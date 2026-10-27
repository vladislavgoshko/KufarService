using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace KufarBot.Infrastructure.TelegramClient; // Changed to avoid namespace conflict with Telegram.Bot

public interface ITelegramSender
{
    Task SendTextMessageAsync(long chatId, string htmlMessage, InlineKeyboardMarkup? replyMarkup = null, CancellationToken ct = default);

    /// <summary>
    /// Sends a photo with an HTML caption. Throws the original exception when Telegram
    /// cannot deliver the photo (e.g. bad URL) — the caller must decide on fallback.
    /// </summary>
    Task SendPhotoAsync(long chatId, string photoUrl, string htmlCaption, InlineKeyboardMarkup? replyMarkup = null, CancellationToken ct = default);

    /// <summary>
    /// eph-9: sends a photo album (2–10 items). The caption of the first item becomes the
    /// group caption. Inline keyboards are not supported by Telegram for media groups.
    /// </summary>
    Task SendMediaGroupAsync(long chatId, IReadOnlyList<(string Url, string? HtmlCaption)> items, CancellationToken ct = default);
}

public class TelegramBotClientSender : ITelegramSender
{
    private readonly ITelegramBotClient _botClient;

    public TelegramBotClientSender(ITelegramBotClient botClient)
    {
        _botClient = botClient ?? throw new ArgumentNullException(nameof(botClient));
    }

    public async Task SendTextMessageAsync(long chatId, string htmlMessage, InlineKeyboardMarkup? replyMarkup = null, CancellationToken ct = default)
    {
        await _botClient.SendMessage(
            chatId: chatId,
            text: htmlMessage,
            parseMode: ParseMode.Html,
            replyMarkup: replyMarkup,
            cancellationToken: ct
        );
    }

    public async Task SendPhotoAsync(long chatId, string photoUrl, string htmlCaption, InlineKeyboardMarkup? replyMarkup = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(photoUrl);

        await _botClient.SendPhoto(
            chatId: chatId,
            photo: new InputFileUrl(photoUrl),
            caption: htmlCaption,
            parseMode: ParseMode.Html,
            replyMarkup: replyMarkup,
            cancellationToken: ct
        );
    }

    public async Task SendMediaGroupAsync(long chatId, IReadOnlyList<(string Url, string? HtmlCaption)> items, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count < 1 || items.Count > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(items), "Telegram allows 1–10 media per group.");
        }

        var media = new List<IAlbumInputMedia>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            var (url, caption) = items[i];
            media.Add(new InputMediaPhoto(new InputFileUrl(url))
            {
                Caption = i == 0 ? caption : null, // one caption per group (the first photo)
                ParseMode = i == 0 ? ParseMode.Html : default
            });
        }

        await _botClient.SendMediaGroup(
            chatId,
            media,
            cancellationToken: ct
        );
    }
}

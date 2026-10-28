using Telegram.Bot.Types.ReplyMarkups;

namespace KufarBot.Infrastructure.Telegram;

/// <summary>
/// Fluent helpers to color inline buttons via Bot API 9.4 button styles
/// (primary = blue, success = green, danger = red). Neutral (unstyled) is the default.
/// </summary>
public static class ButtonStyles
{
    /// <summary>Main / positive-neutral accent (blue).</summary>
    public static InlineKeyboardButton Primary(this InlineKeyboardButton button)
    {
        button.Style = KeyboardButtonStyle.Primary;
        return button;
    }

    /// <summary>Positive action: save, confirm, resume (green).</summary>
    public static InlineKeyboardButton Success(this InlineKeyboardButton button)
    {
        button.Style = KeyboardButtonStyle.Success;
        return button;
    }

    /// <summary>Destructive action: delete, cancel input, clear filters (red).</summary>
    public static InlineKeyboardButton Danger(this InlineKeyboardButton button)
    {
        button.Style = KeyboardButtonStyle.Danger;
        return button;
    }
}

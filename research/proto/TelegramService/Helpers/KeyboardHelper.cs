using System.Collections.Generic;
using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramService.Helpers
{
    public static class KeyboardHelper
    {
        /// <summary>
        /// Группирует кнопки в строки по 3, 2 или 1 штуке в зависимости от длины текста.
        /// </summary>
        public static List<List<InlineKeyboardButton>> GroupInlineButtons(IEnumerable<InlineKeyboardButton> buttons)
        {
            var group3 = new List<InlineKeyboardButton>(); // Текст < 10 символов
            var group2 = new List<InlineKeyboardButton>(); // Текст от 10 до 20 символов
            var result = new List<List<InlineKeyboardButton>>();

            foreach (var button in buttons)
            {
                int textLength = button.Text?.Length ?? 0;

                if (textLength < 10)
                {
                    group3.Add(button);
                }
                else if (textLength <= 20)
                {
                    group2.Add(button);
                }
                else
                {
                    result.Add(new List<InlineKeyboardButton> { button });
                }
            }

            while (group3.Count >= 3)
            {
                result.Add(group3.GetRange(0, 3));
                group3.RemoveRange(0, 3);
            }

            while (group2.Count >= 2)
            {
                result.Add(group2.GetRange(0, 2));
                group2.RemoveRange(0, 2);
            }

            if (group3.Count > 0)
            {
                result.Add(new List<InlineKeyboardButton>(group3));
            }

            if (group2.Count > 0)
            {
                result.Add(new List<InlineKeyboardButton>(group2));
            }

            return result;
        }
    }
}
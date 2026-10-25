using System.Collections.Generic;
using System.Linq;
using Telegram.Bot.Types.ReplyMarkups;
using KufarBot.Core.Models;
using KufarBot.Infrastructure.Persistence;

namespace KufarBot.App.Telegram;

public static class BotKeyboards
{
    public static InlineKeyboardMarkup MainMenu()
    {
        return new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🔍 Создать поиск", "menu_create"),
                InlineKeyboardButton.WithCallbackData("📋 Мои подписки", "menu_list")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("ℹ️ О боте", "menu_about")
            }
        });
    }

    public static InlineKeyboardMarkup SearchDraftMenu(UserSearchDraft draft)
    {
        var rows = new List<InlineKeyboardButton[]>();

        string catTitle = !string.IsNullOrEmpty(draft.SubcategoryTitle)
            ? $"📁 {draft.SubcategoryTitle}"
            : "📁 Категория: Не выбрана";
        rows.Add(new[] { InlineKeyboardButton.WithCallbackData(catTitle, "draft_choose_cat") });

        string cityTitle = !string.IsNullOrEmpty(draft.CityName)
            ? $"📍 {draft.CityName}"
            : (draft.CityId == null ? "📍 Город: Вся Беларусь" : $"📍 Город: ID {draft.CityId}");
        rows.Add(new[] { InlineKeyboardButton.WithCallbackData(cityTitle, "draft_choose_city") });

        string minPriceStr = draft.MinPriceByn.HasValue ? $"{draft.MinPriceByn.Value:0.##} BYN" : "Не задана";
        string maxPriceStr = draft.MaxPriceByn.HasValue ? $"{draft.MaxPriceByn.Value:0.##} BYN" : "Не задана";
        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData($"Мин: {minPriceStr}", "draft_set_min_price"),
            InlineKeyboardButton.WithCallbackData($"Макс: {maxPriceStr}", "draft_set_max_price")
        });

        if (draft.MinPriceByn.HasValue || draft.MaxPriceByn.HasValue)
        {
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("🔄 Сбросить цены", "draft_reset_prices") });
        }

        if (draft.SubcategoryId.HasValue)
        {
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("✅ Сохранить подписку", "draft_save") });
        }

        rows.Add(new[] { InlineKeyboardButton.WithCallbackData("❌ Отмена", "menu_main") });

        return new InlineKeyboardMarkup(rows);
    }

    public static InlineKeyboardMarkup CategoriesMenu(IReadOnlyList<KufarCategory> categories)
    {
        var rows = new List<InlineKeyboardButton[]>();
        foreach (var chunk in categories.Chunk(2))
        {
            rows.Add(chunk.Select(c => InlineKeyboardButton.WithCallbackData(c.Name, $"cat_{c.Id}")).ToArray());
        }
        rows.Add(new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад к черновику", "menu_draft") });
        return new InlineKeyboardMarkup(rows);
    }

    public static InlineKeyboardMarkup SubcategoriesMenu(int categoryId, IReadOnlyList<KufarSubcategory> subcategories)
    {
        var rows = new List<InlineKeyboardButton[]>();
        foreach (var chunk in subcategories.Chunk(2))
        {
            rows.Add(chunk.Select(s => InlineKeyboardButton.WithCallbackData(s.Name, $"sub_{s.Id}")).ToArray());
        }
        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("⬅️ К категориям", "draft_choose_cat"),
            InlineKeyboardButton.WithCallbackData("Отмена", "menu_draft")
        });
        return new InlineKeyboardMarkup(rows);
    }

    public static InlineKeyboardMarkup CitiesMenu()
    {
        var rows = new List<InlineKeyboardButton[]>();

        rows.Add(new[] { InlineKeyboardButton.WithCallbackData("🇧🇾 Вся Беларусь", "city_all") });

        var cityButtons = CityCatalog.CitiesByName
            .Select(kvp => InlineKeyboardButton.WithCallbackData(kvp.Key, $"city_{kvp.Value}"))
            .Chunk(2);

        foreach (var chunk in cityButtons)
        {
            rows.Add(chunk);
        }

        rows.Add(new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад к черновику", "menu_draft") });

        return new InlineKeyboardMarkup(rows);
    }

    public static InlineKeyboardMarkup SubscriptionsListMenu(IReadOnlyList<UserSearchEntity> searches)
    {
        var rows = new List<InlineKeyboardButton[]>();

        foreach (var s in searches)
        {
            string statusIcon = s.IsPaused ? "⏸️" : "▶️";
            string title = $"{statusIcon} {s.SubcategoryTitle} ({(s.CityName ?? "Вся Беларусь")})";
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData(title, $"subview_{s.Id}") });
        }

        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("➕ Создать поиск", "menu_create"),
            InlineKeyboardButton.WithCallbackData("⬅️ Главное меню", "menu_main")
        });

        return new InlineKeyboardMarkup(rows);
    }

    public static InlineKeyboardMarkup SubscriptionDetailsMenu(UserSearchEntity search)
    {
        string toggleText = search.IsPaused ? "▶️ Возобновить" : "⏸️ Приостановить";
        string toggleAction = search.IsPaused ? $"resume_{search.Id}" : $"pause_{search.Id}";

        return new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(toggleText, toggleAction),
                InlineKeyboardButton.WithCallbackData("🗑️ Удалить", $"delconf_{search.Id}")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("⬅️ Назад к списку", "menu_list")
            }
        });
    }

    public static InlineKeyboardMarkup DeleteConfirmationMenu(int searchId)
    {
        return new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("⚠️ Да, удалить", $"del_{searchId}"),
                InlineKeyboardButton.WithCallbackData("Отмена", $"subview_{searchId}")
            }
        });
    }

    public static InlineKeyboardMarkup CancelPriceInputMenu()
    {
        return new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("⬅️ Отмена ввода", "menu_draft") }
        });
    }
}

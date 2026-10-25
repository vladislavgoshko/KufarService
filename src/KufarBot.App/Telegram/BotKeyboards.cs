using System;
using System.Collections.Generic;
using System.Linq;
using Telegram.Bot.Types.ReplyMarkups;
using KufarBot.Core.Filters;
using KufarBot.Core.Models;
using KufarBot.Infrastructure.Persistence;

namespace KufarBot.App.Telegram;

public static class BotKeyboards
{
    public static string HelpText =>
        "ℹ️ <b>Kufar Мониторинг — справка</b>\n\n" +
        "<b>Команды:</b>\n" +
        "/start — главное меню\n" +
        "/help — эта справка\n\n" +
        "<b>Как пользоваться:</b>\n" +
        "1) Нажмите «Создать поиск», выберите категорию, подкатегорию и город.\n" +
        "2) Задайте диапазон цен (больше/меньше) и при желании фильтры («Фильтры»).\n" +
        "3) Сохраните подписку — бот мгновенно пришлёт новые объявления с фото.\n\n" +
        "<b>О фильтрах:</b>\n" +
        "• Текст поиска — все слова должны быть в объявлении\n" +
        "• Ключевые слова включить/исключить — совпадение по любому из слов\n" +
        "• Продавец: только частники / только компании\n" +
        "• Только свежие: не старше 1 ч … 3 дней\n" +
        "• Фильтры сайта подкатегории: состояние, характеристики и т.д.\n\n" +
        "<b>Мои подписки</b>: просмотр, пауза, удаление, изменение фильтров.";

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
            rows.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    draft.Filters.Count > 0 ? $"🎛 Фильтры ({draft.Filters.Count})" : "🎛 Фильтры", "fmenu")
            });
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

    public const int SubscriptionsPageSize = 8;

    public static InlineKeyboardMarkup SubscriptionsListMenu(IReadOnlyList<UserSearchEntity> searches, int page = 0)
    {
        int pageCount = Math.Max(1, (searches.Count + SubscriptionsPageSize - 1) / SubscriptionsPageSize);
        page = Math.Clamp(page, 0, pageCount - 1);

        var rows = new List<InlineKeyboardButton[]>();
        int startIndex = page * SubscriptionsPageSize;
        int count = Math.Min(SubscriptionsPageSize, searches.Count - startIndex);

        for (int i = startIndex; i < startIndex + count; i++)
        {
            var s = searches[i];
            string statusIcon = s.IsPaused ? "⏸️" : "▶️";
            string title = $"{statusIcon} {s.SubcategoryTitle} ({(s.CityName ?? "Вся Беларусь")})";
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData(title, $"subview_{s.Id}") });
        }

        if (pageCount > 1)
        {
            var nav = new List<InlineKeyboardButton>();
            if (page > 0)
            {
                nav.Add(InlineKeyboardButton.WithCallbackData("⬅️", $"slp_{page - 1}"));
            }
            nav.Add(InlineKeyboardButton.WithCallbackData($"{page + 1}/{pageCount}", "slpage"));
            if (page < pageCount - 1)
            {
                nav.Add(InlineKeyboardButton.WithCallbackData("➡️", $"slp_{page + 1}"));
            }
            rows.Add(nav.ToArray());
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
                InlineKeyboardButton.WithCallbackData("✏️ Фильтры", $"fedit_{search.Id}")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🗑️ Удалить", $"delconf_{search.Id}"),
                InlineKeyboardButton.WithCallbackData("⬅️ К списку", "menu_list")
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

    // ---------------- Filter wizard keyboards ----------------

    public const int SpecOptionsPageSize = 6;
    public const int SpecMenuPageSize = 8;

    /// <summary>Main wizard screen: chosen filters, spec list (paged), own filters, done.</summary>
    public static InlineKeyboardMarkup FiltersMenu(UserSearchDraft draft)
    {
        var wizard = draft.Wizard ?? new FilterWizardState();
        var rows = new List<InlineKeyboardButton[]>();

        var specs = draft.AvailableSpecs;
        int startIndex = wizard.Page * SpecMenuPageSize;
        int pageItems = Math.Max(0, Math.Min(SpecMenuPageSize, specs.Count - startIndex));
        int pageCount = specs.Count == 0 ? 1 : (specs.Count + SpecMenuPageSize - 1) / SpecMenuPageSize;

        for (int i = startIndex; i < startIndex + pageItems; i++)
        {
            var spec = specs[i];
            string icon = HasValue(draft.Filters, spec) ? "✅" : "▫️";
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData($"{icon} {spec.Title}", $"fs_{i}") });
        }

        rows.Add(new[] { InlineKeyboardButton.WithCallbackData("🎛 Свои фильтры", "fown") });

        var nav = new List<InlineKeyboardButton>();
        if (pageCount > 1)
        {
            if (wizard.Page > 0)
            {
                nav.Add(InlineKeyboardButton.WithCallbackData("⬅️", $"fp_{wizard.Page - 1}"));
            }
            nav.Add(InlineKeyboardButton.WithCallbackData($"{wizard.Page + 1}/{pageCount}", "fpage"));
            if (wizard.Page < pageCount - 1)
            {
                nav.Add(InlineKeyboardButton.WithCallbackData("➡️", $"fp_{wizard.Page + 1}"));
            }
        }
        if (nav.Count > 0)
        {
            rows.Add(nav.ToArray());
        }

        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("✔️ Готово", "fdone"),
            InlineKeyboardButton.WithCallbackData("⬅️ К черновику", "menu_draft")
        });

        return new InlineKeyboardMarkup(rows);
    }

    public static InlineKeyboardMarkup FilterOptionsMenu(UserSearchDraft draft, int specIndex)
    {
        var spec = draft.AvailableSpecs[specIndex];
        var state = draft.Wizard ?? new FilterWizardState();
        var rows = new List<InlineKeyboardButton[]>();

        switch (spec.Kind)
        {
            case FilterKind.Bool:
            case FilterKind.Single:
            case FilterKind.Multi:
                int startIndex = state.SelectedValuesPage * SpecOptionsPageSize;
                int pageItems = Math.Min(SpecOptionsPageSize, spec.Options.Count - startIndex);
                int pageCount = spec.Options.Count == 0 ? 1 : (spec.Options.Count + SpecOptionsPageSize - 1) / SpecOptionsPageSize;
                var chosen = draft.Filters.FirstOrDefault(f => f.Key == spec.Key);

                for (int i = startIndex; i < startIndex + pageItems; i++)
                {
                    var option = spec.Options[i];
                    bool selected = chosen != null && chosen.Values.Contains(option.Value, StringComparer.OrdinalIgnoreCase);
                    string icon = selected ? "🟩" : (chosen != null ? "⃞" : "");
                    string prefix = selected ? $"{icon} " : (chosen != null ? $"{icon} " : "");
                    string data = spec.Kind == FilterKind.Multi
                        ? $"fmv_{specIndex}_{i}"
                        : $"fsv_{specIndex}_{i}";
                    rows.Add(new[] { InlineKeyboardButton.WithCallbackData($"{prefix}{option.Label}", data) });
                }

                if (pageCount > 1)
                {
                    var nav = new List<InlineKeyboardButton>();
                    if (state.SelectedValuesPage > 0)
                    {
                        nav.Add(InlineKeyboardButton.WithCallbackData("⬅️", $"fvp_{specIndex}_{state.SelectedValuesPage - 1}"));
                    }
                    nav.Add(InlineKeyboardButton.WithCallbackData($"{state.SelectedValuesPage + 1}/{pageCount}", "fpage"));
                    if (state.SelectedValuesPage < pageCount - 1)
                    {
                        nav.Add(InlineKeyboardButton.WithCallbackData("➡️", $"fvp_{specIndex}_{state.SelectedValuesPage + 1}"));
                    }
                    rows.Add(nav.ToArray());
                }

                if (spec.Kind == FilterKind.Multi)
                {
                    rows.Add(new[] { InlineKeyboardButton.WithCallbackData("✅ Подтвердить выбор", $"fmdone_{specIndex}") });
                }

                if (chosen != null)
                {
                    rows.Add(new[] { InlineKeyboardButton.WithCallbackData("🧹 Очистить фильтр", $"fclr_{specIndex}") });
                }
                break;

            case FilterKind.Range:
            case FilterKind.Text:
                if (draft.Filters.Any(f => f.Key == spec.Key))
                {
                    rows.Add(new[] { InlineKeyboardButton.WithCallbackData("🧹 Очистить фильтр", $"fclr_{specIndex}") });
                }
                break;
        }

        rows.Add(new[] { InlineKeyboardButton.WithCallbackData("⬅️ К списку фильтров", "fmenu") });
        return new InlineKeyboardMarkup(rows);
    }

    public static InlineKeyboardMarkup OwnFiltersMenu(UserSearchDraft draft)
    {
        var kwInc = draft.Filters.FirstOrDefault(f => f.Kind == SavedFilterKind.KeywordsInclude);
        var kwExc = draft.Filters.FirstOrDefault(f => f.Kind == SavedFilterKind.KeywordsExclude);
        var query = draft.Filters.FirstOrDefault(f => f.Kind == SavedFilterKind.Query);
        var seller = draft.Filters.FirstOrDefault(f => f.Kind == SavedFilterKind.SellerType);
        var age = draft.Filters.FirstOrDefault(f => f.Kind == SavedFilterKind.MaxAgeHours);

        string queryTitle = query == null ? "🔎 Текст поиска" : $"✅ Текст поиска: {JoinShort(query.Values)}";
        string kwIncTitle = kwInc == null ? "🔢 Ключевые слова (включить)" : $"✅ Включить: {JoinShort(kwInc.Values)}";
        string kwExcTitle = kwExc == null ? "🚫 Ключевые слова (исключить)" : $"✅ Исключить: {JoinShort(kwExc.Values)}";
        string sellerTitle = seller == null ? "👤 Продавец: любой" : $"✅ Продавец: {FormatSeller(seller)}";
        string ageTitle = age == null ? "⏰ Только свежие: нет" : $"✅ Свежее: {FormatAge(age)}";

        return new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData(queryTitle, "fown_query") },
            new[] { InlineKeyboardButton.WithCallbackData(kwIncTitle, "fown_kw_inc") },
            new[] { InlineKeyboardButton.WithCallbackData(kwExcTitle, "fown_kw_exc") },
            new[] { InlineKeyboardButton.WithCallbackData(sellerTitle, "fown_seller") },
            new[] { InlineKeyboardButton.WithCallbackData(ageTitle, "fown_age") },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("⬅️ К списку фильтров", "fmenu"),
                InlineKeyboardButton.WithCallbackData("✔️ Готово", "fdone")
            }
        });
    }

    public static InlineKeyboardMarkup SellerTypeMenu(UserSearchDraft draft)
    {
        var current = draft.Filters.FirstOrDefault(f => f.Kind == SavedFilterKind.SellerType)?.Values.FirstOrDefault();

        return new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData($"{(current == "private" ? "🟩 " : "")}Только частники", "fown_private") },
            new[] { InlineKeyboardButton.WithCallbackData($"{(current == "company" ? "🟩 " : "")}Только компании", "fown_company") },
            new[] { InlineKeyboardButton.WithCallbackData($"{(current == null ? "🟩 " : "")}Любой", "fown_any") },
            new[] { InlineKeyboardButton.WithCallbackData("⬅️ К своим фильтрам", "fown") }
        });
    }

    public static InlineKeyboardMarkup MaxAgeMenu(UserSearchDraft draft)
    {
        var current = draft.Filters.FirstOrDefault(f => f.Kind == SavedFilterKind.MaxAgeHours)?.Min;

        var rows = new List<InlineKeyboardButton[]>();
        var options = new (int Hours, string Title)[] { (1, "1 час"), (3, "3 часа"), (12, "12 часов"), (24, "24 часа"), (72, "3 дня") };
        foreach (var chunk in options.Chunk(2))
        {
            rows.Add(chunk.Select(o => InlineKeyboardButton.WithCallbackData(
                current == o.Hours ? $"✅ {o.Title}" : o.Title, $"fown_age_{o.Hours}")).ToArray());
        }
        rows.Add(new[] { InlineKeyboardButton.WithCallbackData($"{(current == null ? "✅ " : "")}Без ограничения", "fown_age_off") });
        rows.Add(new[] { InlineKeyboardButton.WithCallbackData("⬅️ К своим фильтрам", "fown") });
        return new InlineKeyboardMarkup(rows);
    }

    private static string JoinShort(IReadOnlyList<string> values)
    {
        string joined = string.Join(", ", values);
        return joined.Length <= 24 ? joined : joined.Substring(0, 21) + "…";
    }

    private static string FormatSeller(SavedFilter seller)
    {
        return string.Equals(seller.Values.FirstOrDefault(), "company", StringComparison.OrdinalIgnoreCase)
            ? "только компании"
            : "только частники";
    }

    private static string FormatAge(SavedFilter age)
    {
        if (age.Min >= 24) return $"за {age.Min.Value / 24} дн.";
        return $"за {age.Min:0} ч.";
    }

    public static string FilterValueText(SavedFilter? filter)
    {
        if (filter == null) return "—";
        return filter.Kind switch
        {
            SavedFilterKind.Range => $"{filter.Min:0.#}–{filter.Max:0.#}",
            SavedFilterKind.Text => filter.Values.FirstOrDefault() ?? "",
            SavedFilterKind.MaxAgeHours => FormatAge(filter),
            SavedFilterKind.SellerType => FormatSeller(filter),
            _ => string.Join(", ", filter.Values)
        };
    }

    /// <summary>True if the draft already has a value for this spec (by Key and compatible Kind).</summary>
    public static bool HasValue(List<SavedFilter> filters, FilterSpec spec)
    {
        return filters.Any(f =>
            string.Equals(f.Key, spec.Key, StringComparison.OrdinalIgnoreCase) &&
            IsCompatibleKind(f.Kind, spec.Kind));
    }

    public static SavedFilter? FindForSpec(UserSearchDraft draft, FilterSpec spec)
    {
        return draft.Filters.FirstOrDefault(f =>
            string.Equals(f.Key, spec.Key, StringComparison.OrdinalIgnoreCase) &&
            IsCompatibleKind(f.Kind, spec.Kind));
    }

    private static bool IsCompatibleKind(SavedFilterKind saved, FilterKind spec)
    {
        return (saved, spec) switch
        {
            (SavedFilterKind.Bool, FilterKind.Bool) => true,
            (SavedFilterKind.Single, FilterKind.Single) => true,
            (SavedFilterKind.Multi, FilterKind.Multi) => true,
            (SavedFilterKind.Range, FilterKind.Range) => true,
            (SavedFilterKind.Text, FilterKind.Text) => true,
            _ => false
        };
    }
}

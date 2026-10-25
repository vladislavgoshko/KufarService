using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KufarBot.Core.Filters;
using KufarBot.Core.Http;
using KufarBot.Core.Models;
using KufarBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace KufarBot.App.Telegram;

public class BotEngine : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ITelegramBotClient _botClient;
    private readonly UserSessionStore _sessionStore;
    private readonly ILogger<BotEngine> _logger;

    public BotEngine(
        IServiceProvider serviceProvider,
        ITelegramBotClient botClient,
        UserSessionStore sessionStore,
        ILogger<BotEngine> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _botClient = botClient ?? throw new ArgumentNullException(nameof(botClient));
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[BotEngine] Starting Telegram listener...");

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery }
        };

        _botClient.StartReceiving(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandleErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: stoppingToken
        );

        try
        {
            var me = await _botClient.GetMe(stoppingToken);
            _logger.LogInformation("[BotEngine] Bot @{Username} is running.", me.Username);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[BotEngine] Could not fetch bot profile at startup.");
        }

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var subscriptionService = scope.ServiceProvider.GetRequiredService<ISearchSubscriptionService>();
            var categoryService = scope.ServiceProvider.GetRequiredService<ICategoryService>();

            if (update.Type == UpdateType.Message && update.Message != null)
            {
                await HandleMessageAsync(botClient, dbContext, subscriptionService, categoryService, update.Message, ct);
            }
            else if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
            {
                await HandleCallbackQueryAsync(botClient, dbContext, subscriptionService, categoryService, update.CallbackQuery, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BotEngine] Error handling update {UpdateId}", update.Id);
        }
    }

    private async Task EnsureUserExistsAsync(AppDbContext dbContext, User telegramUser, CancellationToken ct)
    {
        long userId = telegramUser.Id;
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user == null)
        {
            user = new UserEntity
            {
                Id = userId,
                Username = telegramUser.Username,
                FirstName = telegramUser.FirstName,
                CreatedAtUtc = DateTime.UtcNow,
                IsActive = true
            };
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync(ct);
        }
        else
        {
            bool modified = false;
            if (user.Username != telegramUser.Username)
            {
                user.Username = telegramUser.Username;
                modified = true;
            }
            if (user.FirstName != telegramUser.FirstName)
            {
                user.FirstName = telegramUser.FirstName;
                modified = true;
            }
            if (modified)
            {
                await dbContext.SaveChangesAsync(ct);
            }
        }
    }

    private async Task HandleMessageAsync(
        ITelegramBotClient botClient,
        AppDbContext dbContext,
        ISearchSubscriptionService subscriptionService,
        ICategoryService categoryService,
        Message message,
        CancellationToken ct)
    {
        if (message.From == null) return;
        await EnsureUserExistsAsync(dbContext, message.From, ct);

        long chatId = message.Chat.Id;
        string? text = message.Text?.Trim();

        if (string.IsNullOrEmpty(text)) return;

        var draft = _sessionStore.GetDraft(message.From.Id);

        if (text.Equals("/start", StringComparison.OrdinalIgnoreCase))
        {
            _sessionStore.ResetDraft(message.From.Id);
            await botClient.SendMessage(
                chatId: chatId,
                text: "👋 <b>Добро пожаловать в Kufar Мониторинг!</b>\n\n" +
                      "Этот бот отслеживает новые объявления на Kufar.by по вашим критериям и мгновенно присылает уведомления.\n\n" +
                      "Выберите действие в меню ниже:",
                parseMode: ParseMode.Html,
                replyMarkup: BotKeyboards.MainMenu(),
                cancellationToken: ct
            );
            return;
        }

        // Check if user is currently entering a filter value (own filters / range / text)
        if (draft != null && draft.AwaitingFilterInput.HasValue)
        {
            await HandleFilterTextInputAsync(botClient, dbContext, chatId, message.From.Id, draft, text, ct);
            return;
        }

        // Check if user is currently entering a price
        if (draft != null && draft.AwaitingPriceInput != PriceInputMode.None)
        {
            if (decimal.TryParse(text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal price) && price >= 0)
            {
                if (draft.AwaitingPriceInput == PriceInputMode.MinPrice)
                {
                    draft.MinPriceByn = price;
                }
                else if (draft.AwaitingPriceInput == PriceInputMode.MaxPrice)
                {
                    draft.MaxPriceByn = price;
                }

                draft.AwaitingPriceInput = PriceInputMode.None;

                await botClient.SendMessage(
                    chatId: chatId,
                    text: $"✅ Цена сохранена!\n\n{FormatDraftSummary(draft)}",
                    parseMode: ParseMode.Html,
                    replyMarkup: BotKeyboards.SearchDraftMenu(draft),
                    cancellationToken: ct
                );
            }
            else
            {
                await botClient.SendMessage(
                    chatId: chatId,
                    text: "❌ Пожалуйста, введите корректное положительное число (например, <b>50</b> или <b>150.5</b>):",
                    parseMode: ParseMode.Html,
                    replyMarkup: BotKeyboards.CancelPriceInputMenu(),
                    cancellationToken: ct
                );
            }
            return;
        }

        // Unknown text message fallback
        await botClient.SendMessage(
            chatId: chatId,
            text: "Используйте меню для навигации:",
            replyMarkup: BotKeyboards.MainMenu(),
            cancellationToken: ct
        );
    }

    private async Task HandleCallbackQueryAsync(
        ITelegramBotClient botClient,
        AppDbContext dbContext,
        ISearchSubscriptionService subscriptionService,
        ICategoryService categoryService,
        CallbackQuery callback,
        CancellationToken ct)
    {
        if (callback.From == null) return;
        await EnsureUserExistsAsync(dbContext, callback.From, ct);

        long userId = callback.From.Id;
        long chatId = callback.Message?.Chat.Id ?? userId;
        int messageId = callback.Message?.Id ?? 0;
        string data = callback.Data ?? string.Empty;

        await botClient.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        // Navigation: Main Menu
        if (data == "menu_main")
        {
            _sessionStore.ResetDraft(userId);
            await EditOrSendMessageAsync(botClient, chatId, messageId,
                "👋 <b>Главное меню:</b>\n\nВыберите действие:",
                BotKeyboards.MainMenu(), ct);
            return;
        }

        if (data == "menu_about")
        {
            await EditOrSendMessageAsync(botClient, chatId, messageId,
                "ℹ️ <b>О боте Kufar Мониторинг</b>\n\n" +
                "• Сервис в реальном времени опрашивает Kufar API\n" +
                "• Фильтрует новые объявления по категории, городу и ценам\n" +
                "• Присылает фото, название, цену и прямую ссылку\n" +
                "• Не создает дубликатов",
                new InlineKeyboardMarkup(new[] { InlineKeyboardButton.WithCallbackData("⬅️ В главное меню", "menu_main") }),
                ct);
            return;
        }

        // Create new search draft
        if (data == "menu_create")
        {
            var draft = _sessionStore.GetOrCreateDraft(userId);
            await EditOrSendMessageAsync(botClient, chatId, messageId,
                $"⚙️ <b>Настройка параметров поиска:</b>\n\n{FormatDraftSummary(draft)}",
                BotKeyboards.SearchDraftMenu(draft), ct);
            return;
        }

        // Back to current draft
        if (data == "menu_draft")
        {
            var draft = _sessionStore.GetOrCreateDraft(userId);
            draft.AwaitingPriceInput = PriceInputMode.None;
            draft.AwaitingFilterInput = null;
            await EditOrSendMessageAsync(botClient, chatId, messageId,
                $"⚙️ <b>Настройка параметров поиска:</b>\n\n{FormatDraftSummary(draft)}",
                BotKeyboards.SearchDraftMenu(draft), ct);
            return;
        }

        // ===================== Filter wizard =====================

        if (data == "fmenu")
        {
            var draft = _sessionStore.GetOrCreateDraft(userId);
            if (!draft.SubcategoryId.HasValue)
            {
                await EditOrSendMessageAsync(botClient, chatId, messageId,
                    "⚠️ Сначала выберите подкатегорию поиска.",
                    BotKeyboards.SearchDraftMenu(draft), ct);
                return;
            }

            await EnsureFilterSpecsLoadedAsync(botClient, chatId, messageId, draft, ct);
            if (draft.Wizard == null)
            {
                draft.Wizard = new FilterWizardState();
            }
            draft.Wizard.SpecIndex = -1;
            draft.Wizard.SelectedValuesPage = 0;

            await EditOrSendMessageAsync(botClient, chatId, messageId,
                RenderFiltersMenuText(draft), BotKeyboards.FiltersMenu(draft), ct);
            return;
        }

        if (data.StartsWith("fp_") && int.TryParse(data.Substring(3), out int filtersPage))
        {
            var draft = _sessionStore.GetOrCreateDraft(userId);
            if (draft.Wizard != null)
            {
                draft.Wizard.Page = Math.Max(0, filtersPage);
                draft.Wizard.SpecIndex = -1;
            }

            await EditOrSendMessageAsync(botClient, chatId, messageId,
                RenderFiltersMenuText(draft), BotKeyboards.FiltersMenu(draft), ct);
            return;
        }

        // Open a specific server-side filter
        if (data.StartsWith("fs_") && int.TryParse(data.Substring(3), out int specIndex))
        {
            var draft = _sessionStore.GetOrCreateDraft(userId);
            if (!IsSpecIndexValid(draft, specIndex))
            {
                await EditOrSendMessageAsync(botClient, chatId, messageId,
                    "⚠️ Фильтры не загружены. Откроем заново.", BotKeyboards.MainMenu(), ct);
                return;
            }

            draft.Wizard!.SpecIndex = specIndex;
            draft.Wizard.SelectedValuesPage = 0;
            var spec = draft.AvailableSpecs[specIndex];

            if (spec.Kind == FilterKind.Range || spec.Kind == FilterKind.Text)
            {
                draft.AwaitingFilterInput = specIndex;
                draft.AwaitingPriceInput = PriceInputMode.None;
                await EditOrSendMessageAsync(botClient, chatId, messageId,
                    FilterWizardUI.RenderSpecScreen(draft, specIndex), BotKeyboards.FilterOptionsMenu(draft, specIndex), ct);
                return;
            }

            draft.AwaitingFilterInput = null;
            await EditOrSendMessageAsync(botClient, chatId, messageId,
                FilterWizardUI.RenderSpecScreen(draft, specIndex), BotKeyboards.FilterOptionsMenu(draft, specIndex), ct);
            return;
        }

        // Paging for option lists inside one filter
        if (data.StartsWith("fvp_"))
        {
            var parts = data.Split('_');
            if (parts.Length == 3 && int.TryParse(parts[1], out int pageSpecIndex) && int.TryParse(parts[2], out int optionsPage)
                && IsSpecIndexValid(_sessionStore.GetOrCreateDraft(userId), pageSpecIndex))
            {
                var draft = _sessionStore.GetOrCreateDraft(userId);
                draft.Wizard!.SpecIndex = pageSpecIndex;
                draft.Wizard.SelectedValuesPage = Math.Max(0, optionsPage);

                await EditOrSendMessageAsync(botClient, chatId, messageId,
                    FilterWizardUI.RenderSpecScreen(draft, pageSpecIndex), BotKeyboards.FilterOptionsMenu(draft, pageSpecIndex), ct);
            }
            return;
        }

        // Single/Bool value choice
        if (data.StartsWith("fsv_"))
        {
            var parts = data.Split('_');
            if (parts.Length == 3 && int.TryParse(parts[1], out int singleSpecIndex) && int.TryParse(parts[2], out int optionIndex))
            {
                await HandleSingleOptionChoiceAsync(botClient, chatId, messageId, userId, singleSpecIndex, optionIndex, ct);
            }
            return;
        }

        // Multi value toggle
        if (data.StartsWith("fmv_"))
        {
            var parts = data.Split('_');
            if (parts.Length == 3 && int.TryParse(parts[1], out int multiSpecIndex) && int.TryParse(parts[2], out int multiOptionIndex))
            {
                await ToggleMultiOptionAsync(botClient, chatId, messageId, userId, multiSpecIndex, multiOptionIndex, ct);
            }
            return;
        }

        if (data.StartsWith("fmdone_") && int.TryParse(data.Substring(7), out int confirmSpecIndex))
        {
            var draft = _sessionStore.GetOrCreateDraft(userId);
            draft.Wizard!.SpecIndex = -1;
            await EditOrSendMessageAsync(botClient, chatId, messageId,
                RenderFiltersMenuText(draft), BotKeyboards.FiltersMenu(draft), ct);
            return;
        }

        // Clear one filter's value
        if (data.StartsWith("fclr_") && int.TryParse(data.Substring(5), out int clearSpecIndex))
        {
            var draft = _sessionStore.GetOrCreateDraft(userId);
            if (IsSpecIndexValid(draft, clearSpecIndex))
            {
                var spec = draft.AvailableSpecs[clearSpecIndex];
                draft.Filters.RemoveAll(f => string.Equals(f.Key, spec.Key, StringComparison.OrdinalIgnoreCase));
                draft.Wizard!.SpecIndex = clearSpecIndex;
            }

            if (IsSpecIndexValid(draft, clearSpecIndex))
            {
                await EditOrSendMessageAsync(botClient, chatId, messageId,
                    $"🧹 Фильтр очищен.\n\n{FilterWizardUI.RenderSpecScreen(draft, clearSpecIndex)}",
                    BotKeyboards.FilterOptionsMenu(draft, clearSpecIndex), ct);
            }
            return;
        }

        // Done with the wizard
        if (data == "fdone")
        {
            await FinishWizardAsync(botClient, chatId, messageId, userId, subscriptionService, ct);
            return;
        }

        // ===================== Own filters =====================

        if (data == "fown")
        {
            var draft = _sessionStore.GetOrCreateDraft(userId);
            await EditOrSendMessageAsync(botClient, chatId, messageId,
                "🎛 <b>Свои фильтры</b>\n\nПрименяются ботом к каждому найденному объявлению:",
                BotKeyboards.OwnFiltersMenu(draft), ct);
            return;
        }

        if (data is "fown_kw_inc" or "fown_kw_exc")
        {
            var draft = _sessionStore.GetOrCreateDraft(userId);
            draft.AwaitingFilterInput = data == "fown_kw_inc" ? -10 : -11;
            draft.AwaitingPriceInput = PriceInputMode.None;

            string hint = data == "fown_kw_inc"
                ? "🔔 Отправьте ключевые слова, <b>одно из которых</b> должно быть в названии/описании (через запятую):\n\n<i>Например: <code>gtx, 1060</code></i>"
                : "🚫 Отправьте ключевые слова, которых <b>не должно быть</b> в объявлении (через запятую):\n\n<i>Например: <code>битая, обмен</code></i>";

            await EditOrSendMessageAsync(botClient, chatId, messageId,
                hint + "\n\n⬇️ Введите одним сообщением (пусто = удалить фильтр):",
                BotKeyboards.CancelPriceInputMenu(), ct);
            return;
        }

        if (data == "fown_seller")
        {
            var draft = _sessionStore.GetOrCreateDraft(userId);
            await EditOrSendMessageAsync(botClient, chatId, messageId,
                "👤 <b>Тип продавца</b>", BotKeyboards.SellerTypeMenu(draft), ct);
            return;
        }

        if (data is "fown_private" or "fown_company" or "fown_any")
        {
            var draft = _sessionStore.GetOrCreateDraft(userId);
            draft.Filters.RemoveAll(f => f.Kind == SavedFilterKind.SellerType);
            if (data == "fown_private")
            {
                draft.Filters.Add(new SavedFilter { Kind = SavedFilterKind.SellerType, Key = "own_seller", Title = "Продавец", Values = new[] { "private" } });
            }
            else if (data == "fown_company")
            {
                draft.Filters.Add(new SavedFilter { Kind = SavedFilterKind.SellerType, Key = "own_seller", Title = "Продавец", Values = new[] { "company" } });
            }

            await EditOrSendMessageAsync(botClient, chatId, messageId,
                "✅ Сохранено.\n\n🎛 <b>Свои фильтры</b>", BotKeyboards.OwnFiltersMenu(draft), ct);
            return;
        }

        if (data == "fown_age")
        {
            var draft = _sessionStore.GetOrCreateDraft(userId);
            await EditOrSendMessageAsync(botClient, chatId, messageId,
                "⏰ <b>Только свежие объявления</b>\n\nНе уведомлять раньше заданного возраста.",
                BotKeyboards.MaxAgeMenu(draft), ct);
            return;
        }

        if (data.StartsWith("fown_age_"))
        {
            var draft = _sessionStore.GetOrCreateDraft(userId);
            if (data == "fown_age_off")
            {
                draft.Filters.RemoveAll(f => f.Kind == SavedFilterKind.MaxAgeHours);
            }
            else if (int.TryParse(data.Substring("fown_age_".Length), out int hours) && hours > 0)
            {
                draft.Filters.RemoveAll(f => f.Kind == SavedFilterKind.MaxAgeHours);
                draft.Filters.Add(new SavedFilter { Kind = SavedFilterKind.MaxAgeHours, Key = "own_age", Title = "Возраст", Min = hours });
            }

            await EditOrSendMessageAsync(botClient, chatId, messageId,
                "✅ Сохранено.\n\n⏰ <b>Только свежие объявления</b>", BotKeyboards.MaxAgeMenu(draft), ct);
            return;
        }

        // ===================== Edit existing subscription's filters =====================

        if (data.StartsWith("fedit_") && int.TryParse(data.Substring(6), out int editSearchId))
        {
            var search = await subscriptionService.GetSearchByIdAsync(editSearchId, userId, ct);
            if (search == null)
            {
                await EditOrSendMessageAsync(botClient, chatId, messageId,
                    "⚠️ Подписка не найдена или была удалена.", BotKeyboards.MainMenu(), ct);
                return;
            }

            var draft = new UserSearchDraft
            {
                SubcategoryId = search.SubcategoryId,
                SubcategoryTitle = search.SubcategoryTitle,
                CityId = search.CityId,
                CityName = search.CityName,
                MinPriceByn = search.MinPriceByn,
                MaxPriceByn = search.MaxPriceByn,
                Filters = SavedFilter.Deserialize(search.FiltersJson),
                EditingSearchId = search.Id,
                Wizard = new FilterWizardState()
            };
            _sessionStore.SetDraft(userId, draft);

            await EnsureFilterSpecsLoadedAsync(botClient, chatId, messageId, draft, ct);

            await EditOrSendMessageAsync(botClient, chatId, messageId,
                $"✏️ <b>Редактирование фильтров подписки #{search.Id}</b>\n\n{RenderFiltersMenuText(draft)}",
                BotKeyboards.FiltersMenu(draft), ct);
            return;
        }

        // Draft: Choose Category
        if (data == "draft_choose_cat")
        {
            var categories = await categoryService.GetCategoriesAsync(ct);
            if (categories.Count == 0)
            {
                await EditOrSendMessageAsync(botClient, chatId, messageId,
                    "⚠️ Не удалось загрузить категории Kufar. Пожалуйста, попробуйте позже.",
                    new InlineKeyboardMarkup(new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "menu_draft") }),
                    ct);
                return;
            }

            await EditOrSendMessageAsync(botClient, chatId, messageId,
                "📁 <b>Выберите категорию:</b>",
                BotKeyboards.CategoriesMenu(categories), ct);
            return;
        }

        // Draft: Category Selected -> Show subcategories
        if (data.StartsWith("cat_") && int.TryParse(data.Substring(4), out int catId))
        {
            var category = await categoryService.GetCategoryByIdAsync(catId, ct);
            if (category == null || category.Subcategories.Count == 0)
            {
                await EditOrSendMessageAsync(botClient, chatId, messageId,
                    "⚠️ В этой категории нет доступных подкатегорий.",
                    new InlineKeyboardMarkup(new[] { InlineKeyboardButton.WithCallbackData("⬅️ К категориям", "draft_choose_cat") }),
                    ct);
                return;
            }

            var draft = _sessionStore.GetOrCreateDraft(userId);
            draft.CategoryId = category.Id;
            draft.CategoryTitle = category.Name;

            await EditOrSendMessageAsync(botClient, chatId, messageId,
                $"📁 <b>Категория: {category.Name}</b>\n\nВыберите подкатегорию:",
                BotKeyboards.SubcategoriesMenu(category.Id, category.Subcategories), ct);
            return;
        }

        // Draft: Subcategory Selected
        if (data.StartsWith("sub_") && int.TryParse(data.Substring(4), out int subId))
        {
            var (category, subcategory) = await categoryService.FindSubcategoryAsync(subId, ct);
            var draft = _sessionStore.GetOrCreateDraft(userId);
            draft.SubcategoryId = subId;
            draft.SubcategoryTitle = subcategory?.Name ?? $"Подкатегория {subId}";
            if (category != null)
            {
                draft.CategoryId = category.Id;
                draft.CategoryTitle = category.Name;
            }

            await EditOrSendMessageAsync(botClient, chatId, messageId,
                $"✅ Подкатегория выбрана: <b>{draft.SubcategoryTitle}</b>\n\n{FormatDraftSummary(draft)}",
                BotKeyboards.SearchDraftMenu(draft), ct);
            return;
        }

        // Draft: Choose City
        if (data == "draft_choose_city")
        {
            await EditOrSendMessageAsync(botClient, chatId, messageId,
                "📍 <b>Выберите город для поиска:</b>",
                BotKeyboards.CitiesMenu(), ct);
            return;
        }

        // Draft: City Selected (All Belarus)
        if (data == "city_all")
        {
            var draft = _sessionStore.GetOrCreateDraft(userId);
            draft.CityId = null;
            draft.CityName = "Вся Беларусь";

            await EditOrSendMessageAsync(botClient, chatId, messageId,
                $"✅ Регион выбран: <b>Вся Беларусь</b>\n\n{FormatDraftSummary(draft)}",
                BotKeyboards.SearchDraftMenu(draft), ct);
            return;
        }

        // Draft: Specific City Selected
        if (data.StartsWith("city_") && int.TryParse(data.Substring(5), out int cityId))
        {
            var draft = _sessionStore.GetOrCreateDraft(userId);
            draft.CityId = cityId;
            draft.CityName = CityCatalog.GetCityName(cityId) ?? $"Город #{cityId}";

            await EditOrSendMessageAsync(botClient, chatId, messageId,
                $"✅ Город выбран: <b>{draft.CityName}</b>\n\n{FormatDraftSummary(draft)}",
                BotKeyboards.SearchDraftMenu(draft), ct);
            return;
        }

        // Draft: Set Prices
        if (data == "draft_set_min_price")
        {
            var draft = _sessionStore.GetOrCreateDraft(userId);
            draft.AwaitingPriceInput = PriceInputMode.MinPrice;
            await EditOrSendMessageAsync(botClient, chatId, messageId,
                "💰 <b>Введите минимальную цену в BYN</b> в ответном сообщении:\n(например, <code>50</code>)",
                BotKeyboards.CancelPriceInputMenu(), ct);
            return;
        }

        if (data == "draft_set_max_price")
        {
            var draft = _sessionStore.GetOrCreateDraft(userId);
            draft.AwaitingPriceInput = PriceInputMode.MaxPrice;
            await EditOrSendMessageAsync(botClient, chatId, messageId,
                "💰 <b>Введите максимальную цену в BYN</b> в ответном сообщении:\n(например, <code>250</code>)",
                BotKeyboards.CancelPriceInputMenu(), ct);
            return;
        }

        if (data == "draft_reset_prices")
        {
            var draft = _sessionStore.GetOrCreateDraft(userId);
            draft.MinPriceByn = null;
            draft.MaxPriceByn = null;
            draft.AwaitingPriceInput = PriceInputMode.None;

            await EditOrSendMessageAsync(botClient, chatId, messageId,
                $"🔄 Ценовые фильтры сброшены.\n\n{FormatDraftSummary(draft)}",
                BotKeyboards.SearchDraftMenu(draft), ct);
            return;
        }

        // Draft: Save Subscription
        if (data == "draft_save")
        {
            var draft = _sessionStore.GetDraft(userId);
            if (draft == null || !draft.SubcategoryId.HasValue)
            {
                await EditOrSendMessageAsync(botClient, chatId, messageId,
                    "⚠️ Ошибка: категория не выбрана. Пожалуйста, начните сначала.",
                    BotKeyboards.MainMenu(), ct);
                return;
            }

            if (draft.MinPriceByn.HasValue && draft.MaxPriceByn.HasValue && draft.MinPriceByn.Value > draft.MaxPriceByn.Value)
            {
                await EditOrSendMessageAsync(botClient, chatId, messageId,
                    "❌ Ошибка: минимальная цена не может быть больше максимальной!",
                    BotKeyboards.SearchDraftMenu(draft), ct);
                return;
            }

            var savedSearch = await subscriptionService.SaveSearchAsync(
                userId: userId,
                subcategoryId: draft.SubcategoryId.Value,
                subcategoryTitle: draft.SubcategoryTitle ?? $"Подкатегория {draft.SubcategoryId.Value}",
                cityId: draft.CityId,
                cityName: draft.CityName,
                minPriceByn: draft.MinPriceByn,
                maxPriceByn: draft.MaxPriceByn,
                filtersJson: SavedFilter.Serialize(draft.Filters),
                ct: ct
            );

            _sessionStore.ResetDraft(userId);

            int savedFilterCount = SavedFilter.Deserialize(savedSearch.FiltersJson).Count;
            string filtersInfo = savedFilterCount > 0
                ? $"🎛 Фильтров в подписке: <b>{savedFilterCount}</b>\n"
                : "";

            await EditOrSendMessageAsync(botClient, chatId, messageId,
                $"🎉 <b>Подписка успешно сохранена и активирована!</b>\n\n" +
                $"📁 Категория: <b>{savedSearch.SubcategoryTitle}</b>\n" +
                $"📍 Город: <b>{(savedSearch.CityName ?? "Вся Беларусь")}</b>\n" +
                $"💰 Цена: <b>{FormatPriceRange(savedSearch.MinPriceByn, savedSearch.MaxPriceByn)}</b>\n" +
                filtersInfo +
                $"\nКак только на Kufar появится подходящее объявление, бот сразу пришлет вам сообщение.",
                BotKeyboards.MainMenu(), ct);
            return;
        }

        // List Subscriptions
        if (data == "menu_list")
        {
            var searches = await subscriptionService.GetUserSearchesAsync(userId, ct);
            if (searches.Count == 0)
            {
                await EditOrSendMessageAsync(botClient, chatId, messageId,
                    "📋 <b>У вас пока нет сохраненных подписок.</b>\n\n" +
                    "Нажмите кнопку ниже, чтобы создать первое отслеживание!",
                    new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("➕ Создать поиск", "menu_create") },
                        new[] { InlineKeyboardButton.WithCallbackData("⬅️ Главное меню", "menu_main") }
                    }), ct);
                return;
            }

            await EditOrSendMessageAsync(botClient, chatId, messageId,
                $"📋 <b>Ваши активные подписки ({searches.Count}):</b>\n\n" +
                "Нажмите на подписку для управления (пауза / удаление):",
                BotKeyboards.SubscriptionsListMenu(searches), ct);
            return;
        }

        // View Subscription Details
        if (data.StartsWith("subview_") && int.TryParse(data.Substring(8), out int viewSearchId))
        {
            var search = await subscriptionService.GetSearchByIdAsync(viewSearchId, userId, ct);
            if (search == null)
            {
                await EditOrSendMessageAsync(botClient, chatId, messageId,
                    "⚠️ Подписка не найдена или была удалена.",
                    BotKeyboards.MainMenu(), ct);
                return;
            }

            string statusText = search.IsPaused ? "⏸️ <b>Приостановлена</b>" : "▶️ <b>Активна (мониторинг идет)</b>";
            string details = $"🔍 <b>Информация о подписке #{search.Id}</b>\n\n" +
                             $"Статус: {statusText}\n" +
                             $"📁 Категория: <b>{search.SubcategoryTitle}</b>\n" +
                             $"📍 Регион: <b>{(search.CityName ?? "Вся Беларусь")}</b>\n" +
                             $"💰 Диапазон цен: <b>{FormatPriceRange(search.MinPriceByn, search.MaxPriceByn)}</b>\n" +
                             $"📅 Создана: {search.CreatedAtUtc:dd.MM.yyyy HH:mm} UTC\n";

            await EditOrSendMessageAsync(botClient, chatId, messageId,
                details,
                BotKeyboards.SubscriptionDetailsMenu(search), ct);
            return;
        }

        // Pause Subscription
        if (data.StartsWith("pause_") && int.TryParse(data.Substring(6), out int pauseSearchId))
        {
            bool ok = await subscriptionService.TogglePauseAsync(pauseSearchId, userId, isPaused: true, ct);
            if (ok)
            {
                var search = await subscriptionService.GetSearchByIdAsync(pauseSearchId, userId, ct);
                if (search != null)
                {
                    await EditOrSendMessageAsync(botClient, chatId, messageId,
                        $"⏸️ Подписка <b>\"{search.SubcategoryTitle}\"</b> приостановлена.\nУведомления временно отключены.",
                        BotKeyboards.SubscriptionDetailsMenu(search), ct);
                    return;
                }
            }
            await EditOrSendMessageAsync(botClient, chatId, messageId,
                "⚠️ Не удалось приостановить подписку.",
                BotKeyboards.MainMenu(), ct);
            return;
        }

        // Resume Subscription
        if (data.StartsWith("resume_") && int.TryParse(data.Substring(7), out int resumeSearchId))
        {
            bool ok = await subscriptionService.TogglePauseAsync(resumeSearchId, userId, isPaused: false, ct);
            if (ok)
            {
                var search = await subscriptionService.GetSearchByIdAsync(resumeSearchId, userId, ct);
                if (search != null)
                {
                    await EditOrSendMessageAsync(botClient, chatId, messageId,
                        $"▶️ Подписка <b>\"{search.SubcategoryTitle}\"</b> возобновлена!\nМониторинг снова активен.",
                        BotKeyboards.SubscriptionDetailsMenu(search), ct);
                    return;
                }
            }
            await EditOrSendMessageAsync(botClient, chatId, messageId,
                "⚠️ Не удалось возобновить подписку.",
                BotKeyboards.MainMenu(), ct);
            return;
        }

        // Delete Confirmation
        if (data.StartsWith("delconf_") && int.TryParse(data.Substring(8), out int delConfSearchId))
        {
            var search = await subscriptionService.GetSearchByIdAsync(delConfSearchId, userId, ct);
            if (search == null)
            {
                await EditOrSendMessageAsync(botClient, chatId, messageId,
                    "⚠️ Подписка не найдена.",
                    BotKeyboards.MainMenu(), ct);
                return;
            }

            await EditOrSendMessageAsync(botClient, chatId, messageId,
                $"❓ <b>Вы уверены, что хотите удалить подписку?</b>\n\n" +
                $"\"{search.SubcategoryTitle}\" ({(search.CityName ?? "Вся Беларусь")})",
                BotKeyboards.DeleteConfirmationMenu(delConfSearchId), ct);
            return;
        }

        // Confirm Delete
        if (data.StartsWith("del_") && int.TryParse(data.Substring(4), out int delSearchId))
        {
            bool deleted = await subscriptionService.DeleteSearchAsync(delSearchId, userId, ct);
            if (deleted)
            {
                await EditOrSendMessageAsync(botClient, chatId, messageId,
                    "🗑️ <b>Подписка успешно удалена.</b>",
                    new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("📋 К списку подписок", "menu_list") },
                        new[] { InlineKeyboardButton.WithCallbackData("⬅️ Главное меню", "menu_main") }
                    }), ct);
            }
            else
            {
                await EditOrSendMessageAsync(botClient, chatId, messageId,
                    "⚠️ Не удалось удалить подписку (возможно, она уже была удалена).",
                    BotKeyboards.MainMenu(), ct);
            }
            return;
        }
    }

    private static string FormatDraftSummary(UserSearchDraft draft)
    {
        string catStr = !string.IsNullOrEmpty(draft.SubcategoryTitle)
            ? $"<b>{draft.SubcategoryTitle}</b>"
            : "<i>не выбрана</i>";

        string cityStr = !string.IsNullOrEmpty(draft.CityName)
            ? $"<b>{draft.CityName}</b>"
            : (draft.CityId == null ? "<b>Вся Беларусь</b>" : $"<b>ID {draft.CityId}</b>");

        string priceStr = FormatPriceRange(draft.MinPriceByn, draft.MaxPriceByn);

        int filterCount = draft.Filters.Count;
        string filtersStr = filterCount > 0
            ? $"🎛 Фильтры: <b>{filterCount}</b> ({string.Join(", ", draft.Filters.Select(f => System.Net.WebUtility.HtmlEncode(f.Title)))})"
            : "🎛 Фильтры: <i>нет</i>";

        return $"📁 Подкатегория: {catStr}\n" +
               $"📍 Регион: {cityStr}\n" +
               $"💰 Цена: <b>{priceStr}</b>\n" +
               $"{filtersStr}\n";
    }

    // ===================== Filter wizard helpers =====================

    private async Task EnsureFilterSpecsLoadedAsync(
        ITelegramBotClient botClient,
        long chatId,
        int messageId,
        UserSearchDraft draft,
        CancellationToken ct)
    {
        if (draft.SubcategoryId == null || draft.AvailableSpecs.Count > 0)
        {
            return;
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var kufarClient = scope.ServiceProvider.GetRequiredService<IKufarApiClient>();
            draft.AvailableSpecs = (await kufarClient.GetFiltersForCategoryAsync(draft.SubcategoryId.Value, ct)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[BotEngine] Failed to load filter specs for subcategory {SubId}", draft.SubcategoryId);
        }
    }

    private static bool IsSpecIndexValid(UserSearchDraft draft, int specIndex)
    {
        return draft.Wizard != null &&
               specIndex >= 0 &&
               specIndex < draft.AvailableSpecs.Count;
    }

    private static string RenderFiltersMenuText(UserSearchDraft draft)
    {
        var sb = new System.Text.StringBuilder();

        if (draft.EditingSearchId.HasValue)
        {
            sb.AppendLine($"✏️ <b>Редактирование подписки #{draft.EditingSearchId.Value}</b>\n");
        }

        if (draft.Filters.Count > 0)
        {
            sb.AppendLine("<b>Выбранные фильтры:</b>");
            foreach (var filter in draft.Filters)
            {
                sb.AppendLine($"• {System.Net.WebUtility.HtmlEncode(filter.Title)}: {System.Net.WebUtility.HtmlEncode(BotKeyboards.FilterValueText(filter))}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("Выберите фильтр для настройки или нажмите «Готово».");
        return sb.ToString();
    }

    private async Task HandleSingleOptionChoiceAsync(
        ITelegramBotClient botClient,
        long chatId,
        int messageId,
        long userId,
        int specIndex,
        int optionIndex,
        CancellationToken ct)
    {
        var draft = _sessionStore.GetOrCreateDraft(userId);
        if (!IsSpecIndexValid(draft, specIndex))
        {
            return;
        }

        var spec = draft.AvailableSpecs[specIndex];
        if (optionIndex < 0 || optionIndex >= spec.Options.Count)
        {
            return;
        }

        var option = spec.Options[optionIndex];

        if (spec.Kind == FilterKind.Bool)
        {
            // toggle
            draft.Filters.RemoveAll(f =>
                string.Equals(f.Key, spec.Key, StringComparison.OrdinalIgnoreCase) &&
                f.Kind == SavedFilterKind.Bool);
            if (option.Value != "0")
            {
                draft.Filters.Add(new SavedFilter
                {
                    Kind = SavedFilterKind.Bool,
                    Key = spec.Key,
                    Title = spec.Title,
                    Values = new[] { option.Value }
                });
            }
        }
        else // Single
        {
            var existing = BotKeyboards.FindForSpec(draft, spec);
            if (existing != null &&
                existing.Values.FirstOrDefault() is string current &&
                string.Equals(current, option.Value, StringComparison.OrdinalIgnoreCase))
            {
                // clicking the same value unselects
                draft.Filters.Remove(existing);
            }
            else
            {
                draft.Filters.Remove(existing!);
                draft.Filters.Add(new SavedFilter
                {
                    Kind = SavedFilterKind.Single,
                    Key = spec.Key,
                    Title = spec.Title,
                    Values = new[] { option.Value }
                });
            }
        }

        await EditOrSendMessageAsync(botClient, chatId, messageId,
            FilterWizardUI.RenderSpecScreen(draft, specIndex), BotKeyboards.FilterOptionsMenu(draft, specIndex), ct);
    }

    private async Task ToggleMultiOptionAsync(
        ITelegramBotClient botClient,
        long chatId,
        int messageId,
        long userId,
        int specIndex,
        int optionIndex,
        CancellationToken ct)
    {
        var draft = _sessionStore.GetOrCreateDraft(userId);
        if (!IsSpecIndexValid(draft, specIndex))
        {
            return;
        }

        var spec = draft.AvailableSpecs[specIndex];
        if (optionIndex < 0 || optionIndex >= spec.Options.Count)
        {
            return;
        }

        var option = spec.Options[optionIndex];
        var chosen = BotKeyboards.FindForSpec(draft, spec);
        var values = chosen?.Values.ToList() ?? new List<string>();

        if (values.Contains(option.Value, StringComparer.OrdinalIgnoreCase))
        {
            values.Remove(option.Value);
        }
        else
        {
            values.Add(option.Value);
        }

        draft.Filters.Remove(chosen!);
        draft.Filters.Add(new SavedFilter
        {
            Kind = SavedFilterKind.Multi,
            Key = spec.Key,
            Title = spec.Title,
            Values = values
        });

        await EditOrSendMessageAsync(botClient, chatId, messageId,
            FilterWizardUI.RenderSpecScreen(draft, specIndex), BotKeyboards.FilterOptionsMenu(draft, specIndex), ct);
    }

    private async Task FinishWizardAsync(
        ITelegramBotClient botClient,
        long chatId,
        int messageId,
        long userId,
        ISearchSubscriptionService subscriptionService,
        CancellationToken ct)
    {
        var draft = _sessionStore.GetDraft(userId);
        if (draft == null || !draft.SubcategoryId.HasValue)
        {
            await EditOrSendMessageAsync(botClient, chatId, messageId,
                "⚠️ Черновик поиска не найден. Начните заново.", BotKeyboards.MainMenu(), ct);
            return;
        }

        if (draft.EditingSearchId.HasValue)
        {
            string json = SavedFilter.Serialize(draft.Filters);
            bool ok = await subscriptionService.UpdateSearchFiltersAsync(draft.EditingSearchId.Value, userId, json, ct);
            _sessionStore.ResetDraft(userId);

            if (ok)
            {
                await EditOrSendMessageAsync(botClient, chatId, messageId,
                    $"✅ Фильтры подписки #{draft.EditingSearchId.Value} обновлены." +
                    (json == "[]" ? "\n(фильтры сброшены — подписка без фильтров)" : ""),
                    new InlineKeyboardMarkup(new[] { InlineKeyboardButton.WithCallbackData("📋 К подписке", $"subview_{draft.EditingSearchId.Value}") }), ct);
            }
            else
            {
                await EditOrSendMessageAsync(botClient, chatId, messageId,
                    "⚠️ Не удалось обновить подписку.", BotKeyboards.MainMenu(), ct);
            }
            return;
        }

        // New search: back to the draft screen, all chosen filters are in the draft already
        draft.Wizard = null;
        draft.AwaitingFilterInput = null;
        await EditOrSendMessageAsync(botClient, chatId, messageId,
            $"⚙️ <b>Настройка параметров поиска:</b>\n\n{FormatDraftSummary(draft)}",
            BotKeyboards.SearchDraftMenu(draft), ct);
    }

    private async Task HandleFilterTextInputAsync(
        ITelegramBotClient botClient,
        AppDbContext dbContext,
        long chatId,
        long userId,
        UserSearchDraft draft,
        string text,
        CancellationToken ct)
    {
        int pending = draft.AwaitingFilterInput!.Value;
        draft.AwaitingFilterInput = null;

        if (pending == -10 || pending == -11)
        {
            var keywords = FilterWizardUI.ParseKeywordsInput(text);
            SavedFilterKind kind = pending == -10 ? SavedFilterKind.KeywordsInclude : SavedFilterKind.KeywordsExclude;
            string title = pending == -10 ? "Ключевые слова (включить)" : "Ключевые слова (исключить)";

            draft.Filters.RemoveAll(f => f.Kind == kind);
            if (keywords.Count > 0)
            {
                draft.Filters.Add(new SavedFilter
                {
                    Kind = kind,
                    Key = pending == -10 ? "own_kw_inc" : "own_kw_exc",
                    Title = title,
                    Values = keywords
                });
                await botClient.SendMessage(chatId: chatId,
                    $"✅ Сохранено: <b>{FilterWizardUI.Escape(string.Join(", ", keywords))}</b>",
                    parseMode: ParseMode.Html, cancellationToken: ct);
            }
            else
            {
                await botClient.SendMessage(chatId: chatId, "🧹 Фильтр удалён.", cancellationToken: ct);
            }

            await botClient.SendMessage(chatId: chatId,
                "🎛 <b>Свои фильтры</b>", parseMode: ParseMode.Html, replyMarkup: BotKeyboards.OwnFiltersMenu(draft), cancellationToken: ct);
            return;
        }

        if (pending < 0 || pending >= draft.AvailableSpecs.Count)
        {
            return;
        }

        var spec = draft.AvailableSpecs[pending];
        if (spec.Kind == FilterKind.Range)
        {
            var parsed = FilterWizardUI.ParseRangeInput(text);
            if (!parsed.Success)
            {
                draft.AwaitingFilterInput = pending;
                await botClient.SendMessage(chatId: chatId,
                    $"❌ {parsed.Error}\nПопробуйте ещё раз (<code>8-32</code>, <code>до 500</code>):",
                    parseMode: ParseMode.Html, replyMarkup: BotKeyboards.CancelPriceInputMenu(), cancellationToken: ct);
                return;
            }

            draft.Filters.RemoveAll(f => string.Equals(f.Key, spec.Key, StringComparison.OrdinalIgnoreCase));
            draft.Filters.Add(new SavedFilter
            {
                Kind = SavedFilterKind.Range,
                Key = spec.Key,
                Title = spec.Title,
                Min = parsed.Min,
                Max = parsed.Max
            });

            await botClient.SendMessage(chatId: chatId,
                $"✅ Фильтр <b>{FilterWizardUI.Escape(spec.Title)}</b>: {parsed.Min:0.#}–{parsed.Max:0.#}",
                parseMode: ParseMode.Html, cancellationToken: ct);
        }
        else if (spec.Kind == FilterKind.Text)
        {
            var parsed = FilterWizardUI.ParseTextInput(text);
            if (!parsed.Success)
            {
                draft.AwaitingFilterInput = pending;
                await botClient.SendMessage(chatId: chatId, "❌ Введите значение одним сообщением:",
                    replyMarkup: BotKeyboards.CancelPriceInputMenu(), cancellationToken: ct);
                return;
            }

            draft.Filters.RemoveAll(f => string.Equals(f.Key, spec.Key, StringComparison.OrdinalIgnoreCase));
            draft.Filters.Add(new SavedFilter
            {
                Kind = SavedFilterKind.Text,
                Key = spec.Key,
                Title = spec.Title,
                Values = new[] { parsed.TextValue! }
            });

            await botClient.SendMessage(chatId: chatId,
                $"✅ Фильтр <b>{FilterWizardUI.Escape(spec.Title)}</b>: {FilterWizardUI.Escape(parsed.TextValue)}",
                parseMode: ParseMode.Html, cancellationToken: ct);
        }
        else
        {
            return;
        }

        draft.Wizard!.SpecIndex = pending;
        await botClient.SendMessage(chatId: chatId,
            FilterWizardUI.RenderSpecScreen(draft, pending),
            parseMode: ParseMode.Html, replyMarkup: BotKeyboards.FilterOptionsMenu(draft, pending), cancellationToken: ct);
    }

    private static string FormatPriceRange(decimal? min, decimal? max)
    {
        if (min.HasValue && max.HasValue) return $"{min.Value:0.##} – {max.Value:0.##} BYN";
        if (min.HasValue) return $"от {min.Value:0.##} BYN";
        if (max.HasValue) return $"до {max.Value:0.##} BYN";
        return "любая";
    }

    private async Task EditOrSendMessageAsync(
        ITelegramBotClient botClient,
        long chatId,
        int messageId,
        string text,
        InlineKeyboardMarkup markup,
        CancellationToken ct)
    {
        try
        {
            if (messageId > 0)
            {
                await botClient.EditMessageText(
                    chatId: chatId,
                    messageId: messageId,
                    text: text,
                    parseMode: ParseMode.Html,
                    replyMarkup: markup,
                    cancellationToken: ct
                );
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[BotEngine] EditMessageText failed, fallback to SendMessage.");
        }

        await botClient.SendMessage(
            chatId: chatId,
            text: text,
            parseMode: ParseMode.Html,
            replyMarkup: markup,
            cancellationToken: ct
        );
    }

    private Task HandleErrorAsync(ITelegramBotClient botClient, Exception ex, HandleErrorSource source, CancellationToken ct)
    {
        _logger.LogError(ex, "[BotEngine] Telegram polling error from source {Source}", source);
        return Task.CompletedTask;
    }
}

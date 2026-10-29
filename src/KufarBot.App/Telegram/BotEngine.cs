using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KufarBot.Core.Filters;
using KufarBot.Core.Analytics;
using KufarBot.Core.Http;
using KufarBot.Core.Models;
using KufarBot.Infrastructure.Analytics;
using KufarBot.Infrastructure.Persistence;
using KufarBot.Infrastructure.Telegram;
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
    /// <summary>Maximum number of simultaneously active (non-paused) subscriptions per user.</summary>
    private const int MaxActiveSubscriptions = 10;

    private readonly IServiceProvider _serviceProvider;
    private readonly ITelegramBotClient _botClient;
    private readonly UserSessionStore _sessionStore;
    private readonly ILogger<BotEngine> _logger;
    private readonly AdminOptions _adminOptions;

    public BotEngine(
        IServiceProvider serviceProvider,
        ITelegramBotClient botClient,
        UserSessionStore sessionStore,
        ILogger<BotEngine> logger,
        AdminOptions? adminOptions = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _botClient = botClient ?? throw new ArgumentNullException(nameof(botClient));
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _adminOptions = adminOptions ?? new AdminOptions();
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
            var analytics = scope.ServiceProvider.GetRequiredService<IUsageAnalytics>();

            if (update.Type == UpdateType.Message && update.Message != null)
            {
                long telegramUserId = update.Message.From?.Id ?? 0;

                // eph-10: restore the persisted draft so restarts don't lose wizard state
                await LoadDraftFromDbAsync(dbContext, telegramUserId, ct);
                await HandleMessageAsync(botClient, dbContext, subscriptionService, categoryService, analytics, update.Message, ct);
                await SaveDraftToDbAsync(dbContext, telegramUserId, ct);
            }
            else if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
            {
                long telegramUserId = update.CallbackQuery.From?.Id ?? 0;
                var kufarClient = scope.ServiceProvider.GetRequiredService<IKufarApiClient>();

                await LoadDraftFromDbAsync(dbContext, telegramUserId, ct);
                await HandleCallbackQueryAsync(botClient, dbContext, subscriptionService, categoryService, kufarClient, analytics, update.CallbackQuery, ct);
                await SaveDraftToDbAsync(dbContext, telegramUserId, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BotEngine] Error handling update {UpdateId}", update.Id);
        }
    }

    /// <summary>Loads the persisted draft into the session store when it is not in memory (eph-10).</summary>
    private async Task LoadDraftFromDbAsync(AppDbContext dbContext, long userId, CancellationToken ct)
    {
        if (userId == 0 || _sessionStore.GetDraft(userId) != null)
        {
            return;
        }

        try
        {
            var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            var draft = DraftPersistence.Deserialize(user?.DraftJson);
            if (draft != null)
            {
                _sessionStore.SetDraft(userId, draft);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[BotEngine] Draft restore failed for User={UserId}", userId);
        }
    }

    /// <summary>Persists (or clears, after a reset) the user's draft after each interaction (eph-10).</summary>
    private async Task SaveDraftToDbAsync(AppDbContext dbContext, long userId, CancellationToken ct)
    {
        if (userId == 0)
        {
            return;
        }

        try
        {
            string? json = DraftPersistence.Serialize(_sessionStore.GetDraft(userId));

            var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user == null || user.DraftJson == json)
            {
                return;
            }

            user.DraftJson = json;
            await dbContext.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[BotEngine] Draft save failed for User={UserId}", userId);
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
        IUsageAnalytics analytics,
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

            // eph-17: acquisition point for the bot-start -> first-search funnel
            await analytics.RecordAsync(message.From.Id, UsageEventTypes.BotStarted, ct: ct);

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

        if (text.Equals("/help", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("/about", StringComparison.OrdinalIgnoreCase))
        {
            await botClient.SendMessage(
                chatId: chatId,
                text: BotKeyboards.HelpText,
                parseMode: ParseMode.Html,
                replyMarkup: BotKeyboards.MainMenu(),
                cancellationToken: ct
            );
            return;
        }

        // eph-12: admin-only service dashboard
        if (text.StartsWith("/status", StringComparison.OrdinalIgnoreCase))
        {
            if (!_adminOptions.IsAdmin(message.From.Id))
            {
                await botClient.SendMessage(chatId: chatId,
                    "⛔ Команда доступна только администратору.", cancellationToken: ct);
                return;
            }

            string report = await BuildStatusReportAsync(dbContext, ct);
            await botClient.SendMessage(chatId: chatId,
                text: report, parseMode: ParseMode.Html, cancellationToken: ct);
            return;
        }

        // eph-17: admin-only usage analytics
        if (text.StartsWith("/admin", StringComparison.OrdinalIgnoreCase))
        {
            if (!_adminOptions.IsAdmin(message.From.Id))
            {
                await botClient.SendMessage(chatId: chatId,
                    "⛔ Команда доступна только администратору.", cancellationToken: ct);
                return;
            }

            string stats = await BuildAdminStatsTextAsync(analytics, AnalyticsPeriod.Day, ct);
            await botClient.SendMessage(chatId: chatId,
                text: stats, parseMode: ParseMode.Html,
                replyMarkup: BotKeyboards.AdminStatsMenu(AnalyticsPeriod.Day), cancellationToken: ct);
            return;
        }

        // Check if user is currently entering a filter value (own filters / range / text)
        if (draft != null && draft.AwaitingFilterInput.HasValue)
        {
            await HandleFilterTextInputAsync(botClient, dbContext, analytics, chatId, message.From.Id, draft, text, ct);
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
        IKufarApiClient kufarClient,
        IUsageAnalytics analytics,
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

        // eph-17: admin analytics periods
        if (data.StartsWith("admst_"))
        {
            if (!_adminOptions.IsAdmin(userId))
            {
                await botClient.SendMessage(chatId: chatId,
                    "⛔ Команда доступна только администратору.", cancellationToken: ct);
                return;
            }

            AnalyticsPeriod period = data switch
            {
                "admst_w" => AnalyticsPeriod.Week,
                "admst_m" => AnalyticsPeriod.Month,
                _ => AnalyticsPeriod.Day
            };

            string stats = await BuildAdminStatsTextAsync(analytics, period, ct);
            await EditOrSendMessageAsync(botClient, chatId, messageId, stats, BotKeyboards.AdminStatsMenu(period), ct);
            return;
        }

        // eph-17: tracked "open listing" proxy (click observable in Bot API — plain URL buttons are not)
        if (data.StartsWith("open_") && long.TryParse(data.Substring(5), out long openAdId))
        {
            await analytics.RecordAsync(userId, UsageEventTypes.ListingLinkClicked,
                new { target = $"ad:{openAdId}" }, ct);

            // Prefer the real Kufar link stored by favorites tracking.
            string adLink = $"https://www.kufar.by/item/{openAdId}";
            string title = $"Объявление #{openAdId}";
            var watched = await dbContext.WatchedAds
                .FirstOrDefaultAsync(w => w.UserId == userId && w.AdId == openAdId, ct);
            if (watched != null)
            {
                if (!string.IsNullOrWhiteSpace(watched.AdLink)) adLink = watched.AdLink;
                if (!string.IsNullOrWhiteSpace(watched.Title)) title = watched.Title;
            }

            await botClient.SendMessage(
                chatId: chatId,
                text: $"📖 <b>{System.Net.WebUtility.HtmlEncode(title)}</b>",
                parseMode: ParseMode.Html,
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithUrl("Открыть на Kufar", adLink).Primary() },
                    new[] { InlineKeyboardButton.WithCallbackData("⬅️ Главное меню", "menu_main") }
                }),
                cancellationToken: ct);
            return;
        }

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

        // ===================== Favorites (eph-8b) =====================
        if (data == "menu_fav")
        {
            var favorites = await dbContext.WatchedAds
                .Where(w => w.UserId == userId)
                .OrderByDescending(w => w.CreatedAtUtc)
                .ToListAsync(ct);

            string text = favorites.Count == 0
                ? "⭐ <b>Избранное</b>\n\nПока пусто. Добавляйте объявления кнопкой «⭐ В избранное» под новыми объявлениями — бот будет следить за ценой и исчезновением."
                : $"⭐ <b>Избранное</b> — {favorites.Count} объявл.\n\nОтслеживается снижение цены и удаление с Kufar.";
            await EditOrSendMessageAsync(botClient, chatId, messageId, text, BotKeyboards.FavoritesMenu(favorites), ct);
            return;
        }

        if (data.StartsWith("fav_") && long.TryParse(data.Substring(4), out long favAdId))
        {
            var existing = await dbContext.WatchedAds
                .FirstOrDefaultAsync(w => w.UserId == userId && w.AdId == favAdId, ct);

            if (existing != null)
            {
                await EditOrSendMessageAsync(botClient, chatId, messageId,
                    $"⭐ <b>Уже в избранном</b>\n\n{System.Net.WebUtility.HtmlEncode(existing.Title)}\n" +
                    (existing.LastPriceByn.HasValue ? $"💰 {existing.LastPriceByn.Value:0.##} BYN\n" : "") +
                    $"\nСледим за ценой и наличием.",
                    new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("⭐ Избранное", "menu_fav") },
                        new[] { InlineKeyboardButton.WithCallbackData("⬅️ В главное меню", "menu_main") }
                    }),
                    ct);
                return;
            }

            Advertisement? details = await kufarClient.GetAdDetailsAsync(favAdId, ct);
            if (details == null)
            {
                await EditOrSendMessageAsync(botClient, chatId, messageId,
                    "❌ Объявление недоступно на Kufar — добавить в избранное нельзя.",
                    new InlineKeyboardMarkup(new[] { InlineKeyboardButton.WithCallbackData("⬅️ В главное меню", "menu_main") }),
                    ct);
                return;
            }

            dbContext.WatchedAds.Add(new WatchedAdEntity
            {
                UserId = userId,
                AdId = favAdId,
                Title = details.Title,
                AdLink = details.AdLink,
                ImageUrl = details.ImageUrl,
                Location = details.Location,
                LastPriceByn = details.PriceByn,
                LastCheckedUtc = DateTime.UtcNow,
                NotifiedMissing = false,
                CreatedAtUtc = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync(ct);

            await TryRecordFeatureAsync(analytics, userId, UsageFeatures.Favorite, $"ad:{favAdId}", ct);

            await EditOrSendMessageAsync(botClient, chatId, messageId,
                $"✅ <b>Добавлено в избранное!</b>\n\n" +
                $"<b>{System.Net.WebUtility.HtmlEncode(details.Title)}</b>\n" +
                $"💰 {System.Net.WebUtility.HtmlEncode(details.FormattedPrice)}\n" +
                $"📍 {System.Net.WebUtility.HtmlEncode(details.Location)}\n\n" +
                "Следим за ценой и наличием — сообщим об изменениях.",
                new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("📖 Открыть объявление", $"open_{favAdId}").Primary() },
                    new[] { InlineKeyboardButton.WithCallbackData("⭐ Избранное", "menu_fav") },
                    new[] { InlineKeyboardButton.WithCallbackData("⬅️ В главное меню", "menu_main") }
                }),
                ct);
            return;
        }

        if (data.StartsWith("unfav_") && long.TryParse(data.Substring(6), out long unfavAdId))
        {
            var removed = await dbContext.WatchedAds
                .Where(w => w.UserId == userId && w.AdId == unfavAdId)
                .ExecuteDeleteAsync(ct);

            if (removed > 0)
            {
                _logger.LogInformation("[BotEngine] Unfavorited ad AdId={AdId} for User={UserId}", unfavAdId, userId);
            }

            var favorites = await dbContext.WatchedAds
                .Where(w => w.UserId == userId)
                .OrderByDescending(w => w.CreatedAtUtc)
                .ToListAsync(ct);

            string text = favorites.Count == 0
                ? "⭐ <b>Избранное</b>\n\nПусто — всё убрано."
                : $"⭐ <b>Избранное</b> — {favorites.Count} объявл.";
            await EditOrSendMessageAsync(botClient, chatId, messageId, text, BotKeyboards.FavoritesMenu(favorites), ct);
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

        // Preview: live example ads matching the current draft (eph-11)
        if (data == "fpreview")
        {
            var draft = _sessionStore.GetOrCreateDraft(userId);
            if (!draft.SubcategoryId.HasValue)
            {
                await EditOrSendMessageAsync(botClient, chatId, messageId,
                    "⚠️ Сначала выберите подкатегорию поиска.",
                    BotKeyboards.SearchDraftMenu(draft), ct);
                return;
            }

            await SendDraftPreviewAsync(botClient, chatId, messageId, kufarClient, draft, ct);

            // eph-17: preview is a distinct feature worth measuring
            await TryRecordFeatureAsync(analytics, userId, UsageFeatures.Preview,
                draft.SubcategoryId.HasValue ? $"sub:{draft.SubcategoryId.Value}" : null, ct);
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
            if (draft.AvailableSpecs.Count == 0)
            {
                await EditOrSendMessageAsync(botClient, chatId, messageId,
                    "⚠️ Не удалось загрузить параметры подкатегории с Kufar.\n\nПопробуйте ещё раз:",
                    new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("🔄 Повторить", "fmenu").Primary() },
                        new[] { InlineKeyboardButton.WithCallbackData("⬅️ К черновику", "menu_draft") }
                    }), ct);
                return;
            }

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
                await HandleSingleOptionChoiceAsync(botClient, chatId, messageId, userId, analytics, singleSpecIndex, optionIndex, ct);
            }
            return;
        }

        // Multi value toggle
        if (data.StartsWith("fmv_"))
        {
            var parts = data.Split('_');
            if (parts.Length == 3 && int.TryParse(parts[1], out int multiSpecIndex) && int.TryParse(parts[2], out int multiOptionIndex))
            {
                await ToggleMultiOptionAsync(botClient, chatId, messageId, userId, analytics, multiSpecIndex, multiOptionIndex, ct);
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

        // Clear all site-parameter filters at once
        if (data == "fclrall")
        {
            var draft = _sessionStore.GetOrCreateDraft(userId);
            draft.Filters.RemoveAll(f => f.Kind is
                SavedFilterKind.Bool or SavedFilterKind.Single or SavedFilterKind.Multi
                or SavedFilterKind.Range or SavedFilterKind.Text);
            if (draft.Wizard != null)
            {
                draft.Wizard.SpecIndex = -1;
                draft.Wizard.SelectedValuesPage = 0;
            }

            await EditOrSendMessageAsync(botClient, chatId, messageId,
                "🧹 Все фильтры сайта сброшены.\n\n" + RenderFiltersMenuText(draft),
                BotKeyboards.FiltersMenu(draft), ct);
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

        if (data is "fown_kw_inc" or "fown_kw_exc" or "fown_query")
        {
            var draft = _sessionStore.GetOrCreateDraft(userId);
            draft.AwaitingFilterInput = data switch
            {
                "fown_kw_inc" => -10,
                "fown_kw_exc" => -11,
                _ => -12
            };
            draft.AwaitingPriceInput = PriceInputMode.None;

            string hint = data switch
            {
                "fown_kw_inc" => "🔔 Отправьте ключевые слова, <b>одно из которых</b> должно быть в названии/описании (через запятую):\n\n<i>Например: <code>gtx, 1060</code></i>",
                "fown_kw_exc" => "🚫 Отправьте ключевые слова, которых <b>не должно быть</b> в объявлении (через запятую):\n\n<i>Например: <code>битая, обмен</code></i>",
                _ => "🔎 Отправьте текст поиска (по словам). Все слова должны быть в названии/описании:\n\n<i>Например: <code>iphone 13 128</code></i>"
            };

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

            // eph-17: own-filter features (selecting "any" just resets — still a feature use)
            await TryRecordFeatureAsync(analytics, userId, UsageFeatures.OwnSellerType, data.TrimStart('f', 'o', 'w', 'n', '_'), ct);

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

            if (int.TryParse(data.Substring("fown_age_".Length), out int ageHours) && ageHours > 0)
            {
                await TryRecordFeatureAsync(analytics, userId, UsageFeatures.OwnMaxAge, $"{ageHours}h", ct);
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

            // eph-14: full edit — the draft screen covers city, prices and filters
            // («✅ Сохранить подписку» applies everything, the subcategory stays unchanged)
            await EditOrSendMessageAsync(botClient, chatId, messageId,
                $"✏️ <b>Редактирование подписки #{search.Id}</b>\n\n{FormatDraftSummary(draft)}",
                BotKeyboards.SearchDraftMenu(draft), ct);
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

            bool subcategoryChanged = draft.SubcategoryId != subId;
            draft.SubcategoryId = subId;
            draft.SubcategoryTitle = subcategory?.Name ?? $"Подкатегория {subId}";
            if (category != null)
            {
                draft.CategoryId = category.Id;
                draft.CategoryTitle = category.Name;
            }

            if (subcategoryChanged)
            {
                // site-parameter filters belong to the previous subcategory — drop them,
                // keeping the bot's own filters (keywords, seller, age, query)
                ResetServerFiltersOnSubcategoryChange(draft);
            }
            // warm up the dispatch cache so «Фильтры» opens instantly
            PrefetchFilterSpecs(draft);

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

            // eph-14: editing an existing subscription — apply city / price / filters,
            // subcategory stays as-is (changing the category is not an "edit").
            if (draft.EditingSearchId.HasValue)
            {
                int editingId = draft.EditingSearchId.Value;
                var current = await subscriptionService.GetSearchByIdAsync(editingId, userId, ct);
                if (current == null)
                {
                    _sessionStore.ResetDraft(userId);
                    await EditOrSendMessageAsync(botClient, chatId, messageId,
                        "⚠️ Подписка не найдена или была удалена.", BotKeyboards.MainMenu(), ct);
                    return;
                }

                if (draft.SubcategoryId != current.SubcategoryId)
                {
                    draft.SubcategoryId = current.SubcategoryId;
                    draft.SubcategoryTitle = current.SubcategoryTitle;
                    await EditOrSendMessageAsync(botClient, chatId, messageId,
                        "⚠️ Категорию менять нельзя — создайте новую подписку.\n\nИзменены только город, цены и фильтры:",
                        BotKeyboards.SearchDraftMenu(draft), ct);
                    return;
                }

                bool ok = await subscriptionService.UpdateSearchCoreAsync(
                    editingId,
                    userId,
                    draft.CityId,
                    draft.CityName,
                    draft.MinPriceByn,
                    draft.MaxPriceByn,
                    SavedFilter.Serialize(draft.Filters),
                    ct);

                _sessionStore.ResetDraft(userId);

                if (ok)
                {
                    await EditOrSendMessageAsync(botClient, chatId, messageId,
                        $"✅ <b>Подписка #{editingId} обновлена!</b>",
                        new InlineKeyboardMarkup(new[] { InlineKeyboardButton.WithCallbackData("📋 К подписке", $"subview_{editingId}") }), ct);
                }
                else
                {
                    await EditOrSendMessageAsync(botClient, chatId, messageId,
                        "⚠️ Не удалось обновить подписку.", BotKeyboards.MainMenu(), ct);
                }
                return;
            }

            var existingSearches = await subscriptionService.GetUserSearchesAsync(userId, ct);
            if (existingSearches.Count(s => !s.IsPaused) >= MaxActiveSubscriptions)
            {
                // eph-17: funnel-exit point — user hit the subscription cap
                await analytics.RecordAsync(userId, UsageEventTypes.LimitReached,
                    new { target = $"user:{userId}", limit = MaxActiveSubscriptions, active = existingSearches.Count(s => !s.IsPaused) }, ct);

                await EditOrSendMessageAsync(botClient, chatId, messageId,
                    $"🚧 <b>Достигнут лимит активных подписок ({MaxActiveSubscriptions}).</b>\n\n" +
                    "Приостановите или удалите существующие подписки в разделе «Мои подписки», чтобы создать новую.",
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
                _sessionStore.GetOrCreateDraft(userId).SubscriptionsPage = 0;
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
                $"📋 <b>Ваши подписки ({searches.Count}):</b>\n\n" +
                "Нажмите на подписку для управления (пауза / удаление):",
                BotKeyboards.SubscriptionsListMenu(searches, _sessionStore.GetOrCreateDraft(userId).SubscriptionsPage), ct);
            return;
        }

        // Subscriptions list paging
        if (data.StartsWith("slp_") && int.TryParse(data.Substring(4), out int subsPage))
        {
            var searches = await subscriptionService.GetUserSearchesAsync(userId, ct);
            if (searches.Count == 0)
            {
                await EditOrSendMessageAsync(botClient, chatId, messageId,
                    "📋 <b>У вас пока нет сохраненных подписок.</b>",
                    BotKeyboards.MainMenu(), ct);
                return;
            }

            var draft = _sessionStore.GetOrCreateDraft(userId);
            draft.SubscriptionsPage = Math.Max(0, subsPage);

            await EditOrSendMessageAsync(botClient, chatId, messageId,
                $"📋 <b>Ваши подписки ({searches.Count}):</b>\n\n" +
                "Нажмите на подписку для управления (пауза / удаление):",
                BotKeyboards.SubscriptionsListMenu(searches, draft.SubscriptionsPage), ct);
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
            draft.AvailableSpecs = await LoadSubcategorySpecsAsync(draft, null, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[BotEngine] Failed to load filter specs for subcategory {SubId}", draft.SubcategoryId);
        }
    }

    /// <summary>Loads filter specs for the draft's subcategory (served from the client's category cache when warm).</summary>
    private async Task<List<FilterSpec>> LoadSubcategorySpecsAsync(
        UserSearchDraft draft,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? conditionalValues,
        CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var kufarClient = scope.ServiceProvider.GetRequiredService<IKufarApiClient>();
        // CategoryId is guaranteed when a subcategory was chosen; derive 16010 -> 16000 as a fallback
        int parentCategoryId = draft.CategoryId ?? (draft.SubcategoryId!.Value / 100 * 100);
        return (await kufarClient.GetFiltersForSubcategoryAsync(
            draft.SubcategoryId!.Value, parentCategoryId, conditionalValues, ct)).ToList();
    }

    /// <summary>Chosen values of already-set choice filters — drivers for conditional (type-level) parameters.</summary>
    private static Dictionary<string, IReadOnlyList<string>> ConditionalValuesFromDraft(UserSearchDraft draft)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var filter in draft.Filters)
        {
            if (filter.Kind is not (SavedFilterKind.Single or SavedFilterKind.Multi or SavedFilterKind.Bool) ||
                string.IsNullOrWhiteSpace(filter.Key) ||
                filter.Values.Count == 0)
            {
                continue;
            }

            if (result.TryGetValue(filter.Key, out IReadOnlyList<string>? existing))
            {
                var merged = existing.Concat(filter.Values).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                result[filter.Key] = merged;
            }
            else
            {
                result[filter.Key] = filter.Values;
            }
        }

        return result;
    }

    /// <summary>
    /// Rebuilds the spec list with the current chosen values (activating the type-conditional filters)
    /// and re-renders the spec screen, keeping focus on the same filter by key.
    /// Returns the (possibly shifted) index of the spec.
    /// </summary>
    private async Task<int> RefreshSpecsAndLocateAsync(UserSearchDraft draft, FilterSpec spec, int oldSpecIndex, CancellationToken ct)
    {
        if (draft.SubcategoryId == null || draft.Wizard == null)
        {
            return oldSpecIndex;
        }

        try
        {
            var fresh = await LoadSubcategorySpecsAsync(draft, ConditionalValuesFromDraft(draft), ct);
            if (fresh.Count != draft.AvailableSpecs.Count)
            {
                draft.AvailableSpecs = fresh;
                int idx = fresh.FindIndex(s => string.Equals(s.Key, spec.Key, StringComparison.Ordinal));
                if (idx >= 0)
                {
                    draft.Wizard.SpecIndex = idx;
                    return idx;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[BotEngine] Failed to refresh conditional specs for subcategory {SubId}", draft.SubcategoryId);
        }

        return oldSpecIndex;
    }

    /// <summary>
    /// Fire-and-forget prefetch of the whole category dispatch (one request, cached in the client)
    /// so the first open of «Фильтры» shows them instantly.
    /// </summary>
    private void PrefetchFilterSpecs(UserSearchDraft draft)
    {
        if (draft.CategoryId == null)
        {
            return;
        }

        int parentCategoryId = draft.CategoryId.Value;

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var kufarClient = scope.ServiceProvider.GetRequiredService<IKufarApiClient>();
                await kufarClient.GetFiltersForCategoryAsync(parentCategoryId);
                _logger.LogInformation("[BotEngine] Filter specs prefetched for parent category {ParentId}", parentCategoryId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[BotEngine] Prefetch of filter specs for parent {ParentId} failed", parentCategoryId);
            }
        });
    }

    /// <summary>
    /// Site-parameter filters belong to the previous subcategory — cleared on switch.
    /// The bot's own filters (keywords, seller type, age, query) are subcategory-independent
    /// and stay so the user doesn't enter them again.
    /// </summary>
    private static void ResetServerFiltersOnSubcategoryChange(UserSearchDraft draft)
    {
        draft.AvailableSpecs.Clear();
        draft.Wizard = null;
        draft.AwaitingFilterInput = null;

        draft.Filters.RemoveAll(f => f.Kind is not
            (SavedFilterKind.KeywordsInclude or SavedFilterKind.KeywordsExclude
                or SavedFilterKind.SellerType or SavedFilterKind.MaxAgeHours or SavedFilterKind.Query));
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
        IUsageAnalytics analytics,
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
                    UrlName = spec.UrlName,
                    Title = spec.Title,
                    Values = new[] { option.Value }
                });

                // eph-17: on-state selection counts as a preset use
                await TryRecordPresetAsync(analytics, userId, spec, option.Value, ct);
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
                    UrlName = spec.UrlName,
                    Title = spec.Title,
                    Values = new[] { option.Value }
                });

                await TryRecordPresetAsync(analytics, userId, spec, option.Value, ct);
            }
        }

        int updatedSpecIndex = await RefreshSpecsAndLocateAsync(draft, spec, specIndex, ct);

        await EditOrSendMessageAsync(botClient, chatId, messageId,
            FilterWizardUI.RenderSpecScreen(draft, updatedSpecIndex), BotKeyboards.FilterOptionsMenu(draft, updatedSpecIndex), ct);
    }

    private async Task ToggleMultiOptionAsync(
        ITelegramBotClient botClient,
        long chatId,
        int messageId,
        long userId,
        IUsageAnalytics analytics,
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

        bool removed = values.Contains(option.Value, StringComparer.OrdinalIgnoreCase);
        if (removed)
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
            UrlName = spec.UrlName,
            Title = spec.Title,
            Values = values
        });

        if (!removed)
        {
            await TryRecordPresetAsync(analytics, userId, spec, option.Value, ct);
        }

        int updatedSpecIndex = await RefreshSpecsAndLocateAsync(draft, spec, specIndex, ct);

        await EditOrSendMessageAsync(botClient, chatId, messageId,
            FilterWizardUI.RenderSpecScreen(draft, updatedSpecIndex), BotKeyboards.FilterOptionsMenu(draft, updatedSpecIndex), ct);
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
            // eph-14: apply everything (city / prices included) so wizard edits don't
            // lose draft changes; keeps the old filters-only edit as a special case of it.
            bool ok = await subscriptionService.UpdateSearchCoreAsync(
                draft.EditingSearchId.Value,
                userId,
                draft.CityId,
                draft.CityName,
                draft.MinPriceByn,
                draft.MaxPriceByn,
                SavedFilter.Serialize(draft.Filters),
                ct);
            _sessionStore.ResetDraft(userId);

            if (ok)
            {
                await EditOrSendMessageAsync(botClient, chatId, messageId,
                    $"✅ Подписка #{draft.EditingSearchId.Value} обновлена." +
                    (SavedFilter.Serialize(draft.Filters) == "[]" ? "\n(фильтры сброшены — подписка без фильтров)" : ""),
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

    /// <summary>
    /// eph-11: live preview of the current draft — fetches the first page of Kufar results
    /// with URL filters applied server-side, then re-checks every draft filter client-side
    /// (exactly as subscription delivery does) and shows the top matches.
    /// </summary>
    private async Task SendDraftPreviewAsync(
        ITelegramBotClient botClient,
        long chatId,
        int messageId,
        IKufarApiClient kufarClient,
        UserSearchDraft draft,
        CancellationToken ct)
    {
        var urlFilters = UrlFiltersBuilder.Build(draft.Filters);
        var criteria = new KufarSearchCriteria
        {
            CategoryId = draft.SubcategoryId,
            AreaId = draft.CityId,
            MinPriceByn = draft.MinPriceByn,
            MaxPriceByn = draft.MaxPriceByn,
            UrlFilters = urlFilters.Count > 0 ? urlFilters : null,
            Size = 50,
            Sort = "lst.d"
        };

        KufarSearchResult result;
        try
        {
            result = await kufarClient.SearchAsync(criteria, ct);
        }
        catch (Exception ex) when (urlFilters.Count > 0)
        {
            // Unknown/invalid filter names make Kufar reject the whole request (422) —
            // retry without URL filters; the complete filter set is still applied client-side.
            _logger.LogWarning(ex, "[BotEngine] Preview: Kufar rejected URL filters, retrying without them.");
            result = await kufarClient.SearchAsync(criteria with { UrlFilters = null }, ct);
        }

        // Client-side matching mirrors subscription delivery (SearchMatchingExtensions.Matches):
        // prices + every SavedFilter incl. the bot's own (query, keywords, seller, age).
        var matcher = new UserSearchEntity
        {
            UserId = 0,
            SubcategoryId = draft.SubcategoryId!.Value,
            SubcategoryTitle = draft.SubcategoryTitle ?? string.Empty,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            MinPriceByn = draft.MinPriceByn,
            MaxPriceByn = draft.MaxPriceByn,
            FiltersJson = SavedFilter.Serialize(draft.Filters)
        };

        var nowUtc = DateTime.UtcNow;
        var matches = result.Ads
            .Where(ad => matcher.Matches(ad))
            .OrderByDescending(ad => ad.ListTimeUtc)
            .ToList();
        var top = matches.Take(5).ToList();

        string header = $"👁 <b>Предпросмотр поиска</b>\n\n{FormatDraftSummary(draft)}\n";

        if (matches.Count == 0)
        {
            string siteTotal = result.Total is > 0
                ? $"\n⚠️ Сайт выдаёт {result.Total} объявл., но ни одно не проходит все фильтры."
                : "";

            await EditOrSendMessageAsync(botClient, chatId, messageId,
                $"{header}❌ <b>Ничего не найдено</b> по текущим критериям{siteTotal}\n\n<i>Попробуйте расширить диапазон цен или убрать часть фильтров.</i>",
                new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("⬅️ К черновику", "menu_draft") }
                }), ct);
            return;
        }

        var lines = new List<string>
        {
            header + $"✅ Подходящих объявлений (первая страница выдачи): <b>{matches.Count}</b>\n"
        };

        for (int i = 0; i < top.Count; i++)
        {
            var ad = top[i];
            int n = i + 1;
            lines.Add(
                $"<a href=\"{ad.AdLink}\">{n}. {FilterWizardUI.Escape(ad.Title)}</a>\n" +
                $"💰 {FilterWizardUI.Escape(ad.FormattedPrice)} • 📍 {FilterWizardUI.Escape(ad.Location)} • 🕒 {FilterWizardUI.Escape(FormatAgeShort(ad.ListTimeUtc, nowUtc))}");
        }

        // Double-check: an inline URL button per result — matches the favorites-list UX.
        await EditOrSendMessageAsync(botClient, chatId, messageId,
            string.Join("\n\n", lines), BotKeyboards.PreviewResultsMenu(top), ct);
    }

    private static string FormatAgeShort(DateTime listTimeUtc, DateTime nowUtc)
    {
        TimeSpan age = nowUtc - listTimeUtc;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        if (age.TotalHours < 1)
        {
            return $"{Math.Max(1, (int)age.TotalMinutes)} мин назад";
        }

        if (age.TotalHours < 24)
        {
            return $"{(int)age.TotalHours} ч назад";
        }

        int days = (int)age.TotalDays;
        return days == 1 ? "1 дн назад" : $"{days} дн назад";
    }

    // ===================== Usage analytics (eph-17) =====================

    /// <summary>Records a FeatureUsed event; analytics failures must never break UI handling.</summary>
    private async Task TryRecordFeatureAsync(
        IUsageAnalytics analytics, long userId, string feature, string? target, CancellationToken ct)
    {
        try
        {
            await analytics.RecordAsync(userId, UsageEventTypes.FeatureUsed,
                new { feature, target }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[BotEngine] FeatureUsed analytics failed for User={UserId}", userId);
        }
    }

    /// <summary>Records a PresetSelected event for a browser-site filter choice.</summary>
    private async Task TryRecordPresetAsync(
        IUsageAnalytics analytics, long userId, FilterSpec spec, string? value, CancellationToken ct)
    {
        try
        {
            string preset = $"{spec.Title} = {value}";
            await analytics.RecordAsync(userId, UsageEventTypes.PresetSelected,
                new { preset, target = spec.UrlName ?? spec.Key }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[BotEngine] PresetSelected analytics failed for User={UserId}", userId);
        }
    }

    /// <summary>eph-17: usage statistics report for the /admin command.</summary>
    private async Task<string> BuildAdminStatsTextAsync(
        IUsageAnalytics analytics, AnalyticsPeriod period, CancellationToken ct)
    {
        UsageReport r = await analytics.GetStatsAsync(period, ct);

        string periodTitle = period switch
        {
            AnalyticsPeriod.Day => "сутки",
            AnalyticsPeriod.Week => "неделя",
            _ => "месяц"
        };

        string Escape(string s) => System.Net.WebUtility.HtmlEncode(s);
        string TopLines(IReadOnlyList<TopUsageItem> items) =>
            items.Count == 0
                ? "        нет данных"
                : string.Join("\n", items.Select(i => $"        • {Escape(i.Key)} — {i.Count}"));

        return
            "📊 <b>Аналитика использования</b>\n" +
            $"Период: <b>{periodTitle}</b> (UTC)\n\n" +
            "👥 Новые пользователи за период: <b>" + r.NewUsers + "</b>\n" +
            "⚡ Активные пользователи: " +
            $"<b>{r.ActiveUsers1d}</b>/24ч, <b>{r.ActiveUsers7d}</b>/7д, <b>{r.ActiveUsers30d}</b>/30д\n" +
            "🔎 Активных подписок сейчас: <b>" + r.ActiveSearches + "</b>\n" +
            "📨 Уведомлений доставлено за период: <b>" + r.NotificationsSent + "</b>\n\n" +
            "⭐ <b>Топ функций:</b>\n" + TopLines(r.TopFeatures) + "\n\n" +
            "🎛 <b>Топ пресетов (фильтры сайта):</b>\n" + TopLines(r.TopPresets) + "\n\n" +
            "🚧 Достигли лимита подписок: " +
            (r.LimitSharePercent.HasValue ? $"<b>{r.LimitSharePercent.Value:0.#}%</b> активных" : "нет данных") + "\n" +
            "🚀 Конверсия запуск → первый поиск: " +
            (r.ConversionPercent.HasValue
                ? $"<b>{r.ConversionPercent.Value:0.#}%</b> ({r.ConvertedToFirstSearch} из {r.BotStartsInPeriod})"
                : "нет данных") + "\n\n" +
            "<i>События записываются с 29.10.2026 — более ранние периоды могут быть неполными.</i>";
    }

    /// <summary>eph-12: compact service dashboard for the /status admin command.</summary>
    private async Task<string> BuildStatusReportAsync(AppDbContext dbContext, CancellationToken ct)
    {
        DateTime nowUtc = DateTime.UtcNow;

        int userCount = await dbContext.Users.CountAsync(ct);
        int searchCount = await dbContext.UserSearches.CountAsync(ct);
        int activeSearchCount = await dbContext.UserSearches.CountAsync(s => !s.IsPaused, ct);

        var feeds = await dbContext.PollingFeeds.AsNoTracking().ToListAsync(ct);
        var errorFeeds = feeds.Where(f => f.ConsecutiveErrors > 0)
            .OrderByDescending(f => f.ConsecutiveErrors)
            .ToList();
        int staleFeeds = feeds.Count(f =>
            f.LastPolledAtUtc == null ||
            f.LastPolledAtUtc.Value < nowUtc - TimeSpan.FromMinutes(15));

        int outboxPending = await dbContext.NotificationOutbox.CountAsync(n => n.Status == 0 || n.Status == 3, ct);
        int outboxSending = await dbContext.NotificationOutbox.CountAsync(n => n.Status == 1, ct);
        int outboxSentToday = await dbContext.NotificationOutbox.CountAsync(
            n => n.Status == 2 && n.SentAtUtc >= nowUtc - TimeSpan.FromHours(24), ct);
        int outboxDead = await dbContext.NotificationOutbox.CountAsync(n => n.Status == 4, ct);

        int deliveredToday = await dbContext.DeliveredAds.CountAsync(
            d => d.DeliveredAtUtc >= nowUtc - TimeSpan.FromHours(24), ct);
        int watchedCount = await dbContext.WatchedAds.CountAsync(ct);

        var lines = new List<string>
        {
            "📊 <b>Статус бота</b>",
            "",
            $"👥 Пользователей: <b>{userCount}</b>",
            $"🔍 Подписок: <b>{searchCount}</b> (активных: {activeSearchCount})",
            $"📡 Фидов: <b>{feeds.Count}</b> (устаревших: {staleFeeds}, с ошибками: {errorFeeds.Count})",
            $"📦 Очередь уведомлений: в ожидании <b>{outboxPending}</b>, в отправке {outboxSending}, доставлено за 24ч: {outboxSentToday}, dead-letter: {outboxDead}",
            $"📬 Доставок за 24ч: <b>{deliveredToday}</b>",
            $"⭐ Объявлений в избранном: <b>{watchedCount}</b>"
        };

        foreach (var feed in errorFeeds.Take(3))
        {
            lines.Add($"  ⚠️ фид #{feed.Id} (подк. {feed.SubcategoryId}, город {feed.CityId?.ToString() ?? "—"}): {feed.ConsecutiveErrors} ошибок подряд");
        }

        lines.Add("");
        lines.Add($"🕐 Проверено (UTC): {nowUtc:yyyy-MM-dd HH:mm:ss}");
        return string.Join("\n", lines);
    }

    private async Task HandleFilterTextInputAsync(
        ITelegramBotClient botClient,
        AppDbContext dbContext,
        IUsageAnalytics analytics,
        long chatId,
        long userId,
        UserSearchDraft draft,
        string text,
        CancellationToken ct)
    {
        int pending = draft.AwaitingFilterInput!.Value;
        draft.AwaitingFilterInput = null;

        if (pending == -12)
        {
            var words = FilterWizardUI.ParseQueryInput(text);
            draft.Filters.RemoveAll(f => f.Kind == SavedFilterKind.Query);

            if (words.Count > 0)
            {
                draft.Filters.Add(new SavedFilter
                {
                    Kind = SavedFilterKind.Query,
                    Key = "own_query",
                    Title = "Текст поиска",
                    Values = words
                });

                await TryRecordFeatureAsync(analytics, userId, UsageFeatures.OwnQuery, string.Join(" ", words), ct);

                await botClient.SendMessage(chatId: chatId,
                    $"✅ Текст поиска: <b>{FilterWizardUI.Escape(string.Join(" ", words))}</b>",
                    parseMode: ParseMode.Html, cancellationToken: ct);
            }
            else
            {
                await botClient.SendMessage(chatId: chatId, "🧹 Текст поиска удалён.", cancellationToken: ct);
            }

            if (draft.EditingSearchId.HasValue)
            {
                await botClient.SendMessage(chatId: chatId,
                    "🎛 <b>Свои фильтры</b>", parseMode: ParseMode.Html,
                    replyMarkup: BotKeyboards.OwnFiltersMenu(draft), cancellationToken: ct);
            }
            else
            {
                await botClient.SendMessage(chatId: chatId,
                    $"⚙️ <b>Настройка параметров поиска:</b>\n\n{FormatDraftSummary(draft)}",
                    parseMode: ParseMode.Html, replyMarkup: BotKeyboards.SearchDraftMenu(draft), cancellationToken: ct);
            }
            return;
        }

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

                await TryRecordFeatureAsync(analytics, userId,
                    pending == -10 ? UsageFeatures.OwnKeywordsInclude : UsageFeatures.OwnKeywordsExclude,
                    string.Join(",", keywords), ct);

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
                UrlName = spec.UrlName,
                Title = spec.Title,
                Min = parsed.Min,
                Max = parsed.Max
            });

            await TryRecordPresetAsync(analytics, userId, spec, $"{parsed.Min:0.#}-{parsed.Max:0.#}", ct);

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
                UrlName = spec.UrlName,
                Title = spec.Title,
                Values = new[] { parsed.TextValue! }
            });

            await TryRecordPresetAsync(analytics, userId, spec, parsed.TextValue, ct);

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

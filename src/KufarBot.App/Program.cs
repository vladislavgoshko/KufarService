using System;
using System.IO;
using KufarBot.App.Telegram;
using KufarBot.Core.Http;
using KufarBot.Infrastructure.Persistence;
using KufarBot.Infrastructure.Polling;
using KufarBot.Infrastructure.TelegramClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;

namespace KufarBot.App;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // Database configuration
        var dbPath = builder.Configuration["DatabasePath"];
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            dbPath = Path.Combine(AppContext.BaseDirectory, "kufar_bot.db");
        }

        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite($"DataSource={dbPath}"));

        // Telegram Bot Client & Sender
        var botToken = builder.Configuration["TelegramBotToken"];
        if (string.IsNullOrWhiteSpace(botToken))
        {
            throw new InvalidOperationException(
                "TelegramBotToken is not configured. " +
                "For local development run: dotnet user-secrets set \"TelegramBotToken\" \"<your-token>\" " +
                "(project: src/KufarBot.App). For other environments set it in configuration/environment.");
        }

        builder.Services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(botToken));
        builder.Services.AddSingleton<ITelegramSender, TelegramBotClientSender>();

        // Kufar API Client
        builder.Services.AddHttpClient<IKufarApiClient, KufarApiClient>();

        // Domain & Application Services
        builder.Services.AddSingleton<UserSessionStore>();
        builder.Services.AddSingleton<ICategoryService, CachedCategoryService>();
        builder.Services.AddScoped<ISearchSubscriptionService, SearchSubscriptionService>();

        // Worker options from configuration (appsettings: sections "Polling" and "Outbox")
        builder.Services.Configure<FeedPollingOptions>(
            builder.Configuration.GetSection(FeedPollingOptions.SectionName));
        builder.Services.Configure<OutboxDeliveryOptions>(
            builder.Configuration.GetSection(OutboxDeliveryOptions.SectionName));
        builder.Services.Configure<WatchedAdsOptions>(
            builder.Configuration.GetSection(WatchedAdsOptions.SectionName));
        builder.Services.Configure<AdminOptions>(
            builder.Configuration.GetSection(AdminOptions.SectionName));
        builder.Services.AddSingleton<FeedPollingOptions>(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<FeedPollingOptions>>().Value);
        builder.Services.AddSingleton<OutboxDeliveryOptions>(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OutboxDeliveryOptions>>().Value);
        builder.Services.AddSingleton<WatchedAdsOptions>(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WatchedAdsOptions>>().Value);
        builder.Services.AddSingleton<AdminOptions>(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AdminOptions>>().Value);

        // Background Workers
        builder.Services.AddHostedService<FeedPollingWorker>();
        builder.Services.AddHostedService<OutboxDeliveryWorker>();
        builder.Services.AddHostedService<WatchedAdsWorker>();
        builder.Services.AddHostedService<BotEngine>();

        var host = builder.Build();

        // Database Initialization
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.Migrate();
        }

        await host.RunAsync();
    }
}

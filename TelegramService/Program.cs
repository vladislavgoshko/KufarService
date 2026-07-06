using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using TelegramService.CallbackHandlers;
using TelegramService.Services;
using Microsoft.Extensions.Configuration;


namespace TelegramService
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            // 1. Создаем коллекцию сервисов
            var services = new ServiceCollection();
            services.AddSingleton(new ApiSettings { BaseUrl = "http://localhost:5080" });


            // 2. Регистрация клиента бота
            services.AddSingleton<ITelegramBotClient>(sp =>
                new TelegramBotClient("7284945146:AAELFmnfF9Rsr9wjl-K_FwWvys8oNRBrxNA"));

            // 3. Регистрация сервиса уведомлений
            services.AddSingleton<AdNotificationService>();

            // 4. Регистрация всех обработчиков (Handlers)
            // Используем интерфейс IBotHandler, чтобы роутер мог получить их списком
            services.AddScoped<IBotHandler, StartCommand>();
            services.AddScoped<IBotHandler, BackToMenuCallbackHandler>();
            services.AddScoped<IBotHandler, CreateNewSearchCallbackHandler>();

            services.AddScoped<IBotHandler, SubcategoryCallbackHandler>(); 
            services.AddScoped<IBotHandler, StopTrackingCallbackHandler>();
            services.AddScoped<IBotHandler, CategoryCallbackHandler>();

            services.AddScoped<IBotHandler, MainCallbackHandler>();  // Должен быть последним, так как общий

            // 5. Регистрация ядра
            services.AddScoped<CategoryService>();
            services.AddScoped<UpdateRouter>();
            services.AddScoped<BotEngine>();

            // 6. Построение провайдера и запуск
            var serviceProvider = services.BuildServiceProvider();

            // Получаем BotEngine из контейнера и запускаем
            var botEngine = serviceProvider.GetRequiredService<BotEngine>();
            await botEngine.ListenForMessageAsync();
        }
    }
}

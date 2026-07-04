
using Scalar.AspNetCore;
using CoreAPI.Services;

namespace CoreAPI
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Настройка логирования
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.AddDebug();
            builder.Logging.SetMinimumLevel(LogLevel.Information);

            // Add services to the container.
            builder.Services.AddControllers();
            // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
            builder.Services.AddOpenApi();

            // Регистрация CategoryCacheService как Singleton
            builder.Services.AddSingleton<CategoryCacheService>();

            var app = builder.Build();

            // Инициализация кэша категорий при запуске приложения
            var categoryCache = app.Services.GetRequiredService<CategoryCacheService>();
            await categoryCache.InitializeAsync();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
            }

            app.MapScalarApiReference();
            app.UseHttpsRedirection();

            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }
    }
}

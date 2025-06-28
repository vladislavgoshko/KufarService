using Microsoft.Extensions.Logging;
using WebApplicationAPI.Services;
using WebApplicationAPI.Models.Parameter; // Make sure this is still needed for models
using System.Text.Json; // Make sure this is still needed for JSON handling

namespace WebApplicationAPI
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // --- SERVICE CONFIGURATION ---

            // Configure logging:
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            // builder.Logging.AddDebug();

            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // Register HttpClient for dependency injection.
            // When you call .AddHttpClient<TClient>(), HttpClient is configured correctly
            // with features like automatic retry, proper connection management, etc.
            // It will also create an HttpClient instance for ParameterMetadataService.
            builder.Services.AddHttpClient<ParameterService>();

            // Register your services as singletons.
            // ParameterMetadataService now manages its own caching and fetches data on demand.
            // CategoryService still loads data from a file, so it also benefits from being a singleton
            // to load data only once.
            builder.Services.AddSingleton<CategoryService>();
            builder.Services.AddSingleton<ParameterService>();

            var app = builder.Build();

            // --- DATA LOADING AT APPLICATION STARTUP (For CategoryService) ---
            // The ParameterMetadataService no longer loads from a file at startup.
            // It now loads data from the internet on demand and caches it.
            // So, we only need to explicitly load CategoryService data here if it's still file-based.
            using (var scope = app.Services.CreateScope())
            {
                var serviceProvider = scope.ServiceProvider;
                var startupLogger = serviceProvider.GetRequiredService<ILogger<Program>>();

                // 1. Loading data for CategoryService (if it's still file-based)
                try
                {
                    var categoryService = serviceProvider.GetRequiredService<CategoryService>();
                    // Assuming LoadCategoriesAsync still loads from a local file like "categories.json"
                    await categoryService.LoadCategoriesAsync();
                    startupLogger.LogInformation("Category data loaded successfully.");
                }
                catch (Exception ex)
                {
                    startupLogger.LogCritical(ex, "FATAL ERROR: Failed to load category data during application startup. Application will terminate.");
                    throw; // Terminate if essential data can't be loaded
                }

                // Removed the explicit call to parameterMetadataService.LoadParametersAsync("parameters.json");
                // as ParameterMetadataService now fetches data on demand from the internet.
                startupLogger.LogInformation("ParameterMetadataService will load data on demand.");
            }

            // --- HTTP REQUEST PIPELINE CONFIGURATION ---
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }
    }
}
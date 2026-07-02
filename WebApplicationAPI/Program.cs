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
            builder.Services.AddHttpClient<SearchAdService>();
            // Register your services as singletons.
            // ParameterMetadataService now manages its own caching and fetches data on demand.
            // CategoryService still loads data from a file, so it also benefits from being a singleton
            // to load data only once.
            builder.Services.AddSingleton<CategoryService>();
            builder.Services.AddSingleton<ParameterService>();

            var app = builder.Build();


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
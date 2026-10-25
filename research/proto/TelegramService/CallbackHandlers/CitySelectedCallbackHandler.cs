using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramService.Services;

namespace TelegramService.CallbackHandlers
{
    internal class CitySelectedCallbackHandler : CallbackQueryHandlerBase
    {
        private readonly UserSessionService _sessionService;
        private Regex _cityRegex = new(@"^city_(?<id>\d+)$", RegexOptions.Compiled);

        public CitySelectedCallbackHandler(UserSessionService sessionService)
        {
            _sessionService = sessionService;
        }

        protected override bool CanHandle(CallbackQuery callbackQuery) => _cityRegex.IsMatch(callbackQuery!.Data!);

        public override async Task ExecuteAsync(CallbackQuery callback, ITelegramBotClient botClient, CancellationToken ct)
        {
            await botClient.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

            var match = _cityRegex.Match(callback.Data!);
            if (match.Success && int.TryParse(match.Groups["id"].Value, out int cityId))
            {
                // Обратный словарь для вывода красивого названия в чат
                var cityNames = new Dictionary<int, string>
                {
                    { 7, "Минск" },
                    { 5, "Гомель" },
                    { 13, "Могилев" },
                    { 18, "Витебск" },
                    { 9, "Гродно" },
                    { 1, "Брест" }
                };

                if (cityNames.TryGetValue(cityId, out var cityName))
                {
                    var session = _sessionService.GetOrCreateSession(callback.Message!.Chat.Id);
                    session.City = cityName;
                    session.CityId = cityId;
                }
            }

            var settingsMenu = new CreateNewSearchCallbackHandler(_sessionService);
            await settingsMenu.ExecuteAsync(callback, botClient, ct);
        }
    }
}
using System.Collections.Generic;
using System.Linq;

namespace KufarBot.App.Telegram;

public static class CityCatalog
{
    public static readonly IReadOnlyDictionary<string, int> CitiesByName = new Dictionary<string, int>
    {
        { "Минск", 7 },
        { "Гомель", 5 },
        { "Могилев", 13 },
        { "Витебск", 18 },
        { "Гродно", 9 },
        { "Брест", 1 }
    };

    public static readonly IReadOnlyDictionary<int, string> CitiesById =
        CitiesByName.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);

    public static string? GetCityName(int? cityId)
    {
        if (cityId.HasValue && CitiesById.TryGetValue(cityId.Value, out var name))
        {
            return name;
        }
        return null;
    }
}

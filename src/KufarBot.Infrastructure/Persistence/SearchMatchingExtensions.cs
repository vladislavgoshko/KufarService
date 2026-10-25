using System;
using System.Text.Json;
using KufarBot.Core.Models;

namespace KufarBot.Infrastructure.Persistence;

public static class SearchMatchingExtensions
{
    public static bool Matches(this UserSearchEntity search, Advertisement ad)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(ad);

        if (search.MinPriceByn.HasValue)
        {
            if (!ad.PriceByn.HasValue || ad.PriceByn.Value < search.MinPriceByn.Value)
            {
                return false;
            }
        }

        if (search.MaxPriceByn.HasValue)
        {
            if (!ad.PriceByn.HasValue || ad.PriceByn.Value > search.MaxPriceByn.Value)
            {
                return false;
            }
        }

        return true;
    }

    public static List<long> DeserializeIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<long>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<long>>(json) ?? new List<long>();
        }
        catch
        {
            return new List<long>();
        }
    }

    public static string SerializeIds(IEnumerable<long> ids)
    {
        return JsonSerializer.Serialize(ids);
    }
}

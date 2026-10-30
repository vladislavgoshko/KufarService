using System;
using System.Linq;
using System.Text.Json;
using KufarBot.Core.Filters;
using KufarBot.Core.Models;

namespace KufarBot.Infrastructure.Persistence;

public static class SearchMatchingExtensions
{
    public static bool Matches(this UserSearchEntity search, Advertisement ad, DateTime? utcNow = null)
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

        foreach (var filter in SavedFilter.Deserialize(search.FiltersJson))
        {
            if (!MatchesFilter(filter, ad, utcNow ?? DateTime.UtcNow))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesFilter(SavedFilter filter, Advertisement ad, DateTime utcNow)
    {
        switch (filter.Kind)
        {
            case SavedFilterKind.KeywordsInclude:
                return filter.Values.Any() &&
                       filter.Values.Any(k => ContainsIgnoreCase(ad.SearchableText, k));

            case SavedFilterKind.KeywordsExclude:
                return !filter.Values.Any(k => ContainsIgnoreCase(ad.SearchableText, k));

            case SavedFilterKind.Query:
                return filter.Values.Any() &&
                       filter.Values.All(k => ContainsIgnoreCase(ad.SearchableText, k));

            case SavedFilterKind.SellerType:
                if (string.Equals(filter.Values.FirstOrDefault(), "company", StringComparison.OrdinalIgnoreCase))
                {
                    return ad.CompanyAd == true;
                }
                return ad.CompanyAd != true; // "private"

            case SavedFilterKind.MaxAgeHours:
                return ad.ListTimeUtc >= utcNow - TimeSpan.FromHours((double)filter.Min!.Value);

            case SavedFilterKind.Bool:
                return AdParameterValue(ad, filter) == "1" && filter.Values.Contains("1");

            case SavedFilterKind.Single:
                return MatchesValue(ad, filter, any: true);

            case SavedFilterKind.Multi:
                return filter.MultiEnabled()
                    ? MatchesValue(ad, filter, any: true)
                    : MatchesValue(ad, filter, any: false);

            case SavedFilterKind.Range:
                return MatchesNumericRange(ad, filter);

            case SavedFilterKind.Text:
                var adValue = AdParameterValue(ad, filter);
                return adValue != null && ContainsIgnoreCase(adValue, filter.Values.FirstOrDefault() ?? "");

            default:
                return true;
        }
    }

    // NOTE: Multi SavedFilters from the bot's own UI default to "any of" semantics
    // (Kufar's multiselect_or); a separate "All" flag can be added later.
    private static bool MultiEnabled(this SavedFilter _) => true;

    private static bool MatchesValue(Advertisement ad, SavedFilter filter, bool any)
    {
        var actual = AdParameterValue(ad, filter);
        if (actual == null)
        {
            return false;
        }

        return any
            ? filter.Values.Contains(actual, StringComparer.OrdinalIgnoreCase)
            : filter.Values.All(e => ContainsIgnoreCase(actual, e));
    }

    private static bool MatchesNumericRange(Advertisement ad, SavedFilter filter)
    {
        var value = AdParameterValue(ad, filter);
        if (value == null || !decimal.TryParse(value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out decimal numeric))
        {
            return false;
        }

        if (filter.Min.HasValue && numeric < filter.Min.Value)
        {
            return false;
        }

        if (filter.Max.HasValue && numeric > filter.Max.Value)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Site-filter value lookup by parameter identity. A saved filter carries either
    /// the internal parameter name (wizard filters: Key="type", UrlName="typ") or only
    /// the URL token (eph-18 search presets: Key="typ"); an ad parameter exposes both
    /// (Name="p", UrlName="pu"). Any matching pair of identifiers resolves the value.
    /// </summary>
    private static string? AdParameterValue(Advertisement ad, SavedFilter filter)
    {
        var param = ad.Parameters.FirstOrDefault(p =>
            MatchesParameterIdentity(p, filter.Key) || MatchesParameterIdentity(p, filter.UrlName));
        return param?.Value;
    }

    private static bool MatchesParameterIdentity(AdParameter parameter, string? identifier)
    {
        if (string.IsNullOrEmpty(identifier))
        {
            return false;
        }

        return string.Equals(parameter.Name, identifier, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(parameter.UrlName, identifier, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsIgnoreCase(string source, string fragment)
    {
        return source.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
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

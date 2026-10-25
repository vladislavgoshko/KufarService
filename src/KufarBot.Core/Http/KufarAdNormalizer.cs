using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KufarBot.Core.Http.Dto;
using KufarBot.Core.Models;

namespace KufarBot.Core.Http;

public static class KufarAdNormalizer
{
    public static Advertisement Normalize(KufarAdDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        decimal? priceByn = ParsePriceByn(dto.PriceByn);
        (string? region, string? area, string location) = ExtractLocation(dto.AdParameters);
        List<string> imageUrls = ExtractImageUrls(dto.Images);
        DateTime listTimeUtc = ParseDateTimeUtc(dto.ListTime);
        List<AdParameter> parameters = ExtractParameters(dto.AdParameters);

        return new Advertisement
        {
            Id = dto.AdId,
            Title = string.IsNullOrWhiteSpace(dto.Subject) ? "Без названия" : dto.Subject.Trim(),
            Description = string.IsNullOrWhiteSpace(dto.Body) ? dto.BodyShort?.Trim() : dto.Body.Trim(),
            PriceByn = priceByn,
            Currency = string.IsNullOrWhiteSpace(dto.Currency) ? "BYN" : dto.Currency.Trim(),
            Region = region,
            Area = area,
            Location = location,
            AdLink = string.IsNullOrWhiteSpace(dto.AdLink)
                ? $"https://www.kufar.by/item/{dto.AdId}"
                : dto.AdLink.Trim(),
            ImageUrl = imageUrls.FirstOrDefault(),
            AllImageUrls = imageUrls,
            ListTimeUtc = listTimeUtc,
            CategoryId = dto.Category,
            CompanyAd = dto.CompanyAd,
            Parameters = parameters
        };
    }

    public static List<Advertisement> NormalizeMany(IEnumerable<KufarAdDto>? dtos)
    {
        if (dtos == null)
        {
            return new List<Advertisement>();
        }

        return dtos.Select(Normalize).ToList();
    }

    public static decimal? ParsePriceByn(string? priceBynString)
    {
        if (string.IsNullOrWhiteSpace(priceBynString))
        {
            return null;
        }

        if (long.TryParse(priceBynString.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long kopecks))
        {
            return kopecks / 100m;
        }

        return null;
    }

    public static (string? Region, string? Area, string FormattedLocation) ExtractLocation(List<KufarAdParameterDto>? parameters)
    {
        if (parameters == null || parameters.Count == 0)
        {
            return (null, null, "Беларусь");
        }

        string? region = null;
        string? area = null;

        foreach (var p in parameters)
        {
            if (p.ParameterName == "region")
            {
                region = ExtractParameterLabel(p.ValueLabel, p.Value);
            }
            else if (p.ParameterName == "area")
            {
                area = ExtractParameterLabel(p.ValueLabel, p.Value);
            }
        }

        string formatted;
        if (!string.IsNullOrWhiteSpace(region) && !string.IsNullOrWhiteSpace(area))
        {
            if (string.Equals(region, area, StringComparison.OrdinalIgnoreCase))
            {
                formatted = region;
            }
            else
            {
                formatted = $"{region}, {area}";
            }
        }
        else if (!string.IsNullOrWhiteSpace(region))
        {
            formatted = region;
        }
        else if (!string.IsNullOrWhiteSpace(area))
        {
            formatted = area;
        }
        else
        {
            formatted = "Беларусь";
        }

        return (region, area, formatted);
    }

    private static string? ExtractParameterLabel(object? valueLabel, object? value)
    {
        if (valueLabel != null)
        {
            string labelStr = valueLabel.ToString()?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(labelStr))
            {
                return labelStr;
            }
        }

        if (value != null)
        {
            string valStr = value.ToString()?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(valStr))
            {
                return valStr;
            }
        }

        return null;
    }

    public static List<string> ExtractImageUrls(List<KufarImageDto>? images)
    {
        if (images == null || images.Count == 0)
        {
            return new List<string>();
        }

        var result = new List<string>(images.Count);
        foreach (var img in images)
        {
            var url = KufarUrlBuilder.FormatImageUrl(img.Path);
            if (!string.IsNullOrWhiteSpace(url))
            {
                result.Add(url);
            }
        }

        return result;
    }

    /// <summary>
    /// Builds a string-normalized snapshot of ad parameters so saved filters can be
    /// matched against any ad without hitting the taxonomy API again.
    /// </summary>
    public static List<AdParameter> ExtractParameters(List<KufarAdParameterDto>? parameters)
    {
        if (parameters == null || parameters.Count == 0)
        {
            return new List<AdParameter>();
        }

        var result = new List<AdParameter>(parameters.Count);
        foreach (var p in parameters)
        {
            if (string.IsNullOrWhiteSpace(p.ParameterName))
            {
                continue;
            }

            string? value = NormalizeParameterValue(p.Value);
            if (value == null)
            {
                continue;
            }

            string? label = TryNormalizeParameterValue(p.ValueLabel);
            result.Add(new AdParameter(p.ParameterName.Trim(), value, label));
        }

        return result;
    }

    public static string? NormalizeParameterValue(object? raw)
    {
        var normalized = TryNormalizeParameterValue(raw);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? TryNormalizeParameterValue(object? raw)
    {
        if (raw == null)
        {
            return null;
        }

        if (raw is bool b)
        {
            return b ? "1" : "0";
        }

        var s = raw.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(s))
        {
            return null;
        }

        // bool values sometimes arrive as strings "true"/"false"
        if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)) return "1";
        if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)) return "0";

        return s;
    }

    public static DateTime ParseDateTimeUtc(string? rawDateTime)
    {        if (string.IsNullOrWhiteSpace(rawDateTime))
        {
            return DateTime.UtcNow;
        }

        if (DateTimeOffset.TryParse(
            rawDateTime.Trim(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsedOffset))
        {
            return parsedOffset.UtcDateTime;
        }

        return DateTime.UtcNow;
    }
}

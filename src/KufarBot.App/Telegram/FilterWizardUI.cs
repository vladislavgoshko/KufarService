using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KufarBot.Core.Filters;
using System.Text;

namespace KufarBot.App.Telegram;

/// <summary>Renders filter wizard screens and parses free-text filter input.</summary>
public static class FilterWizardUI
{
    public static string RenderSpecScreen(UserSearchDraft draft, int specIndex)
    {
        var spec = draft.AvailableSpecs[specIndex];
        var sb = new StringBuilder();

        sb.Append($"🎛 <b>{Escape(spec.Title)}</b>\n\n");

        switch (spec.Kind)
        {
            case FilterKind.Bool:
                sb.Append("Включить этот фильтр в поиск?\n");
                break;
            case FilterKind.Single:
                sb.Append("Выберите значение:\n");
                break;
            case FilterKind.Multi:
                var chosenMulti = BotKeyboards.FindForSpec(draft, spec);
                if (chosenMulti != null && chosenMulti.Values.Count > 0)
                {
                    sb.Append($"Выбрано: <b>{chosenMulti.Values.Count}</b>\n\n");
                }
                sb.Append("Выберите подходящие значения (можно несколько), затем «✔ Готово».\n");
                break;
            case FilterKind.Range:
                var saved = BotKeyboards.FindForSpec(draft, spec);
                if (saved != null)
                {
                    sb.Append($"Текущее значение: <b>{FilterValue(saved)}</b>\n\n");
                }
                sb.Append("Введите диапазон одним сообщением, например:\n" +
                          "<code>8 32</code> или <code>8-32</code> — от 8 до 32;\n" +
                          "<code>от 8</code> / <code>8</code> — минимум 8;\n" +
                          "<code>до 32</code> — максимум 32.");
                break;
            case FilterKind.Text:
                sb.Append("Отправьте текст (совпадение по вхождению).\n");
                break;
        }

        if (spec.RangeLower.HasValue || spec.RangeUpper.HasValue)
        {
            sb.Append($"\nДопустимо: {(spec.RangeLower ?? 0):0.#} – {(spec.RangeUpper ?? 999999):0.#}");
        }

        return sb.ToString();
    }

    public static string FilterValue(SavedFilter filter)
    {
        return filter.Kind switch
        {
            SavedFilterKind.Range => $"{filter.Min:0.#}–{filter.Max:0.#}",
            SavedFilterKind.MaxAgeHours => $"за {filter.Min:0} ч.",
            SavedFilterKind.SellerType => string.Equals(filter.Values.FirstOrDefault(), "company", StringComparison.OrdinalIgnoreCase)
                ? "только компании" : "только частники",
            _ => string.Join(", ", filter.Values)
        };
    }

    /// <summary>
    /// Parses a range input like "8 32", "8-32", "8", "от 8", "до 32", "8+".
    /// Returns error message (HTML) when input is unusable; at least one bound required.
    /// </summary>
    public static FilterParseResult ParseRangeInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return FilterParseResult.Fail("Пустой ввод. Введите диапазон, например <code>8-32</code>.");
        }

        var clean = input.Trim();
        string? minText = null;
        string? maxText = null;

        var lower = clean.ToLowerInvariant();
        if (lower.StartsWith("от "))
        {
            minText = clean.Substring(3).Trim().TrimEnd('+');
        }
        else if (lower.StartsWith("до "))
        {
            maxText = clean.Substring(3).Trim();
        }
        else if (clean.EndsWith("+"))
        {
            minText = clean.TrimEnd('+');
        }
        else
        {
            var separators = new[] { ' ', '-', '–', ';', ',' };
            var parts = clean.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 1)
            {
                minText = parts[0];
            }
            else if (parts.Length == 2)
            {
                minText = parts[0];
                maxText = parts[1];
            }
            else
            {
                return FilterParseResult.Fail("Не понял диапазон. Пример: <code>8-32</code>, <code>от 8</code> или <code>до 32</code>.");
            }
        }

        bool minMissing = string.IsNullOrWhiteSpace(minText);
        bool maxMissing = string.IsNullOrWhiteSpace(maxText);

        if (minMissing && maxMissing)
        {
            return FilterParseResult.Fail("Нужно указать хотя бы одну границу диапазона.");
        }

        decimal? min = null;
        decimal? max = null;

        if (!minMissing && !TryParseNumber(minText, out min))
        {
            return FilterParseResult.Fail($"«{Escape(minText ?? "")}» — не похоже на число.");
        }

        if (!maxMissing && !TryParseNumber(maxText, out max))
        {
            return FilterParseResult.Fail($"«{Escape(maxText ?? "")}» — не похоже на число.");
        }

        if (min.HasValue && max.HasValue && min.Value > max.Value)
        {
            return FilterParseResult.Fail("Минимум больше максимума. Исправьте, например: <code>8-32</code>.");
        }

        return FilterParseResult.Ok(min, max);
    }

    /// <summary>Comma/whitespace separated keywords; empty input clears the filter.</summary>
    public static List<string> ParseKeywordsInput(string input)
    {
        return (input ?? "")
            .Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(k => k.Length > 0)
            .Select(k => k.Length > 40 ? k.Substring(0, 40) : k)
            .Take(10)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static FilterParseResult ParseTextInput(string input)
    {
        var text = (input ?? "").Trim();
        if (text.Length == 0)
        {
            return FilterParseResult.Fail("Введите значение.");
        }

        if (text.Length > 120)
        {
            text = text.Substring(0, 120);
        }

        return FilterParseResult.Ok(text);
    }

    /// <summary>Whitespace-separated query words (1..5 words, each ≤ 40 chars). Empty input clears the filter.</summary>
    public static List<string> ParseQueryInput(string input)
    {
        return (input ?? "")
            .Split(new[] { ' ', '\t', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(k => k.Length > 1)
            .Select(k => k.Length > 40 ? k.Substring(0, 40) : k)
            .Take(5)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool TryParseNumber(string? text, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (decimal.TryParse(text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    public static string Escape(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}

public class FilterParseResult
{
    public decimal? Min { get; }
    public decimal? Max { get; }
    public string? TextValue { get; }
    public string? Error { get; private set; }
    public bool Success => Error == null;

    private FilterParseResult(decimal? min, decimal? max, string? error)
    {
        Min = min;
        Max = max;
        Error = error;
    }

    private FilterParseResult(string? error, string textValue)
    {
        TextValue = textValue;
        Error = error;
    }

    public static FilterParseResult Ok(decimal? min, decimal? max) => new(min, max, null);
    public static FilterParseResult Ok(string text) => new(null, text);
    public static FilterParseResult Fail(string error) => new(null, null, error);
}

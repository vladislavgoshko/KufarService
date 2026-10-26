using System.Collections.Generic;

namespace KufarBot.Core.Filters;

/// <summary>
/// A dispatch rule that adds extra parameters once a "driver" parameter has certain values,
/// e.g. «Тип = Процессоры» → «Сокет», «Модель процессора». The rule form is
/// {"category": "&lt;subcategoryId&gt;", "&lt;driver&gt;": "&lt;value&gt;"} where multiple values
/// come as "$in[a,b]".
/// </summary>
public sealed record ConditionalRule(
    int SubcategoryId,
    string DriverKey,
    IReadOnlyList<string> DriverValues,
    IReadOnlyList<FilterSpec> Specs);

/// <summary>All filter parameters derived from one category dispatch (single HTTP request).</summary>
public sealed record CategoryFilterSpecs(
    /// <summary>Base + subcategory-specific parameters per subcategory id.</summary>
    IReadOnlyDictionary<int, IReadOnlyList<FilterSpec>> BySubcategory,
    /// <summary>Value-driven extra parameters per subcategory id.</summary>
    IReadOnlyDictionary<int, IReadOnlyList<ConditionalRule>> Conditionals);

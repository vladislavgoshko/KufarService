namespace KufarBot.Core.Models;

/// <summary>
/// A single parameter carried by a Kufar ad ("ad_parameters" entries: p/pu/v/vl),
/// normalized to strings so it can be matched against saved filters.
/// <see cref="UrlName"/> is the Kufar search-API token ("pu", e.g. "typ") while
/// <see cref="Name"/> is the internal parameter name ("p", e.g. "type").
/// </summary>
public sealed record AdParameter(string Name, string Value, string? Label = null, string? UrlName = null);

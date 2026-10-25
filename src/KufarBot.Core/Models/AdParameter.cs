namespace KufarBot.Core.Models;

/// <summary>
/// A single parameter carried by a Kufar ad ("ad_parameters" entries: p/v/vl),
/// normalized to strings so it can be matched against saved filters.
/// </summary>
public sealed record AdParameter(string Name, string Value, string? Label = null);

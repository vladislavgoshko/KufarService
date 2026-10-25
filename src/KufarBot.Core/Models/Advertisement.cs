using System;
using System.Collections.Generic;

namespace KufarBot.Core.Models;

public record Advertisement
{
    public required long Id { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public decimal? PriceByn { get; init; }
    public string Currency { get; init; } = "BYN";
    public string? Region { get; init; }
    public string? Area { get; init; }
    public required string Location { get; init; }
    public required string AdLink { get; init; }
    public string? ImageUrl { get; init; }
    public IReadOnlyList<string> AllImageUrls { get; init; } = Array.Empty<string>();
    public required DateTime ListTimeUtc { get; init; }
    public string? CategoryId { get; init; }

    public string FormattedPrice =>
        PriceByn switch
        {
            null => "По договоренности",
            0m => "По договоренности",
            _ => $"{PriceByn.Value:0.##} BYN"
        };
}

namespace KufarBot.Core.Models;

public record KufarSearchCriteria
{
    public int? CategoryId { get; init; }
    public int? AreaId { get; init; }
    public int? RegionId { get; init; }
    public decimal? MinPriceByn { get; init; }
    public decimal? MaxPriceByn { get; init; }
    public string? Query { get; init; }
    public int Size { get; init; } = 50;
    public string? Cursor { get; init; }
    public string Sort { get; init; } = "lst.d";
    public string Currency { get; init; } = "BYN";
}

using System;
using System.Collections.Generic;
using KufarBot.Core.Http;
using KufarBot.Core.Http.Dto;
using Xunit;

namespace KufarBot.Tests;

public class KufarAdNormalizerTests
{
    [Theory]
    [InlineData("20000", 200.0)]
    [InlineData("25000", 250.0)]
    [InlineData("500", 5.0)]
    [InlineData("95", 0.95)]
    [InlineData("0", 0.0)]
    public void ParsePriceByn_ConvertsKopecksToDecimalByn(string rawKopecks, decimal expectedByn)
    {
        var price = KufarAdNormalizer.ParsePriceByn(rawKopecks);

        Assert.NotNull(price);
        Assert.Equal(expectedByn, price.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not_a_number")]
    public void ParsePriceByn_ReturnsNull_WhenEmptyOrInvalid(string? rawPrice)
    {
        var price = KufarAdNormalizer.ParsePriceByn(rawPrice);

        Assert.Null(price);
    }

    [Fact]
    public void ExtractLocation_ExtractsRegionAndArea_FormatsCleanly()
    {
        var parameters = new List<KufarAdParameterDto>
        {
            new() { ParameterName = "region", ValueLabel = "Минск", Value = 7 },
            new() { ParameterName = "area", ValueLabel = "Первомайский", Value = "24" }
        };

        var (region, area, formatted) = KufarAdNormalizer.ExtractLocation(parameters);

        Assert.Equal("Минск", region);
        Assert.Equal("Первомайский", area);
        Assert.Equal("Минск, Первомайский", formatted);
    }

    [Fact]
    public void ExtractLocation_HandlesDuplicateRegionAndArea()
    {
        var parameters = new List<KufarAdParameterDto>
        {
            new() { ParameterName = "region", ValueLabel = "Минск" },
            new() { ParameterName = "area", ValueLabel = "Минск" }
        };

        var (region, area, formatted) = KufarAdNormalizer.ExtractLocation(parameters);

        Assert.Equal("Минск", formatted);
    }

    [Fact]
    public void ExtractLocation_HandlesMissingAreaOrRegion()
    {
        var parametersOnlyRegion = new List<KufarAdParameterDto>
        {
            new() { ParameterName = "region", ValueLabel = "Брестская область" }
        };

        var (_, _, formattedRegionOnly) = KufarAdNormalizer.ExtractLocation(parametersOnlyRegion);
        Assert.Equal("Брестская область", formattedRegionOnly);

        var (regionNull, areaNull, formattedEmpty) = KufarAdNormalizer.ExtractLocation(null);
        Assert.Null(regionNull);
        Assert.Null(areaNull);
        Assert.Equal("Беларусь", formattedEmpty);
    }

    [Fact]
    public void ExtractImageUrls_ConstructsValidCdnLinks()
    {
        var images = new List<KufarImageDto>
        {
            new() { Path = "adim1/image1.jpg" },
            new() { Path = "adim1/image2.jpg" },
            new() { Path = null },
            new() { Path = "   " }
        };

        var urls = KufarAdNormalizer.ExtractImageUrls(images);

        Assert.Equal(2, urls.Count);
        Assert.Equal("https://rms.kufar.by/v1/list_thumbs_2x/adim1/image1.jpg", urls[0]);
        Assert.Equal("https://rms.kufar.by/v1/list_thumbs_2x/adim1/image2.jpg", urls[1]);
    }

    [Fact]
    public void ParseDateTimeUtc_ParsesIsoTimestampsToUtc()
    {
        var rawIso = "2026-01-13T12:22:53Z";

        var dt = KufarAdNormalizer.ParseDateTimeUtc(rawIso);

        Assert.Equal(DateTimeKind.Utc, dt.Kind);
        Assert.Equal(2026, dt.Year);
        Assert.Equal(1, dt.Month);
        Assert.Equal(13, dt.Day);
        Assert.Equal(12, dt.Hour);
        Assert.Equal(22, dt.Minute);
        Assert.Equal(53, dt.Second);
    }

    [Fact]
    public void Normalize_MapsAllFieldsCorrectly()
    {
        var dto = new KufarAdDto
        {
            AdId = 1052019666,
            Subject = " Видеокарта MSI GTX 1060 3gb ",
            PriceByn = "20000",
            Currency = "BYR",
            AdLink = "https://www.kufar.by/item/1052019666",
            ListTime = "2026-01-13T12:22:53Z",
            Category = "16010",
            AdParameters = new List<KufarAdParameterDto>
            {
                new() { ParameterName = "region", ValueLabel = "Минск" },
                new() { ParameterName = "area", ValueLabel = "Первомайский" }
            },
            Images = new List<KufarImageDto>
            {
                new() { Path = "adim1/7dc5fc02-b93a-4624-9d04-4adab2a08d4a.jpg" }
            }
        };

        var ad = KufarAdNormalizer.Normalize(dto);

        Assert.Equal(1052019666L, ad.Id);
        Assert.Equal("Видеокарта MSI GTX 1060 3gb", ad.Title);
        Assert.Equal(200.00m, ad.PriceByn);
        Assert.Equal("200 BYN", ad.FormattedPrice);
        Assert.Equal("Минск, Первомайский", ad.Location);
        Assert.Equal("https://www.kufar.by/item/1052019666", ad.AdLink);
        Assert.Equal("https://rms.kufar.by/v1/list_thumbs_2x/adim1/7dc5fc02-b93a-4624-9d04-4adab2a08d4a.jpg", ad.ImageUrl);
        Assert.Equal(new DateTime(2026, 1, 13, 12, 22, 53, DateTimeKind.Utc), ad.ListTimeUtc);
        Assert.Equal("16010", ad.CategoryId);
    }

    [Fact]
    public void Normalize_WhenPriceIsZero_FormattedPriceShowsAgreement()
    {
        var dto = new KufarAdDto
        {
            AdId = 1005753296,
            Subject = "Процессоры для ПК",
            PriceByn = "0",
            ListTime = "2026-01-13T12:12:03Z"
        };

        var ad = KufarAdNormalizer.Normalize(dto);

        Assert.Equal(0m, ad.PriceByn);
        Assert.Equal("По договоренности", ad.FormattedPrice);
    }
}

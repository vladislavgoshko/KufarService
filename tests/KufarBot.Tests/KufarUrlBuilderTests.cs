using System;
using KufarBot.Core.Http;
using KufarBot.Core.Models;
using Xunit;

namespace KufarBot.Tests;

public class KufarUrlBuilderTests
{
    [Fact]
    public void BuildSearchUrl_IncludesCategoryAndCity_WithValidAmpersandSeparators()
    {
        var criteria = new KufarSearchCriteria
        {
            CategoryId = 16010,
            AreaId = 7,
            Size = 20
        };

        var url = KufarUrlBuilder.BuildSearchUrl(criteria);

        Assert.Contains("cat=16010", url);
        Assert.Contains("ar=7", url);
        Assert.Contains("size=20", url);
        Assert.Contains("sort=lst.d", url);
        Assert.Contains("cur=BYN", url);
        Assert.DoesNotContain(";", url);
    }

    [Fact]
    public void BuildSearchUrl_NeverContainsSemicolons()
    {
        var criteria = new KufarSearchCriteria
        {
            CategoryId = 17010,
            AreaId = 22,
            MinPriceByn = 100m,
            MaxPriceByn = 500m
        };

        var url = KufarUrlBuilder.BuildSearchUrl(criteria);

        Assert.DoesNotContain(";", url);
    }

    [Fact]
    public void BuildSearchUrl_ConvertsPriceBynToKopecks_BothBounds()
    {
        var criteria = new KufarSearchCriteria
        {
            MinPriceByn = 150.50m,
            MaxPriceByn = 300m
        };

        var url = KufarUrlBuilder.BuildSearchUrl(criteria);

        // 150.50 BYN = 15050 kopecks, 300 BYN = 30000 kopecks
        Assert.Contains("prc=r:15050,30000", url);
    }

    [Fact]
    public void BuildSearchUrl_MinPriceOnly_ProvidesDefaultUpperBound()
    {
        var criteria = new KufarSearchCriteria
        {
            MinPriceByn = 50m
        };

        var url = KufarUrlBuilder.BuildSearchUrl(criteria);

        // Kufar elasticsearch throws 422 if either bound is empty.
        // It must output prc=r:5000,999999999
        Assert.Contains("prc=r:5000,999999999", url);
    }

    [Fact]
    public void BuildSearchUrl_MaxPriceOnly_ProvidesZeroLowerBound()
    {
        var criteria = new KufarSearchCriteria
        {
            MaxPriceByn = 250m
        };

        var url = KufarUrlBuilder.BuildSearchUrl(criteria);

        // Kufar elasticsearch throws 422 if lower bound is empty.
        // It must output prc=r:0,25000
        Assert.Contains("prc=r:0,25000", url);
    }

    [Fact]
    public void BuildSearchUrl_IncludesCursorAndQuery_UrlEncoded()
    {
        var criteria = new KufarSearchCriteria
        {
            Query = "RTX 3060 Ti",
            Cursor = "token==with+symbols"
        };

        var url = KufarUrlBuilder.BuildSearchUrl(criteria);

        Assert.Contains("query=RTX%203060%20Ti", url);
        Assert.Contains("cursor=token%3D%3Dwith%2Bsymbols", url);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(50, 50)]
    [InlineData(300, 200)]
    public void BuildSearchUrl_ClampsSizeBetween1And200(int requestedSize, int expectedSize)
    {
        var criteria = new KufarSearchCriteria
        {
            Size = requestedSize
        };

        var url = KufarUrlBuilder.BuildSearchUrl(criteria);

        Assert.Contains($"size={expectedSize}", url);
    }

    [Fact]
    public void FormatImageUrl_ReturnsValidCdnUrl_WhenPathProvided()
    {
        var rawPath = "adim1/7dc5fc02-b93a-4624-9d04-4adab2a08d4a.jpg";

        var url = KufarUrlBuilder.FormatImageUrl(rawPath);

        Assert.Equal("https://rms.kufar.by/v1/list_thumbs_2x/adim1/7dc5fc02-b93a-4624-9d04-4adab2a08d4a.jpg", url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FormatImageUrl_ReturnsNull_WhenPathEmptyOrWhitespace(string? path)
    {
        var url = KufarUrlBuilder.FormatImageUrl(path);

        Assert.Null(url);
    }
}

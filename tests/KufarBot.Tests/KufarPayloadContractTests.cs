using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using KufarBot.Core.Http;
using KufarBot.Core.Http.Dto;
using Xunit;

namespace KufarBot.Tests;

public class KufarPayloadContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static string FindFixturePath(string relativePath)
    {
        var currentDir = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDir != null)
        {
            var candidate = Path.Combine(currentDir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            currentDir = currentDir.Parent;
        }

        throw new FileNotFoundException($"Fixture file not found: {relativePath}");
    }

    [Fact]
    public void RealKufarFixture_DeserializesAndNormalizesAccurately()
    {
        var fixturePath = FindFixturePath("research/data/api_data/search16010.json");
        var json = File.ReadAllText(fixturePath);

        var responseDto = JsonSerializer.Deserialize<KufarSearchResponseDto>(json, JsonOptions);

        Assert.NotNull(responseDto);
        Assert.NotNull(responseDto.Ads);
        Assert.NotEmpty(responseDto.Ads);

        var normalizedAds = KufarAdNormalizer.NormalizeMany(responseDto.Ads);

        Assert.Equal(responseDto.Ads.Count, normalizedAds.Count);

        // General contract assertions across every ad in real payload
        foreach (var ad in normalizedAds)
        {
            Assert.True(ad.Id > 0, "Ad Id must be positive");
            Assert.False(string.IsNullOrWhiteSpace(ad.Title), "Title must not be empty");
            Assert.StartsWith("https://www.kufar.by/item/", ad.AdLink);
            Assert.Equal(DateTimeKind.Utc, ad.ListTimeUtc.Kind);
            Assert.False(string.IsNullOrWhiteSpace(ad.Location), "Location must be populated");

            if (ad.ImageUrl != null)
            {
                Assert.StartsWith("https://rms.kufar.by/v1/list_thumbs_2x/", ad.ImageUrl);
            }
        }

        // Spot-check specific known items from the verified payload
        var gtx1060 = normalizedAds.First(a => a.Id == 1052019666L);
        Assert.Equal("Видеокарта MSI GTX 1060 3gb", gtx1060.Title);
        Assert.Equal(200.00m, gtx1060.PriceByn);
        Assert.Equal("200 BYN", gtx1060.FormattedPrice);
        Assert.Equal("Минск, Первомайский", gtx1060.Location);
        Assert.Equal("https://rms.kufar.by/v1/list_thumbs_2x/adim1/7dc5fc02-b93a-4624-9d04-4adab2a08d4a.jpg", gtx1060.ImageUrl);
        Assert.Equal(5, gtx1060.AllImageUrls.Count);

        var kingstonRam = normalizedAds.First(a => a.Id == 1052019618L);
        Assert.Equal("Kingston DDR4 16Gb KVR26N19D8/16", kingstonRam.Title);
        Assert.Equal(250.00m, kingstonRam.PriceByn);
        Assert.Equal("250 BYN", kingstonRam.FormattedPrice);
        Assert.Equal("Минск, Центральный", kingstonRam.Location);

        var hdd2Tb = normalizedAds.First(a => a.Id == 1052018556L);
        Assert.Equal(150.00m, hdd2Tb.PriceByn);
        Assert.Equal("Могилевская область, Осиповичи", hdd2Tb.Location);

        var zeroPriceItem = normalizedAds.First(a => a.Id == 1005753296L);
        Assert.Equal(0.00m, zeroPriceItem.PriceByn);
        Assert.Equal("По договоренности", zeroPriceItem.FormattedPrice);
        Assert.Equal("Гомельская область, Жлобин", zeroPriceItem.Location);
    }
}

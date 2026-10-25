using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KufarBot.Core.Models;
using KufarBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KufarBot.Tests;

public class PersistenceTests
{
    private AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        var context = new AppDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public async Task CanCreateUserAndSearch_PersistsCorrectly()
    {
        await using var context = CreateInMemoryDbContext();

        var user = new UserEntity
        {
            Id = 123456789,
            Username = "testuser",
            FirstName = "Test",
            CreatedAtUtc = DateTime.UtcNow
        };

        context.Users.Add(user);

        var search = new UserSearchEntity
        {
            UserId = user.Id,
            SubcategoryId = 16010,
            SubcategoryTitle = "Комплектующие",
            CityId = 7,
            CityName = "Минск",
            MinPriceByn = 100m,
            MaxPriceByn = 500m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        context.UserSearches.Add(search);
        await context.SaveChangesAsync();

        var loadedSearch = await context.UserSearches.Include(s => s.User).FirstOrDefaultAsync();

        Assert.NotNull(loadedSearch);
        Assert.Equal(16010, loadedSearch.SubcategoryId);
        Assert.Equal(7, loadedSearch.CityId);
        Assert.Equal(100m, loadedSearch.MinPriceByn);
        Assert.Equal(500m, loadedSearch.MaxPriceByn);
        Assert.Equal(123456789, loadedSearch.User.Id);
    }

    [Fact]
    public void SearchMatching_FiltersAdsCorrectly()
    {
        var search = new UserSearchEntity
        {
            MinPriceByn = 100m,
            MaxPriceByn = 300m
        };

        var adCheap = new Advertisement
        {
            Id = 1,
            Title = "Cheap",
            PriceByn = 50m,
            Location = "Minsk",
            AdLink = "https://kufar.by/1",
            ListTimeUtc = DateTime.UtcNow
        };

        var adMatch = new Advertisement
        {
            Id = 2,
            Title = "Match",
            PriceByn = 200m,
            Location = "Minsk",
            AdLink = "https://kufar.by/2",
            ListTimeUtc = DateTime.UtcNow
        };

        var adExpensive = new Advertisement
        {
            Id = 3,
            Title = "Expensive",
            PriceByn = 400m,
            Location = "Minsk",
            AdLink = "https://kufar.by/3",
            ListTimeUtc = DateTime.UtcNow
        };

        var adNoPrice = new Advertisement
        {
            Id = 4,
            Title = "No Price",
            PriceByn = null,
            Location = "Minsk",
            AdLink = "https://kufar.by/4",
            ListTimeUtc = DateTime.UtcNow
        };

        Assert.False(search.Matches(adCheap));
        Assert.True(search.Matches(adMatch));
        Assert.False(search.Matches(adExpensive));
        Assert.False(search.Matches(adNoPrice));
    }
}

using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KufarBot.Infrastructure.Persistence;
using KufarBot.Infrastructure.Polling;
using KufarBot.Infrastructure.TelegramClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Xunit;

namespace KufarBot.Tests;

public class OutboxDeliveryWorkerTests
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
    public async Task ProcessOutboxBatch_SendsSuccessfully_AndMarksAsSent()
    {
        await using var dbContext = CreateInMemoryDbContext();

        long userId = 123;
        dbContext.Users.Add(new UserEntity { Id = userId, CreatedAtUtc = DateTime.UtcNow });

        var payload = new OutboxPayload
        {
            AdId = 1,
            Title = "Test Ad",
            PriceFormatted = "100 BYN",
            Location = "Minsk",
            AdLink = "http://link",
            CreatedAtUtc = DateTime.UtcNow
        };

        var outboxItem = new NotificationOutboxEntity
        {
            UserId = userId,
            SearchId = 1,
            AdId = 1,
            Status = 0,
            PayloadJson = JsonSerializer.Serialize(payload),
            MaxAttempts = 5,
            NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(-1),
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.NotificationOutbox.Add(outboxItem);
        await dbContext.SaveChangesAsync();

        var mockSender = new Mock<ITelegramSender>();
        mockSender.Setup(s => s.SendTextMessageAsync(userId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton(mockSender.Object);

        var worker = new OutboxDeliveryWorker(services.BuildServiceProvider(), NullLogger<OutboxDeliveryWorker>.Instance);

        int processed = await worker.ProcessOutboxBatchAsync(CancellationToken.None);

        Assert.Equal(1, processed);

        var item = await dbContext.NotificationOutbox.FirstAsync();
        Assert.Equal(2, item.Status); // Sent
        Assert.NotNull(item.SentAtUtc);
        Assert.Null(item.LockedUntilUtc);

        mockSender.Verify(s => s.SendTextMessageAsync(userId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessOutboxBatch_TransientError_SchedulesRetryWithBackoff()
    {
        await using var dbContext = CreateInMemoryDbContext();

        long userId = 123;
        dbContext.Users.Add(new UserEntity { Id = userId, CreatedAtUtc = DateTime.UtcNow });

        var payload = new OutboxPayload { AdId = 1, Title = "Test Ad" };
        var outboxItem = new NotificationOutboxEntity
        {
            UserId = userId,
            SearchId = 1,
            AdId = 1,
            Status = 0,
            PayloadJson = JsonSerializer.Serialize(payload),
            AttemptCount = 0,
            MaxAttempts = 5,
            NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(-1),
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.NotificationOutbox.Add(outboxItem);
        await dbContext.SaveChangesAsync();

        var mockSender = new Mock<ITelegramSender>();
        mockSender.Setup(s => s.SendTextMessageAsync(userId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiRequestException("Timeout", 500));

        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton(mockSender.Object);

        var worker = new OutboxDeliveryWorker(services.BuildServiceProvider(), NullLogger<OutboxDeliveryWorker>.Instance);

        await worker.ProcessOutboxBatchAsync(CancellationToken.None);

        var item = await dbContext.NotificationOutbox.FirstAsync();
        Assert.Equal(3, item.Status); // Failed/Retryable
        Assert.Equal(1, item.AttemptCount);
        Assert.Null(item.LockedUntilUtc);
        Assert.True(item.NextAttemptAtUtc > DateTime.UtcNow);
    }

    [Fact]
    public async Task ProcessOutboxBatch_403Forbidden_DeactivatesUserAndDeadLetters()
    {
        await using var dbContext = CreateInMemoryDbContext();

        long userId = 123;
        dbContext.Users.Add(new UserEntity { Id = userId, CreatedAtUtc = DateTime.UtcNow, IsActive = true });

        var payload = new OutboxPayload { AdId = 1, Title = "Test Ad" };
        var outboxItem = new NotificationOutboxEntity
        {
            UserId = userId,
            SearchId = 1,
            AdId = 1,
            Status = 0,
            PayloadJson = JsonSerializer.Serialize(payload),
            MaxAttempts = 5,
            NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(-1),
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.NotificationOutbox.Add(outboxItem);
        await dbContext.SaveChangesAsync();

        var mockSender = new Mock<ITelegramSender>();
        mockSender.Setup(s => s.SendTextMessageAsync(userId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiRequestException("Forbidden: bot was blocked by the user", 403));

        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton(mockSender.Object);

        var worker = new OutboxDeliveryWorker(services.BuildServiceProvider(), NullLogger<OutboxDeliveryWorker>.Instance);

        await worker.ProcessOutboxBatchAsync(CancellationToken.None);

        var item = await dbContext.NotificationOutbox.FirstAsync();
        Assert.Equal(4, item.Status); // DeadLetter
        Assert.Equal(1, item.AttemptCount);

        var user = await dbContext.Users.FirstAsync();
        Assert.False(user.IsActive);
    }

    [Fact]
    public async Task ProcessOutboxBatch_RateLimit429_RespectsRetryAfter()
    {
        await using var dbContext = CreateInMemoryDbContext();

        long userId = 123;
        dbContext.Users.Add(new UserEntity { Id = userId, CreatedAtUtc = DateTime.UtcNow });

        var payload = new OutboxPayload { AdId = 1, Title = "Test Ad" };
        var outboxItem = new NotificationOutboxEntity
        {
            UserId = userId,
            SearchId = 1,
            AdId = 1,
            Status = 0,
            PayloadJson = JsonSerializer.Serialize(payload),
            AttemptCount = 0,
            MaxAttempts = 5,
            NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(-1),
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.NotificationOutbox.Add(outboxItem);
        await dbContext.SaveChangesAsync();

        var mockSender = new Mock<ITelegramSender>();
        var responseParams = new ResponseParameters { RetryAfter = 45 };
        mockSender.Setup(s => s.SendTextMessageAsync(userId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiRequestException("Too Many Requests", 429, responseParams));

        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton(mockSender.Object);

        var worker = new OutboxDeliveryWorker(services.BuildServiceProvider(), NullLogger<OutboxDeliveryWorker>.Instance);

        await worker.ProcessOutboxBatchAsync(CancellationToken.None);

        var item = await dbContext.NotificationOutbox.FirstAsync();
        Assert.Equal(3, item.Status); // Failed/Retryable
        
        // Next attempt should be pushed out by roughly 45 seconds
        var diff = item.NextAttemptAtUtc - DateTime.UtcNow;
        Assert.True(diff.TotalSeconds > 40 && diff.TotalSeconds < 50);
    }

    [Fact]
    public async Task ProcessOutboxBatch_RecoversExpiredLease()
    {
        await using var dbContext = CreateInMemoryDbContext();

        long userId = 123;
        dbContext.Users.Add(new UserEntity { Id = userId, CreatedAtUtc = DateTime.UtcNow });

        var payload = new OutboxPayload { AdId = 1, Title = "Test Ad" };
        var outboxItem = new NotificationOutboxEntity
        {
            UserId = userId,
            SearchId = 1,
            AdId = 1,
            Status = 1, // Sending
            LockedUntilUtc = DateTime.UtcNow.AddMinutes(-5), // Expired 5 mins ago
            PayloadJson = JsonSerializer.Serialize(payload),
            AttemptCount = 0,
            MaxAttempts = 5,
            NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(-10),
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.NotificationOutbox.Add(outboxItem);
        await dbContext.SaveChangesAsync();

        var mockSender = new Mock<ITelegramSender>();
        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton(mockSender.Object);

        var worker = new OutboxDeliveryWorker(services.BuildServiceProvider(), NullLogger<OutboxDeliveryWorker>.Instance);

        await worker.ProcessOutboxBatchAsync(CancellationToken.None);

        // Expired lease should be recovered and immediately processed in the same run
        var item = await dbContext.NotificationOutbox.FirstAsync();
        Assert.Equal(2, item.Status); // Ultimately sent successfully
        Assert.Null(item.LockedUntilUtc);
        mockSender.Verify(s => s.SendTextMessageAsync(userId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static NotificationOutboxEntity MakePhotoOutboxItem(long userId, OutboxPayload payload)
    {
        return new NotificationOutboxEntity
        {
            UserId = userId,
            SearchId = 1,
            AdId = payload.AdId,
            Status = 0,
            PayloadJson = JsonSerializer.Serialize(payload),
            AttemptCount = 0,
            MaxAttempts = 5,
            NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(-1),
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task ProcessOutboxBatch_PhotoAvailable_SendsPhotoInsteadOfText()
    {
        await using var dbContext = CreateInMemoryDbContext();

        long userId = 555;
        dbContext.Users.Add(new UserEntity { Id = userId, CreatedAtUtc = DateTime.UtcNow });

        var payload = new OutboxPayload
        {
            AdId = 1,
            Title = "Test Ad",
            PriceFormatted = "100 BYN",
            Location = "Minsk",
            AdLink = "http://link",
            ImageUrl = "https://rms.kufar.by/v1/list_thumbs_2x/a/1.jpg",
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.NotificationOutbox.Add(MakePhotoOutboxItem(userId, payload));
        await dbContext.SaveChangesAsync();

        var mockSender = new Mock<ITelegramSender>();
        mockSender.Setup(s => s.SendPhotoAsync(userId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton(mockSender.Object);

        var worker = new OutboxDeliveryWorker(services.BuildServiceProvider(), NullLogger<OutboxDeliveryWorker>.Instance);

        await worker.ProcessOutboxBatchAsync(CancellationToken.None);

        var item = await dbContext.NotificationOutbox.FirstAsync();
        Assert.Equal(2, item.Status); // Sent

        mockSender.Verify(s => s.SendPhotoAsync(userId, payload.ImageUrl!, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        mockSender.Verify(s => s.SendTextMessageAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessOutboxBatch_PhotoFails_FallsBackToTextMessage()
    {
        await using var dbContext = CreateInMemoryDbContext();

        long userId = 556;
        dbContext.Users.Add(new UserEntity { Id = userId, CreatedAtUtc = DateTime.UtcNow });

        var payload = new OutboxPayload
        {
            AdId = 2,
            Title = "Broken image",
            PriceFormatted = "50 BYN",
            Location = "Minsk",
            AdLink = "http://link2",
            ImageUrl = "https://rms.kufar.by/v1/list_thumbs_2x/broken.jpg",
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.NotificationOutbox.Add(MakePhotoOutboxItem(userId, payload));
        await dbContext.SaveChangesAsync();

        var mockSender = new Mock<ITelegramSender>();
        mockSender.Setup(s => s.SendPhotoAsync(userId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiRequestException("Wrong file identifier/HTTP URL specified", 400));
        mockSender.Setup(s => s.SendTextMessageAsync(userId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton(mockSender.Object);

        var worker = new OutboxDeliveryWorker(services.BuildServiceProvider(), NullLogger<OutboxDeliveryWorker>.Instance);

        await worker.ProcessOutboxBatchAsync(CancellationToken.None);

        var item = await dbContext.NotificationOutbox.FirstAsync();
        Assert.Equal(2, item.Status); // Delivered as text, not lost and not retried as failure

        mockSender.Verify(s => s.SendPhotoAsync(userId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        mockSender.Verify(s => s.SendTextMessageAsync(userId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}

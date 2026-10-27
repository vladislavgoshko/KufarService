using System;
using KufarBot.Infrastructure.Polling;
using Xunit;

namespace KufarBot.Tests;

/// <summary>eph-13: anti-ban exponential backoff for failing polling feeds.</summary>
public class FeedBackoffTests
{
    [Fact]
    public void ComputeBackoffDelay_DoublesEachRetry()
    {
        Assert.Equal(TimeSpan.FromSeconds(90), FeedPollingWorker.ComputeBackoffDelay(1, 90, 1800));
        Assert.Equal(TimeSpan.FromSeconds(180), FeedPollingWorker.ComputeBackoffDelay(2, 90, 1800));
        Assert.Equal(TimeSpan.FromSeconds(360), FeedPollingWorker.ComputeBackoffDelay(3, 90, 1800));
    }

    [Fact]
    public void ComputeBackoffDelay_CapsAtMax()
    {
        Assert.Equal(TimeSpan.FromSeconds(1800), FeedPollingWorker.ComputeBackoffDelay(10, 90, 1800));
        Assert.Equal(TimeSpan.FromSeconds(1800), FeedPollingWorker.ComputeBackoffDelay(100, 90, 1800));
    }

    [Fact]
    public void ComputeBackoffDelay_ZeroOrNegativeErrors_TreatedAsFirst()
    {
        Assert.Equal(TimeSpan.FromSeconds(90), FeedPollingWorker.ComputeBackoffDelay(0, 90, 1800));
        Assert.Equal(TimeSpan.FromSeconds(90), FeedPollingWorker.ComputeBackoffDelay(-5, 90, 1800));
    }

    [Fact]
    public void ComputeBackoffDelay_JitterStaysWithinTwentyPercent()
    {
        var rng = new Random(4200);

        for (int n = 1; n <= 12; n++)
        {
            double expected = Math.Min(90 * Math.Pow(2, n - 1), 1800);
            var actual = FeedPollingWorker.ComputeBackoffDelay(n, 90, 1800, rng);

            Assert.InRange(actual.TotalSeconds, expected * 0.8, expected * 1.2);
        }
    }
}

using AkkornStudio.Metadata;
using Xunit;

namespace AkkornStudio.Tests.Unit.Metadata;

public sealed class CanvasTableTrackerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Add_WithInvalidName_Throws(string? tableName)
    {
        ICanvasTableTracker tracker = new CanvasTableTracker();

        Assert.ThrowsAny<ArgumentException>(() => tracker.Add(tableName!));
    }

    [Fact]
    public void AddAndContains_AreCaseInsensitive()
    {
        ICanvasTableTracker tracker = new CanvasTableTracker();

        tracker.Add("public.orders");

        Assert.True(tracker.Contains("PUBLIC.ORDERS"));
        Assert.Equal(1, tracker.Count);
    }

    [Fact]
    public void Add_TrimmedName_IsStoredWithoutOuterSpaces()
    {
        ICanvasTableTracker tracker = new CanvasTableTracker();

        tracker.Add("  public.orders  ");

        Assert.True(tracker.Contains("public.orders"));
        Assert.Equal("public.orders", Assert.Single(tracker.Snapshot()));
    }

    [Fact]
    public void Remove_DeletesExistingEntry()
    {
        ICanvasTableTracker tracker = new CanvasTableTracker();
        tracker.Add("public.orders");

        bool removed = tracker.Remove("public.orders");

        Assert.True(removed);
        Assert.False(tracker.Contains("public.orders"));
        Assert.Equal(0, tracker.Count);
    }

    [Fact]
    public void Remove_WhenEntryDoesNotExist_ReturnsFalse()
    {
        ICanvasTableTracker tracker = new CanvasTableTracker();
        tracker.Add("public.orders");

        bool removed = tracker.Remove("public.customers");

        Assert.False(removed);
        Assert.True(tracker.Contains("public.orders"));
        Assert.Equal(1, tracker.Count);
    }

    [Fact]
    public void Remove_UnqualifiedName_WithSingleMatch_RemovesQualifiedEntry()
    {
        ICanvasTableTracker tracker = new CanvasTableTracker();
        tracker.Add("dbo.orders");

        bool removed = tracker.Remove("orders");

        Assert.True(removed);
        Assert.False(tracker.Contains("dbo.orders"));
        Assert.Equal(0, tracker.Count);
    }

    [Fact]
    public void Remove_UnqualifiedName_WithMultipleMatches_ReturnsFalse()
    {
        ICanvasTableTracker tracker = new CanvasTableTracker();
        tracker.Add("dbo.orders");
        tracker.Add("sales.orders");

        bool removed = tracker.Remove("orders");

        Assert.False(removed);
        Assert.Equal(2, tracker.Count);
    }

    [Fact]
    public void Contains_UnqualifiedName_MatchesQualifiedEntry()
    {
        ICanvasTableTracker tracker = new CanvasTableTracker();
        tracker.Add("dbo.orders");

        Assert.True(tracker.Contains("orders"));
        Assert.False(tracker.Contains("customers"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Contains_WithInvalidName_Throws(string? tableName)
    {
        ICanvasTableTracker tracker = new CanvasTableTracker();

        Assert.ThrowsAny<ArgumentException>(() => tracker.Contains(tableName!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Remove_WithInvalidName_Throws(string? tableName)
    {
        ICanvasTableTracker tracker = new CanvasTableTracker();

        Assert.ThrowsAny<ArgumentException>(() => tracker.Remove(tableName!));
    }

    [Fact]
    public async Task Snapshot_IsStableDuringConcurrentWrites()
    {
        ICanvasTableTracker tracker = new CanvasTableTracker();
        Task[] adds = Enumerable
            .Range(0, 50)
            .Select(i => Task.Run(() => tracker.Add($"public.t{i}")))
            .ToArray();

        await Task.WhenAll(adds);
        IReadOnlyList<string> snapshot = tracker.Snapshot();

        Assert.Equal(50, snapshot.Count);
        Assert.Equal(50, tracker.Count);
    }
}

using AkkornStudio.Core;
using AkkornStudio.Metadata;

namespace AkkornStudio.Tests.Unit.Metadata;

public sealed class MetadataSnapshotCacheTests
{
    [Fact]
    public async Task GetOrLoadAsync_Throws_WhenLoaderIsNull()
    {
        using var cache = new MetadataSnapshotCache(TimeSpan.FromMinutes(1));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            cache.GetOrLoadAsync(null!, forceRefresh: false));
    }

    [Fact]
    public async Task GetOrLoadAsync_UsesCache_WhenEntryIsFresh()
    {
        using var cache = new MetadataSnapshotCache(TimeSpan.FromMinutes(1));
        int calls = 0;

        DbMetadata first = await cache.GetOrLoadAsync(_ =>
        {
            calls++;
            return Task.FromResult(MetadataFixtures.EcommerceDb());
        }, forceRefresh: false);

        DbMetadata second = await cache.GetOrLoadAsync(_ =>
        {
            calls++;
            return Task.FromResult(MetadataFixtures.EcommerceDb());
        }, forceRefresh: false);

        Assert.Equal(1, calls);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task GetOrLoadAsync_ForceRefresh_BypassesFreshCache()
    {
        using var cache = new MetadataSnapshotCache(TimeSpan.FromMinutes(1));
        int calls = 0;

        _ = await cache.GetOrLoadAsync(_ =>
        {
            calls++;
            return Task.FromResult(MetadataFixtures.EcommerceDb());
        }, forceRefresh: false);

        _ = await cache.GetOrLoadAsync(_ =>
        {
            calls++;
            return Task.FromResult(MetadataFixtures.EcommerceDb());
        }, forceRefresh: true);

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task GetOrLoadAsync_ExpiredEntry_ReloadsMetadata()
    {
        using var cache = new MetadataSnapshotCache(TimeSpan.FromMilliseconds(1));
        int calls = 0;

        _ = await cache.GetOrLoadAsync(_ =>
        {
            calls++;
            return Task.FromResult(MetadataFixtures.EcommerceDb());
        }, forceRefresh: false);

        await Task.Delay(10);

        _ = await cache.GetOrLoadAsync(_ =>
        {
            calls++;
            return Task.FromResult(MetadataFixtures.EcommerceDb());
        }, forceRefresh: false);

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task GetOrLoadAsync_ConcurrentCalls_UsesSingleLoaderInvocation()
    {
        using var cache = new MetadataSnapshotCache(TimeSpan.FromMinutes(1));
        int calls = 0;

        Task<DbMetadata>[] loads =
        [
            cache.GetOrLoadAsync(async _ =>
            {
                Interlocked.Increment(ref calls);
                await Task.Delay(50);
                return MetadataFixtures.EcommerceDb();
            }, forceRefresh: false),
            cache.GetOrLoadAsync(async _ =>
            {
                Interlocked.Increment(ref calls);
                await Task.Delay(50);
                return MetadataFixtures.EcommerceDb();
            }, forceRefresh: false),
            cache.GetOrLoadAsync(async _ =>
            {
                Interlocked.Increment(ref calls);
                await Task.Delay(50);
                return MetadataFixtures.EcommerceDb();
            }, forceRefresh: false)
        ];

        DbMetadata[] all = await Task.WhenAll(loads);

        Assert.Equal(1, calls);
        Assert.True(all.All(item => ReferenceEquals(item, all[0])));
    }

    [Fact]
    public async Task Constructor_WithNonPositiveTtl_FallsBackToDefaultCacheTtl()
    {
        using var cache = new MetadataSnapshotCache(TimeSpan.Zero);
        int calls = 0;

        _ = await cache.GetOrLoadAsync(_ =>
        {
            calls++;
            return Task.FromResult(MetadataFixtures.EcommerceDb());
        }, forceRefresh: false);

        _ = await cache.GetOrLoadAsync(_ =>
        {
            calls++;
            return Task.FromResult(MetadataFixtures.EcommerceDb());
        }, forceRefresh: false);

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ReplaceTable_Throws_WhenCacheIsEmpty()
    {
        using var cache = new MetadataSnapshotCache(TimeSpan.FromMinutes(1));
        TableMetadata fresh = MetadataFixtures.Table("public", "orders", [MetadataFixtures.Col("id")]);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            cache.ReplaceTable(fresh));

        Assert.Equal("Cannot replace table because metadata cache is empty.", ex.Message);
    }

    [Fact]
    public async Task ReplaceTable_UpdatesMatchingTable_InCachedSnapshot()
    {
        using var cache = new MetadataSnapshotCache(TimeSpan.FromMinutes(1));
        _ = await cache.GetOrLoadAsync(_ => Task.FromResult(MetadataFixtures.EcommerceDb()), forceRefresh: false);

        TableMetadata fresh = MetadataFixtures.Table(
            "public",
            "orders",
            [
                MetadataFixtures.Col("id", isPk: true, isNullable: false),
                MetadataFixtures.Col("customer_id", isFk: true, isNullable: false),
                MetadataFixtures.Col("hardening_flag", type: "varchar")
            ]);

        cache.ReplaceTable(fresh);

        DbMetadata reloaded = await cache.GetOrLoadAsync(
            _ => throw new InvalidOperationException("Loader should not run for fresh cache."),
            forceRefresh: false);

        TableMetadata? replaced = reloaded.FindTable("public", "orders");
        Assert.NotNull(replaced);
        Assert.Contains(replaced.Columns, c => c.Name == "hardening_flag");
    }

    [Fact]
    public async Task ReplaceTable_WhenSchemaDoesNotMatch_LeavesCacheUnchanged()
    {
        using var cache = new MetadataSnapshotCache(TimeSpan.FromMinutes(1));
        DbMetadata original = await cache.GetOrLoadAsync(_ => Task.FromResult(MetadataFixtures.EcommerceDb()), forceRefresh: false);

        TableMetadata fresh = MetadataFixtures.Table("sales", "orders", [MetadataFixtures.Col("id")]);

        cache.ReplaceTable(fresh);

        DbMetadata reloaded = await cache.GetOrLoadAsync(
            _ => throw new InvalidOperationException("Loader should not run for fresh cache."),
            forceRefresh: false);

        Assert.Equal(original.DatabaseName, reloaded.DatabaseName);
        Assert.Equal(original.Provider, reloaded.Provider);
        Assert.Equal(original.TotalTables, reloaded.TotalTables);
        Assert.Equal(original.TotalForeignKeys, reloaded.TotalForeignKeys);
        Assert.Null(reloaded.FindTable("sales", "orders"));
    }

    [Fact]
    public async Task Invalidate_ClearsCache_AndForcesReload()
    {
        using var cache = new MetadataSnapshotCache(TimeSpan.FromMinutes(1));
        int calls = 0;

        _ = await cache.GetOrLoadAsync(_ =>
        {
            calls++;
            return Task.FromResult(MetadataFixtures.EcommerceDb());
        }, forceRefresh: false);

        cache.Invalidate();

        _ = await cache.GetOrLoadAsync(_ =>
        {
            calls++;
            return Task.FromResult(MetadataFixtures.EcommerceDb());
        }, forceRefresh: false);

        Assert.Equal(2, calls);
    }

    [Fact]
    public void Dispose_CanBeCalledTwice()
    {
        using var cache = new MetadataSnapshotCache(TimeSpan.FromMinutes(1));
        cache.Dispose();
        cache.Dispose();
    }
}

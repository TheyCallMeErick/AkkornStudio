namespace AkkornStudio.Metadata;

public interface IMetadataSnapshotCache : IDisposable
{
    Task<DbMetadata> GetOrLoadAsync(
        Func<CancellationToken, Task<DbMetadata>> loader,
        bool forceRefresh,
        CancellationToken ct = default
    );
    void ReplaceTable(TableMetadata fresh);
    void Invalidate();
}

internal sealed record CacheEntry(DbMetadata Metadata, DateTimeOffset ExpiresAt);

public sealed class MetadataSnapshotCache(TimeSpan ttl) : IMetadataSnapshotCache
{
    private readonly TimeSpan _ttl = ttl > TimeSpan.Zero ? ttl : MetadataServiceOptions.DefaultCacheTtl;
    private readonly SemaphoreSlim _fetchLock = new(1, 1);
    private volatile CacheEntry? _cache;
    private bool _disposed;

    public async Task<DbMetadata> GetOrLoadAsync(
        Func<CancellationToken, Task<DbMetadata>> loader,
        bool forceRefresh,
        CancellationToken ct = default
    )
    {
        ArgumentNullException.ThrowIfNull(loader);

        CacheEntry? cached = _cache;
        if (!forceRefresh && IsFresh(cached))
            return cached!.Metadata;

        await _fetchLock.WaitAsync(ct);
        try
        {
            cached = _cache;
            if (!forceRefresh && IsFresh(cached))
                return cached!.Metadata;

            DbMetadata metadata = await loader(ct);
            _cache = new CacheEntry(metadata, DateTimeOffset.UtcNow.Add(_ttl));
            return metadata;
        }
        finally
        {
            _fetchLock.Release();
        }
    }

    public void ReplaceTable(TableMetadata fresh)
    {
        ArgumentNullException.ThrowIfNull(fresh);

        CacheEntry? cached = _cache;
        if (cached is null)
            throw new InvalidOperationException("Cannot replace table because metadata cache is empty.");

        DbMetadata metadata = cached.Metadata;
        var schemas = metadata.Schemas
            .Select(schema =>
            {
                if (!schema.Name.Equals(fresh.Schema, StringComparison.OrdinalIgnoreCase))
                    return schema;

                var tables = schema.Tables
                    .Select(table =>
                        table.FullName.Equals(fresh.FullName, StringComparison.OrdinalIgnoreCase)
                            ? fresh
                            : table
                    )
                    .ToList();

                return schema with
                {
                    Tables = tables,
                };
            })
            .ToList();

        _cache = cached with
        {
            Metadata = metadata with
            {
                Schemas = schemas,
            },
        };
    }

    public void Invalidate() => _cache = null;

    public void Dispose()
    {
        if (_disposed)
            return;

        _fetchLock.Dispose();
        _disposed = true;
    }

    private static bool IsFresh(CacheEntry? entry) =>
        entry is not null && DateTimeOffset.UtcNow < entry.ExpiresAt;
}

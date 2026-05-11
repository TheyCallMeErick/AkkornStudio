using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Data;
using AkkornStudio;
using AkkornStudio.Core;
using AkkornStudio.Metadata;
using AkkornStudio.Nodes;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.Canvas;

namespace AkkornStudio.UI.Services.Connection;

/// <summary>
/// Orchestrates the database connection workflow:
/// 1. Establishes connection to the database
/// 2. Fetches and introspects database tables
/// 3. Loads tables into the search menu for quick access
/// 4. Initializes metadata service for auto-join suggestions
///
/// This service ensures that after a successful database connection,
/// the normal flow of operations is triggered automatically.
/// </summary>
public sealed class DatabaseConnectionService : IDisposable
{
    private readonly ILogger<DatabaseConnectionService> _logger;
    private MetadataService? _metadataService;
    private CancellationTokenSource? _operationCts;
    private DbMetadata? _loadedMetadata;
    private ConnectionConfig? _activeConfig;
    private bool _disposed;

    /// <summary>
    /// The most recently loaded database metadata.
    /// Null if no database has been connected or loading failed.
    /// </summary>
    public DbMetadata? LoadedMetadata => _loadedMetadata;

    public DatabaseConnectionService(ILogger<DatabaseConnectionService>? logger = null)
    {
        _logger = logger ?? NullLogger<DatabaseConnectionService>.Instance;
    }

    /// <summary>
    /// Executes the complete database connection workflow:
    /// - Tests connection
    /// - Loads schema metadata
    /// - Populates search menu with tables
    /// - Initializes metadata service for auto-join detection
    /// </summary>
    public async Task ConnectAndLoadAsync(
        ConnectionConfig config,
        SearchMenuViewModel searchMenu,
        CancellationToken ct = default
    )
    {
        // Cancel any previous operation
        _operationCts?.Cancel();
        _operationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        CancellationToken linked = _operationCts.Token;

        try
        {
            _logger.LogInformation(
                "[DatabaseConnectionService] Starting connection workflow for {Provider} @ {Host}:{Port}/{Database}",
                config.Provider,
                config.Host,
                config.Port,
                config.Database
            );

            // Step 1: Initialize the metadata service
            _metadataService = MetadataService.Create(config);
            _activeConfig = config;

            // Step 2: Fetch complete database schema
            _logger.LogInformation("[DatabaseConnectionService] Fetching database schema...");
            var metadata = await _metadataService.GetMetadataAsync(forceRefresh: true, ct: linked);

            // Store the loaded metadata for later access
            _loadedMetadata = metadata;

            _logger.LogInformation(
                "[DatabaseConnectionService] Schema loaded: {Tables} tables, {Views} views, {FKs} foreign keys",
                metadata.TotalTables,
                metadata.TotalViews,
                metadata.TotalForeignKeys
            );

            // Step 3: Convert metadata to SearchMenu format and load
            var tables = ConvertMetadataToTableList(metadata);
            _logger.LogInformation(
                "[DatabaseConnectionService] Loading {Count} tables into search menu",
                tables.Count
            );

            searchMenu.LoadTables(tables);

            _logger.LogInformation(
                "[DatabaseConnectionService] Database connection workflow completed successfully"
            );
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[DatabaseConnectionService] Connection workflow cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[DatabaseConnectionService] Error during connection workflow: {Message}",
                ex.Message
            );
            throw;
        }
    }

    /// <summary>
    /// Converts DbMetadata to the tuple format expected by SearchMenuViewModel.LoadTables().
    /// Flattens all schemas and returns (FullName, Columns) pairs.
    /// </summary>
    private static List<(string FullName, IReadOnlyList<(string Name, PinDataType Type)> Cols)>
        ConvertMetadataToTableList(DbMetadata metadata)
    {
        var result = new List<(string, IReadOnlyList<(string, PinDataType)>)>();

        foreach (var schema in metadata.Schemas)
        {
            foreach (var table in schema.Tables)
            {
                // Build full name: schema.tablename
                string fullName = $"{table.Schema}.{table.Name}";

                // Convert columns to (Name, PinDataType) tuples
                var columns = table.Columns
                    .OrderBy(c => c.OrdinalPosition)
                    .Select(c => (
                        Name: c.Name,
                        Type: MapDataTypeToPinDataType(c)
                    ))
                    .ToList();

                result.Add((fullName, columns.AsReadOnly()));
            }
        }

        return result;
    }

    /// <summary>
    /// Maps database column semantics to UI pin data types.
    /// Used for proper icon and color rendering in the canvas.
    /// </summary>
    private static PinDataType MapDataTypeToPinDataType(ColumnMetadata column)
    {
        return column.SemanticType switch
        {
            ColumnSemanticType.Numeric => PinDataType.Number,
            ColumnSemanticType.Text => PinDataType.Text,
            ColumnSemanticType.DateTime => PinDataType.DateTime,
            ColumnSemanticType.Boolean => PinDataType.Boolean,
            ColumnSemanticType.Guid => PinDataType.Text, // GUIDs rendered as text in pins
            ColumnSemanticType.Document => PinDataType.Json,
            ColumnSemanticType.Binary => PinDataType.Text, // Binary data rendered as text
            ColumnSemanticType.Spatial => PinDataType.Json, // Spatial types as JSON
            ColumnSemanticType.Other => PinDataType.Text,
            _ => PinDataType.Text,
        };
    }

    /// <summary>
    /// Returns the active metadata service if connected, or null if not.
    /// Used for features like auto-join detection that require database schema.
    /// </summary>
    public MetadataService? GetActiveMetadataService() => _metadataService;

    /// <summary>
    /// Lists available databases/schemas for the active connection when provider supports it.
    /// </summary>
    public async Task<string[]> ListDatabasesAsync(CancellationToken ct = default)
    {
        if (_activeConfig is null)
            return [];

        if (_activeConfig.Provider == DatabaseProvider.SQLite)
            return [Path.GetFileNameWithoutExtension(_activeConfig.Database)];

        string sql = _activeConfig.Provider switch
        {
            DatabaseProvider.Postgres => "SELECT datname FROM pg_database WHERE datallowconn = TRUE ORDER BY datname;",
            DatabaseProvider.MySql => "SELECT SCHEMA_NAME FROM INFORMATION_SCHEMA.SCHEMATA ORDER BY SCHEMA_NAME;",
            DatabaseProvider.SqlServer => "SELECT name FROM sys.databases WHERE state = 0 ORDER BY name;",
            _ => string.Empty,
        };

        if (string.IsNullOrWhiteSpace(sql))
            return [];

        try
        {
            DataTable? table = await ExecutePreviewAsTableAsync(
                _activeConfig,
                sql,
                PreviewExecutionOptions.NoLimit,
                ct);
            if (table is null || table.Columns.Count == 0)
                return [];

            string[] names = table.Rows
                .Cast<DataRow>()
                .Select(r => r[0]?.ToString())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return names;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list databases for provider {Provider}", _activeConfig.Provider);
            return [];
        }
    }

    /// <summary>
    /// Switches the active database by updating the active connection config and validating connectivity.
    /// </summary>
    public async Task SwitchDatabaseAsync(string databaseName, CancellationToken ct = default)
    {
        if (_activeConfig is null || string.IsNullOrWhiteSpace(databaseName))
            return;

        if (string.Equals(_activeConfig.Database, databaseName, StringComparison.OrdinalIgnoreCase))
            return;

        ConnectionConfig switched = _activeConfig with { Database = databaseName };
        var factory = DbOrchestratorFactory.CreateDefault();
        await using IDbOrchestrator orchestrator = factory.Create(switched);
        ConnectionTestResult result = await orchestrator.TestConnectionAsync(ct);
        if (!result.Success)
            throw new InvalidOperationException(result.ErrorMessage ?? $"Could not switch database to '{databaseName}'.");

        (_metadataService as IDisposable)?.Dispose();
        _metadataService = null;
        _loadedMetadata = null;
        _activeConfig = switched;
    }

    /// <summary>
    /// Best-effort read permission probe for a specific database/catalog.
    /// Returns true when metadata read succeeds, false when access fails, null when no active config exists.
    /// </summary>
    public async Task<bool?> HasReadAccessToDatabaseAsync(string databaseName, CancellationToken ct = default)
    {
        if (_activeConfig is null || string.IsNullOrWhiteSpace(databaseName))
            return null;

        ConnectionConfig target = _activeConfig with { Database = databaseName };
        string sql = target.Provider switch
        {
            DatabaseProvider.Postgres => "SELECT 1 FROM information_schema.tables LIMIT 1;",
            DatabaseProvider.MySql => "SELECT 1 FROM information_schema.tables LIMIT 1;",
            DatabaseProvider.SqlServer => "SELECT TOP 1 1 FROM INFORMATION_SCHEMA.TABLES;",
            DatabaseProvider.SQLite => "SELECT 1;",
            _ => "SELECT 1;",
        };

        try
        {
            _ = await ExecutePreviewAsTableAsync(target, sql, 1, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Read permission probe failed for database {Database}", databaseName);
            return false;
        }
    }

    /// <summary>
    /// Returns a human-friendly server version string for the active connection.
    /// </summary>
    public async Task<string?> GetServerVersionAsync(CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(_loadedMetadata?.ServerVersion))
            return _loadedMetadata.ServerVersion;

        if (_activeConfig is null)
            return null;

        string sql = _activeConfig.Provider switch
        {
            DatabaseProvider.Postgres => "SELECT version();",
            DatabaseProvider.MySql => "SELECT VERSION();",
            DatabaseProvider.SqlServer => "SELECT @@VERSION;",
            DatabaseProvider.SQLite => "SELECT sqlite_version();",
            _ => string.Empty,
        };

        if (string.IsNullOrWhiteSpace(sql))
            return null;

        try
        {
            DataTable? table = await ExecutePreviewAsTableAsync(_activeConfig, sql, 1, ct);
            if (table is null || table.Rows.Count == 0 || table.Columns.Count == 0)
                return null;

            return table.Rows[0][0]?.ToString()?.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not fetch server version for provider {Provider}", _activeConfig.Provider);
            return null;
        }
    }

    private static async Task<DataTable?> ExecutePreviewAsTableAsync(
        ConnectionConfig config,
        string sql,
        int maxRows,
        CancellationToken ct)
    {
        var factory = DbOrchestratorFactory.CreateDefault();
        await using IDbOrchestrator orchestrator = factory.Create(config);
        PreviewResult preview = await orchestrator.ExecutePreviewAsync(sql, maxRows, ct);
        return preview.Success ? preview.Data : null;
    }

    /// <summary>
    /// Cancels any in-progress connection operations.
    /// </summary>
    public void Cancel()
    {
        _operationCts?.Cancel();
    }

    /// <summary>
    /// Cleans up resources. Call when switching connections or closing the app.
    /// Properly disposes CancellationTokenSource and MetadataService if they are IDisposable.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        // Dispose the CancellationTokenSource
        _operationCts?.Dispose();
        _operationCts = null;

        // Dispose the MetadataService if not null
        if (_metadataService is not null)
        {
            (_metadataService as IDisposable)?.Dispose();
        }
        _metadataService = null;
        _activeConfig = null;

        _loadedMetadata = null;
        _disposed = true;
    }
}

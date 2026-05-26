using System.Data;
using System.IO;

namespace AkkornStudio.Core;

// ─── Schema Models ────────────────────────────────────────────────────────────

public record ColumnSchema(
    string Name,
    string DataType,
    bool IsNullable,
    bool IsPrimaryKey,
    bool IsForeignKey,
    string? ForeignKeyTable = null,
    int? MaxLength = null
);

public record TableSchema(string Schema, string Name, IReadOnlyList<ColumnSchema> Columns)
{
    public string FullName => string.IsNullOrEmpty(Schema) ? Name : $"{Schema}.{Name}";
}

public record DatabaseSchema(
    string DatabaseName,
    DatabaseProvider Provider,
    IReadOnlyList<TableSchema> Tables
);

// ─── Connection / Execution Models ───────────────────────────────────────────

public record ConnectionConfig(
    DatabaseProvider Provider,
    string Host,
    int Port,
    string Database,
    string Username,
    string Password,
    bool UseIntegratedSecurity = false,
    int TimeoutSeconds = 30,
    IDictionary<string, string>? ExtraParameters = null
)
{
    private bool GetExtraBool(string key, bool defaultValue = false)
    {
        if (ExtraParameters is null)
            return defaultValue;

        if (!ExtraParameters.TryGetValue(key, out string? value))
            return defaultValue;

        return bool.TryParse(value, out bool parsed) ? parsed : defaultValue;
    }

    public string BuildConnectionString() =>
        Provider switch
        {
            DatabaseProvider.SqlServer => BuildSqlServerCs(),
            DatabaseProvider.MySql => BuildMySqlCs(),
            DatabaseProvider.Postgres => BuildPostgresCs(),
            DatabaseProvider.SQLite => BuildSqliteCs(),
            _ => throw new NotSupportedException($"Provider {Provider} is not supported."),
        };

    private string BuildSqlServerCs()
    {
        string auth = UseIntegratedSecurity
            ? "Integrated Security=True"
            : $"User Id={Username};Password={Password}";

        bool trustServerCertificate = GetExtraBool("TrustServerCertificate", defaultValue: true);
        bool useSsl = GetExtraBool("UseSsl");
        string encrypt = useSsl ? "True" : "False";

        return $"Server={Host},{Port};Database={Database};{auth};Encrypt={encrypt};TrustServerCertificate={trustServerCertificate};Connection Timeout={TimeoutSeconds};";
    }

    private string BuildMySqlCs()
    {
        bool useSsl = GetExtraBool("UseSsl");
        string sslMode = useSsl ? "Required" : "None";
        return $"Server={Host};Port={Port};Database={Database};Uid={Username};Pwd={Password};SslMode={sslMode};ConnectionTimeout={TimeoutSeconds};";
    }

    private string BuildPostgresCs()
    {
        bool useSsl = GetExtraBool("UseSsl");
        string sslMode = useSsl ? "Require" : "Disable";
        return $"Host={Host};Port={Port};Database={Database};Username={Username};Password={Password};Timeout={TimeoutSeconds};SSL Mode={sslMode};";
    }

    private string BuildSqliteCs()
    {
        string databasePath = (Database ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(databasePath))
            throw new InvalidOperationException("SQLite database path cannot be null or empty.");

        if (databasePath.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
            return $"Data Source={databasePath};Default Timeout={TimeoutSeconds};";

        if (ContainsParentTraversalSegment(databasePath))
        {
            throw new InvalidOperationException(
                $"SQLite database path '{databasePath}' contains parent traversal ('..'), which is not allowed."
            );
        }

        string resolvedPath = Path.GetFullPath(databasePath);
        string? parentDirectory = Path.GetDirectoryName(resolvedPath);
        if (string.IsNullOrWhiteSpace(parentDirectory) || !Directory.Exists(parentDirectory))
        {
            throw new InvalidOperationException(
                $"SQLite database parent directory does not exist for path '{databasePath}'."
            );
        }

        return $"Data Source={resolvedPath};Default Timeout={TimeoutSeconds};";
    }

    private static bool ContainsParentTraversalSegment(string path)
    {
        string[] segments = path.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        return segments.Any(segment => segment.Equals("..", StringComparison.Ordinal));
    }
}

public record ConnectionTestResult(
    bool Success,
    string? ErrorMessage = null,
    TimeSpan? Latency = null
);

public record PreviewResult(
    bool Success,
    DataTable? Data = null,
    string? ErrorMessage = null,
    TimeSpan? ExecutionTime = null,
    long? RowsAffected = null
);

public record DdlStatementExecutionResult(
    int StatementIndex,
    string Sql,
    bool Success,
    string? ErrorMessage = null,
    long? RowsAffected = null
);

public record DdlExecutionResult(
    bool Success,
    IReadOnlyList<DdlStatementExecutionResult> Statements,
    TimeSpan? ExecutionTime = null
);

// ─── Provider Enum ────────────────────────────────────────────────────────────

public enum DatabaseProvider
{
    SqlServer,
    MySql,
    Postgres,
    SQLite,
}

// ─── Core Orchestrator Interface ──────────────────────────────────────────────

/// <summary>
/// Central contract for all database operations in AkkornStudio.
/// Each provider implements this interface, keeping the canvas nodes
/// completely agnostic of the underlying database engine.
/// </summary>
public interface IConnectionTester
{
    /// <summary>Validates the connection and returns latency metrics.</summary>
    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct = default);
}

public interface ISchemaIntrospector
{
    /// <summary>
    /// Introspects the database and returns a full schema snapshot
    /// including tables, columns, PKs and FK relationships.
    /// </summary>
    Task<DatabaseSchema> GetSchemaAsync(CancellationToken ct = default);
}

public interface IQueryExecutor
{
    /// <summary>
    /// Executes a read-only query preview, capped at <paramref name="maxRows"/>.
    /// Wraps the query in a transaction that is always rolled back.
    /// </summary>
    Task<PreviewResult> ExecutePreviewAsync(
        string sql,
        int maxRows = PreviewExecutionOptions.UseConfiguredDefault,
        CancellationToken ct = default
    );
}

public interface IDdlExecutor
{
    /// <summary>
    /// Executes DDL statements and returns a per-statement result summary.
    /// When <paramref name="stopOnError"/> is true, execution stops at the first failing statement.
    /// </summary>
    Task<DdlExecutionResult> ExecuteDdlAsync(
        string sql,
        bool stopOnError = true,
        CancellationToken ct = default
    );
}

public interface IDbOrchestrator
    : IConnectionTester, ISchemaIntrospector, IQueryExecutor, IDdlExecutor, IAsyncDisposable
{
    DatabaseProvider Provider { get; }
    ConnectionConfig Config { get; }
}

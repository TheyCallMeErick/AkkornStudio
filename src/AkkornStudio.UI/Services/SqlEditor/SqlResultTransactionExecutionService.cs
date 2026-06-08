using System.Data;
using System.Data.Common;
using System.Diagnostics;
using AkkornStudio.Core;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;

namespace AkkornStudio.UI.Services.SqlEditor;

public sealed class SqlResultTransactionExecutionService
{
    public bool SupportsProvider(DatabaseProvider provider)
    {
        return provider is DatabaseProvider.SqlServer
            or DatabaseProvider.MySql
            or DatabaseProvider.Postgres
            or DatabaseProvider.SQLite;
    }

    public async Task<SqlResultTransactionExecutionResult> ExecuteAsync(
        ConnectionConfig config,
        IReadOnlyList<string> statements,
        bool commitChanges,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(statements);

        if (!SupportsProvider(config.Provider))
        {
            return new SqlResultTransactionExecutionResult(
                Success: false,
                ExecutedStatements: 0,
                WasCommitted: false,
                WasRolledBack: false,
                ErrorMessage: $"Transactional execution is not supported for provider '{config.Provider}'.");
        }

        List<string> executableStatements = statements
            .Where(static statement => !string.IsNullOrWhiteSpace(statement))
            .Select(static statement => statement.Trim())
            .ToList();
        if (executableStatements.Count == 0)
        {
            return new SqlResultTransactionExecutionResult(
                Success: false,
                ExecutedStatements: 0,
                WasCommitted: false,
                WasRolledBack: false,
                ErrorMessage: "No executable SQL statements were generated.");
        }

        DbTransaction? transaction = null;
        int executedStatements = 0;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await using DbConnection connection = CreateConnection(config);
            await connection.OpenAsync(ct);
            transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

            foreach (string statement in executableStatements)
            {
                ct.ThrowIfCancellationRequested();
                await using DbCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = statement;
                command.CommandTimeout = config.TimeoutSeconds;
                await command.ExecuteNonQueryAsync(ct);
                executedStatements++;
            }

            if (commitChanges)
            {
                await transaction.CommitAsync(ct);
                stopwatch.Stop();
                return new SqlResultTransactionExecutionResult(
                    Success: true,
                    ExecutedStatements: executedStatements,
                    WasCommitted: true,
                    WasRolledBack: false,
                    ExecutionTime: stopwatch.Elapsed);
            }

            await transaction.RollbackAsync(ct);
            stopwatch.Stop();
            return new SqlResultTransactionExecutionResult(
                Success: true,
                ExecutedStatements: executedStatements,
                WasCommitted: false,
                WasRolledBack: true,
                ExecutionTime: stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return new SqlResultTransactionExecutionResult(
                Success: false,
                ExecutedStatements: executedStatements,
                WasCommitted: false,
                WasRolledBack: false,
                ErrorMessage: "Transactional execution was canceled.",
                ExecutionTime: stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            bool rollbackSucceeded = await TryRollbackAsync(transaction, ct);
            return new SqlResultTransactionExecutionResult(
                Success: false,
                ExecutedStatements: executedStatements,
                WasCommitted: false,
                WasRolledBack: rollbackSucceeded,
                ErrorMessage: ex.Message,
                ExecutionTime: stopwatch.Elapsed);
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    private static DbConnection CreateConnection(ConnectionConfig config)
    {
        string connectionString = config.BuildConnectionString();
        return config.Provider switch
        {
            DatabaseProvider.SqlServer => new SqlConnection(connectionString),
            DatabaseProvider.MySql => new MySqlConnection(connectionString),
            DatabaseProvider.Postgres => new NpgsqlConnection(connectionString),
            DatabaseProvider.SQLite => new SqliteConnection(connectionString),
            _ => throw new NotSupportedException($"Provider '{config.Provider}' is not supported."),
        };
    }

    private static async Task<bool> TryRollbackAsync(DbTransaction? transaction, CancellationToken ct)
    {
        if (transaction is null)
            return false;

        try
        {
            await transaction.RollbackAsync(ct);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

using System.Data;
using System.Data.Common;
using System.Collections;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using AkkornStudio.Core;
using AkkornStudio.Providers;
using AkkornStudio.UI.Services.Localization;

namespace AkkornStudio.UI.Services;

/// <summary>
/// Executes SQL queries against a connected database and returns results as DataTable.
/// Handles connection management, timeouts, and safe preview mode execution.
/// </summary>
public sealed partial class QueryExecutorService
{
    private readonly ILogger<QueryExecutorService> _logger;
    private const int DefaultCommandTimeoutSeconds = 300;

    public int CommandTimeoutSeconds { get; set; } = DefaultCommandTimeoutSeconds;

    public QueryExecutorService(ILogger<QueryExecutorService>? logger = null)
    {
        _logger = logger ?? NullLogger<QueryExecutorService>.Instance;
    }

    /// <summary>
    /// Executes a SQL query against the database and returns results.
    /// </summary>
    /// <param name="config">Database connection configuration</param>
    /// <param name="query">SQL query to execute</param>
    /// <param name="maxRows">Maximum number of rows to return (default 1000)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>DataTable containing query results</returns>
    public Task<DataTable> ExecuteQueryAsync(
        ConnectionConfig config,
        string query,
        int maxRows = 1000,
        CancellationToken ct = default) =>
        ExecuteQueryAsync(config, query, parameters: null, maxRows, ct);

    /// <summary>
    /// Executes a SQL query against the database and returns results, optionally binding
    /// named or positional parameters when the provider supports them.
    /// </summary>
    public async Task<DataTable> ExecuteQueryAsync(
        ConnectionConfig config,
        string query,
        IReadOnlyList<QueryParameter>? parameters,
        int maxRows = 1000,
        CancellationToken ct = default)
    {
        _logger.LogDebug(
            "ExecuteQueryAsync called with config={Config}, query length={QueryLength}, parameter count={ParameterCount}",
            config,
            query?.Length,
            parameters?.Count ?? 0
        );

        if (config is null)
        {
            _logger.LogWarning("Connection config is null, returning demo data");
            return BuildDemoDataTable();
        }
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException(
                L("queryExecutor.error.queryEmpty", "Query cannot be empty"),
                nameof(query)
            );

        try
        {
            _logger.LogInformation("Starting query execution on {Provider} ({Host}:{Port}/{Database})",
                config.Provider, config.Host, config.Port, config.Database);

            (string executionQuery, IReadOnlyList<QueryParameter>? executionParameters) =
                ExpandNamedListParameters(query, parameters);

            ValidatePreviewQuery(executionQuery, executionParameters);

            // Wrap query with LIMIT/TOP clause for safe preview
            string wrappedQuery = WrapWithPreviewLimit(executionQuery, config.Provider, maxRows);
            _logger.LogDebug("Wrapped query: {Query}", wrappedQuery);

            // Create orchestrator for the database provider
            var orchestrator = CreateOrchestrator(config.Provider, config);
            _logger.LogInformation("Created orchestrator for {Provider}", config.Provider);

            var result = await ExecuteQueryInternalAsync(orchestrator, wrappedQuery, executionParameters, ct);

            _logger.LogInformation("Query executed successfully. Rows: {RowCount}", result.Rows.Count);
            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Query execution was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute query: {Message}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Wraps a query with a LIMIT/TOP clause based on the database provider.
    /// This ensures preview queries don't return excessive data.
    /// </summary>
    private static string WrapWithPreviewLimit(string query, DatabaseProvider provider, int maxRows)
    {
        if (PreviewExecutionOptions.IsUnlimitedRequested(maxRows))
            return TrimTrailingSemicolon(query);

        int boundedMaxRows = Math.Clamp(maxRows, 1, 10_000);
        string trimmed = TrimTrailingSemicolon(query);

        return provider switch
        {
            DatabaseProvider.SqlServer =>
                $"SELECT TOP {boundedMaxRows} * FROM (\n{trimmed}\n) AS __preview",
            DatabaseProvider.MySql =>
                $"SELECT * FROM (\n{trimmed}\n) AS __preview LIMIT {boundedMaxRows}",
            DatabaseProvider.Postgres =>
                $"{trimmed}\nLIMIT {boundedMaxRows}",
            DatabaseProvider.SQLite =>
                $"SELECT * FROM (\n{trimmed}\n) AS __preview LIMIT {boundedMaxRows}",
            _ => throw new NotSupportedException(
                string.Format(
                    L("queryExecutor.error.providerNotSupported", "Provider {0} is not supported"),
                    provider
                )
            )
        };
    }

    private static void ValidatePreviewQuery(string query, IReadOnlyList<QueryParameter>? parameters = null)
    {
        string trimmed = query.Trim();

        // Reject multiple statements to avoid stacked execution.
        int semicolon = trimmed.IndexOf(';');
        if (semicolon >= 0)
        {
            string trailing = trimmed[(semicolon + 1)..].Trim();
            if (!IsCommentOnlyOrWhitespace(trailing))
                throw new ArgumentException(
                    L("queryExecutor.error.singleStatementOnly", "Preview accepts a single SQL statement only."),
                    nameof(query)
                );
        }

        string firstToken = Regex.Match(trimmed, @"^\s*(\w+)", RegexOptions.CultureInvariant)
            .Groups[1]
            .Value
            .ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(firstToken))
            throw new ArgumentException(
                L("queryExecutor.error.queryEmptyWithPeriod", "Query cannot be empty."),
                nameof(query)
            );

        string effectiveToken = firstToken;
        if (firstToken == "WITH" && TryResolveTokenAfterLeadingWith(trimmed, out string? postWithToken))
            effectiveToken = postWithToken!;

        // Preview mode must remain read-only.
        if (effectiveToken is "INSERT" or "UPDATE" or "DELETE" or "MERGE" or "TRUNCATE" or
            "DROP" or "ALTER" or "CREATE" or "REPLACE" or "GRANT" or "REVOKE" or "CALL" or "EXEC")
            throw new ArgumentException(
                L("queryExecutor.error.readOnlyOnly", "Preview mode only supports read-only SQL statements."),
                nameof(query)
            );

        ValidateParameterBoundaries(trimmed, query, parameters);
    }

    private static void ValidateParameterBoundaries(
        string trimmedQuery,
        string originalQuery,
        IReadOnlyList<QueryParameter>? parameters)
    {
        IReadOnlyList<QueryParameterPlaceholder> placeholders = QueryParameterPlaceholderParser.Parse(trimmedQuery);
        IReadOnlyList<QueryParameterPlaceholder> namedPlaceholders = placeholders
            .Where(static placeholder => placeholder.Kind == QueryParameterPlaceholderKind.Named)
            .ToArray();
        IReadOnlyList<QueryParameterPlaceholder> positionalPlaceholders = placeholders
            .Where(static placeholder => placeholder.Kind == QueryParameterPlaceholderKind.Positional)
            .ToArray();
        if (namedPlaceholders.Count == 0 && positionalPlaceholders.Count == 0)
            return;

        if (parameters is null || parameters.Count == 0)
        {
            if (namedPlaceholders.Count > 0)
                throw new ArgumentException(
                    L(
                        "queryExecutor.error.namedParametersNotSupported",
                        "Preview mode does not support bound parameters in execution SQL. Inline safe literals or run the query outside preview."
                    ),
                    nameof(originalQuery)
                );

            throw new ArgumentException(
                L(
                    "queryExecutor.error.positionalParametersNotSupported",
                    "Preview mode does not support positional parameter placeholders ('?' or '$1')."
                ),
                nameof(originalQuery)
            );
        }

        IReadOnlyDictionary<string, QueryParameter> namedParameters = parameters
            .Where(static parameter => !string.IsNullOrWhiteSpace(parameter.Name))
            .GroupBy(parameter => NormalizeParameterName(parameter.Name!))
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<QueryParameter> positionalParameters = parameters
            .Where(static parameter => string.IsNullOrWhiteSpace(parameter.Name))
            .ToArray();

        foreach (QueryParameterPlaceholder placeholder in namedPlaceholders)
        {
            if (namedParameters.ContainsKey(NormalizeParameterName(placeholder.Token)))
                continue;

            throw new ArgumentException(
                string.Format(
                    L(
                        "queryExecutor.error.namedParameterValueMissing",
                        "Preview mode requires a value for SQL parameter '{0}'."
                    ),
                    placeholder.Token
                ),
                nameof(originalQuery)
            );
        }

        int requiredQuestionMarkCount = positionalPlaceholders.Count(static placeholder =>
            string.Equals(placeholder.Token, "?", StringComparison.Ordinal));
        int requiredDollarIndex = positionalPlaceholders
            .Where(static placeholder => placeholder.Token.StartsWith('$'))
            .Select(static placeholder => placeholder.Position ?? 0)
            .DefaultIfEmpty(0)
            .Max();
        int requiredPositionalCount = Math.Max(requiredQuestionMarkCount, requiredDollarIndex);

        if (requiredPositionalCount > 0 && positionalParameters.Count < requiredPositionalCount)
            throw new ArgumentException(
                string.Format(
                    L(
                        "queryExecutor.error.positionalParameterValueMissing",
                        "Preview mode requires {0} positional parameter value(s) for this SQL statement."
                    ),
                    requiredPositionalCount
                ),
                nameof(originalQuery)
            );
    }

    private static string NormalizeParameterName(string parameterName) =>
        QueryParameterPlaceholderParser.NormalizeName(parameterName);

    private static bool TryResolveTokenAfterLeadingWith(string statement, out string? token)
    {
        token = null;
        int index = 0;
        while (index < statement.Length && char.IsWhiteSpace(statement[index]))
            index++;

        if (!statement.AsSpan(index).StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
            return false;

        index += 4;
        bool consumedDefinition = false;
        while (index < statement.Length)
        {
            while (index < statement.Length && (char.IsWhiteSpace(statement[index]) || statement[index] == ','))
                index++;

            if (index >= statement.Length || !IsIdentifierStart(statement[index]))
                return false;

            index++;
            while (index < statement.Length && IsIdentifierPart(statement[index]))
                index++;

            while (index < statement.Length && char.IsWhiteSpace(statement[index]))
                index++;

            if (index < statement.Length && statement[index] == '(')
            {
                if (!TrySkipBalancedParentheses(statement, ref index))
                    return false;

                while (index < statement.Length && char.IsWhiteSpace(statement[index]))
                    index++;
            }

            if (!statement.AsSpan(index).StartsWith("AS", StringComparison.OrdinalIgnoreCase))
                return false;

            index += 2;
            while (index < statement.Length && char.IsWhiteSpace(statement[index]))
                index++;

            if (index >= statement.Length || statement[index] != '(')
                return false;

            if (!TrySkipBalancedParentheses(statement, ref index))
                return false;

            consumedDefinition = true;
            while (index < statement.Length && char.IsWhiteSpace(statement[index]))
                index++;

            if (index < statement.Length && statement[index] == ',')
            {
                index++;
                continue;
            }

            break;
        }

        if (!consumedDefinition || index >= statement.Length)
            return false;

        token = Regex.Match(statement[index..], @"^\s*(\w+)", RegexOptions.CultureInvariant)
            .Groups[1]
            .Value
            .ToUpperInvariant();
        return !string.IsNullOrWhiteSpace(token);
    }

    private static bool TrySkipBalancedParentheses(string value, ref int index)
    {
        if (index >= value.Length || value[index] != '(')
            return false;

        int depth = 0;
        bool inSingleQuote = false;
        while (index < value.Length)
        {
            char current = value[index];
            if (current == '\'')
            {
                if (inSingleQuote)
                {
                    if (index + 1 < value.Length && value[index + 1] == '\'')
                    {
                        index += 2;
                        continue;
                    }

                    inSingleQuote = false;
                }
                else
                {
                    inSingleQuote = true;
                }
            }

            if (!inSingleQuote)
            {
                if (current == '(')
                    depth++;
                else if (current == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        index++;
                        return true;
                    }
                }
            }

            index++;
        }

        return false;
    }

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static (string Query, IReadOnlyList<QueryParameter>? Parameters) ExpandNamedListParameters(
        string query,
        IReadOnlyList<QueryParameter>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
            return (query, parameters);

        IReadOnlyList<QueryParameterPlaceholder> namedPlaceholders = QueryParameterPlaceholderParser.Parse(query)
            .Where(static placeholder => placeholder.Kind == QueryParameterPlaceholderKind.Named)
            .ToArray();
        if (namedPlaceholders.Count == 0)
            return (query, parameters);

        Dictionary<string, ListExpansion> expansions = [];
        foreach (QueryParameter parameter in parameters)
        {
            if (string.IsNullOrWhiteSpace(parameter.Name)
                || !TryGetListParameterValues(parameter.Value, out IReadOnlyList<object?> values))
                continue;

            if (values.Count == 0)
                throw new ArgumentException(
                    string.Format(
                        L(
                            "queryExecutor.error.listParameterEmpty",
                            "Preview parameter '{0}' cannot be an empty list."
                        ),
                        parameter.Name
                    ),
                    nameof(parameters)
                );

            expansions[NormalizeParameterName(parameter.Name)] = new ListExpansion(values);
        }

        if (expansions.Count == 0)
            return (query, parameters);

        string expandedQuery = query;
        List<QueryParameter> expandedParameters = [];
        HashSet<string> expandedNames = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> generatedNames = new(StringComparer.OrdinalIgnoreCase);

        foreach (QueryParameterPlaceholder placeholder in namedPlaceholders)
        {
            string normalizedName = NormalizeParameterName(placeholder.Token);
            if (!expansions.TryGetValue(normalizedName, out ListExpansion? expansion))
                continue;

            bool firstExpansionForName = expandedNames.Add(normalizedName);

            string tokenPrefix = placeholder.Token[0].ToString();
            string tokenName = NormalizeParameterName(placeholder.Token);
            string[] generatedTokens = expansion.Values
                .Select((_, index) => $"{tokenPrefix}{tokenName}_{index}")
                .ToArray();

            expandedQuery = ReplaceParameterToken(expandedQuery, placeholder.Token, string.Join(", ", generatedTokens));

            if (!firstExpansionForName)
                continue;

            for (int index = 0; index < expansion.Values.Count; index++)
            {
                string generatedToken = generatedTokens[index];
                if (generatedNames.Add(NormalizeParameterName(generatedToken)))
                    expandedParameters.Add(new QueryParameter(generatedToken, expansion.Values[index]));
            }
        }

        foreach (QueryParameter parameter in parameters)
        {
            if (string.IsNullOrWhiteSpace(parameter.Name))
            {
                expandedParameters.Add(parameter);
                continue;
            }

            string normalizedName = NormalizeParameterName(parameter.Name);
            if (!expandedNames.Contains(normalizedName))
                expandedParameters.Add(parameter);
        }

        return (expandedQuery, expandedParameters);
    }

    private static string ReplaceParameterToken(string query, string token, string replacement) =>
        Regex.Replace(
            query,
            $@"(?<![A-Za-z0-9_]){Regex.Escape(token)}(?![A-Za-z0-9_])",
            replacement,
            RegexOptions.CultureInvariant);

    private static bool TryGetListParameterValues(
        object? value,
        out IReadOnlyList<object?> values)
    {
        if (value is null or string or byte[])
        {
            values = [];
            return false;
        }

        if (value is not IEnumerable enumerable)
        {
            values = [];
            return false;
        }

        List<object?> items = [];
        foreach (object? item in enumerable)
            items.Add(item);

        values = items;
        return true;
    }

    private static bool IsCommentOnlyOrWhitespace(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return true;

        bool inLineComment = false;
        bool inBlockComment = false;

        for (int i = 0; i < sql.Length; i++)
        {
            char c = sql[i];
            char next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (inLineComment)
            {
                if (c == '\n')
                    inLineComment = false;
                continue;
            }

            if (inBlockComment)
            {
                if (c == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }
                continue;
            }

            if (char.IsWhiteSpace(c))
                continue;

            if (c == '-' && next == '-')
            {
                inLineComment = true;
                i++;
                continue;
            }

            if (c == '/' && next == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }

            return false;
        }

        return true;
    }

    private static string TrimTrailingSemicolon(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return string.Empty;

        return sql.Trim().TrimEnd(';').TrimEnd();
    }

    /// <summary>
    /// Creates the appropriate orchestrator for the given provider.
    /// </summary>
    private IDbOrchestrator CreateOrchestrator(DatabaseProvider provider, ConnectionConfig config)
    {
        _logger.LogDebug("Creating orchestrator for provider: {Provider}", provider);
        return provider switch
        {
            DatabaseProvider.SqlServer => new SqlServerOrchestrator(config),
            DatabaseProvider.MySql => new MySqlOrchestrator(config),
            DatabaseProvider.Postgres => new PostgresOrchestrator(config),
            DatabaseProvider.SQLite => new SqliteOrchestrator(config),
            _ => throw new NotSupportedException($"Provider {provider} is not supported")
        };
    }

    /// <summary>
    /// Returns demo data for testing when real connection is not available.
    /// </summary>
    private DataTable BuildDemoDataTable()
    {
        var dt = new DataTable("demo_results");
        dt.Columns.Add("id", typeof(int));
        dt.Columns.Add("name", typeof(string));
        dt.Columns.Add("value", typeof(decimal));
        dt.Columns.Add("created_at", typeof(DateTime));

        var rng = new Random(42);
        for (int i = 1; i <= 10; i++)
        {
            dt.Rows.Add(
                i,
                $"Record {i}",
                Math.Round(rng.NextDouble() * 1000, 2),
                DateTime.Now.AddDays(-rng.Next(0, 30))
            );
        }

        _logger.LogDebug("Demo DataTable created with {RowCount} rows", dt.Rows.Count);
        return dt;
    }

    /// <summary>
    /// Executes a query using direct connection from the orchestrator.
    /// </summary>
    private async Task<DataTable> ExecuteQueryInternalAsync(
        IDbOrchestrator orchestrator,
        string query,
        IReadOnlyList<QueryParameter>? parameters,
        CancellationToken ct)
    {
        var dt = new DataTable();

        try
        {
            // Use reflection to access OpenConnectionAsync since it's protected
            var method = orchestrator.GetType().GetMethod(
                "OpenConnectionAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (method is null)
                throw new InvalidOperationException(
                    L("queryExecutor.error.openConnectionMethodNotFound", "Cannot find OpenConnectionAsync method on orchestrator")
                );

            var task = method.Invoke(orchestrator, new object[] { ct }) as Task<DbConnection>;
            if (task is null)
                throw new InvalidOperationException(
                    L("queryExecutor.error.openConnectionInvokeFailed", "Failed to invoke OpenConnectionAsync")
                );

            _logger.LogDebug("Opening database connection");
            await using (DbConnection conn = await task)
            {
                _logger.LogDebug("Connection opened, executing query");

                using (var command = conn.CreateCommand())
                {
                    command.CommandText = query;
                    command.CommandTimeout = Math.Max(1, CommandTimeoutSeconds);
                    BindParameters(command, query, parameters);

                    _logger.LogDebug("Executing command: {Query}", query);
                    using (var reader = await command.ExecuteReaderAsync(ct))
                    {
                        _logger.LogDebug("Reader obtained, loading data into DataTable");
                        dt.Load(reader);
                        _logger.LogInformation("DataTable loaded with {RowCount} rows and {ColumnCount} columns",
                            dt.Rows.Count, dt.Columns.Count);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during query execution: {Message}", ex.Message);
            throw;
        }

        return dt;
    }

    private static void BindParameters(
        DbCommand command,
        string query,
        IReadOnlyList<QueryParameter>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
            return;

        Dictionary<string, QueryParameter> namedParameters = parameters
            .Where(static parameter => !string.IsNullOrWhiteSpace(parameter.Name))
            .GroupBy(parameter => NormalizeParameterName(parameter.Name!))
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<QueryParameter> positionalParameters = parameters
            .Where(static parameter => string.IsNullOrWhiteSpace(parameter.Name))
            .ToArray();

        foreach (QueryParameterPlaceholder placeholder in QueryParameterPlaceholderParser.Parse(query)
                     .Where(static item => item.Kind == QueryParameterPlaceholderKind.Named))
        {
            if (!namedParameters.TryGetValue(NormalizeParameterName(placeholder.Token), out QueryParameter? parameter))
                continue;

            AddParameter(command, placeholder.Token, parameter.Value);
        }

        int sequentialPositionalIndex = 0;
        foreach (QueryParameterPlaceholder placeholder in QueryParameterPlaceholderParser.Parse(query)
                     .Where(static item => item.Kind == QueryParameterPlaceholderKind.Positional))
        {
            QueryParameter? parameter = null;
            string parameterName = placeholder.Token;

            if (string.Equals(placeholder.Token, "?", StringComparison.Ordinal))
            {
                if (sequentialPositionalIndex >= positionalParameters.Count)
                    continue;

                parameter = positionalParameters[sequentialPositionalIndex++];
                parameterName = $"p{sequentialPositionalIndex}";
            }
            else if (placeholder.Token.StartsWith('$')
                && placeholder.Position is int numericIndex
                && numericIndex > 0
                && numericIndex <= positionalParameters.Count)
            {
                parameter = positionalParameters[numericIndex - 1];
            }

            if (parameter is null)
                continue;

            AddParameter(command, parameterName, parameter.Value);
        }
    }

    private static void AddParameter(DbCommand command, string parameterName, object? value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = parameterName;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private sealed record ListExpansion(IReadOnlyList<object?> Values);

    private static string L(string key, string fallback)
    {
        string value = LocalizationService.Instance[key];
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }
}

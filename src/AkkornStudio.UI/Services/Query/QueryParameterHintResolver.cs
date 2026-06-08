using System.Globalization;
using System.Text.RegularExpressions;
using AkkornStudio.Core;
using AkkornStudio.Metadata;
using AkkornStudio.UI.Services.SqlEditor;

namespace AkkornStudio.UI.Services;

internal static partial class QueryParameterHintResolver
{
    private static readonly SqlSymbolTableBuilder SymbolTableBuilder = new();

    public static QueryParameterHint Resolve(
        string sql,
        QueryParameterPlaceholder placeholder,
        QueryParameter? suggestedParameter = null,
        QueryExecutionParameterContext? structuralContext = null,
        DbMetadata? metadata = null,
        DatabaseProvider provider = DatabaseProvider.Postgres)
    {
        if (TryResolveFromSuggestedValue(placeholder, sql, suggestedParameter, structuralContext, out QueryParameterHint suggestedHint))
            return suggestedHint;

        if (TryResolveFromStructuralMetadata(structuralContext, metadata, out QueryParameterHint structuralMetadataHint))
            return structuralMetadataHint;

        if (TryResolveFromMetadata(sql, placeholder, metadata, provider, out QueryParameterHint metadataHint))
            return metadataHint;

        string token = placeholder.Token;
        string normalizedName = QueryParameterPlaceholderParser.NormalizeName(token);
        string sqlUpper = sql.ToUpperInvariant();
        int tokenIndex = sql.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        string context = tokenIndex >= 0
            ? sqlUpper[Math.Max(0, tokenIndex - 32)..Math.Min(sqlUpper.Length, tokenIndex + token.Length + 32)]
            : sqlUpper;
        string nameUpper = normalizedName.ToUpperInvariant();

        if (nameUpper.Contains("DATE", StringComparison.Ordinal)
            || nameUpper.Contains("TIME", StringComparison.Ordinal)
            || context.Contains("DATE", StringComparison.Ordinal)
            || context.Contains("TIMESTAMP", StringComparison.Ordinal))
        {
            return new QueryParameterHint("date/time", "2026-01-31", "Accepts ISO date/time text.", BuildContextLabel(sql, placeholder, null, structuralContext));
        }

        if (nameUpper.StartsWith("IS_", StringComparison.Ordinal)
            || nameUpper.StartsWith("HAS_", StringComparison.Ordinal)
            || nameUpper.Contains("ENABLED", StringComparison.Ordinal)
            || nameUpper.Contains("ACTIVE", StringComparison.Ordinal)
            || context.Contains(" = TRUE", StringComparison.Ordinal)
            || context.Contains(" = FALSE", StringComparison.Ordinal))
        {
            return new QueryParameterHint("boolean", "true", "Use true or false.", BuildContextLabel(sql, placeholder, null, structuralContext));
        }

        if (nameUpper.EndsWith("_ID", StringComparison.Ordinal)
            || nameUpper.Equals("ID", StringComparison.Ordinal)
            || nameUpper.Contains("COUNT", StringComparison.Ordinal)
            || nameUpper.Contains("LIMIT", StringComparison.Ordinal)
            || nameUpper.Contains("OFFSET", StringComparison.Ordinal)
            || context.Contains(" LIMIT ", StringComparison.Ordinal)
            || context.Contains(" OFFSET ", StringComparison.Ordinal))
        {
            return new QueryParameterHint("integer", "42", "Whole numeric value.", BuildContextLabel(sql, placeholder, null, structuralContext));
        }

        if (nameUpper.Contains("PRICE", StringComparison.Ordinal)
            || nameUpper.Contains("AMOUNT", StringComparison.Ordinal)
            || nameUpper.Contains("TOTAL", StringComparison.Ordinal)
            || context.Contains("DECIMAL", StringComparison.Ordinal)
            || context.Contains("NUMERIC", StringComparison.Ordinal))
        {
            return new QueryParameterHint("decimal", "19.99", "Decimal numeric value.", BuildContextLabel(sql, placeholder, null, structuralContext));
        }

        if (context.Contains(" LIKE ", StringComparison.Ordinal)
            || nameUpper.Contains("NAME", StringComparison.Ordinal)
            || nameUpper.Contains("EMAIL", StringComparison.Ordinal)
            || nameUpper.Contains("STATUS", StringComparison.Ordinal)
            || nameUpper.Contains("CODE", StringComparison.Ordinal))
        {
            return new QueryParameterHint("text", "sample", "Plain text value.", BuildContextLabel(sql, placeholder, null, structuralContext));
        }

        return new QueryParameterHint("text", "value", "Value inferred as generic text.", BuildContextLabel(sql, placeholder, null, structuralContext));
    }

    private static bool TryResolveFromStructuralMetadata(
        QueryExecutionParameterContext? structuralContext,
        DbMetadata? metadata,
        out QueryParameterHint hint)
    {
        hint = null!;
        if (metadata is null
            || structuralContext is null
            || string.IsNullOrWhiteSpace(structuralContext.TableRef)
            || string.IsNullOrWhiteSpace(structuralContext.ColumnName))
        {
            return false;
        }

        TableMetadata? table = ResolveTable(metadata, structuralContext.TableRef!);
        ColumnMetadata? column = table?.Columns.FirstOrDefault(c =>
            c.Name.Equals(structuralContext.ColumnName, StringComparison.OrdinalIgnoreCase));
        if (column is null)
            return false;

        hint = BuildColumnHint(table!, column, structuralContext);
        return true;
    }

    private static bool TryResolveFromMetadata(
        string sql,
        QueryParameterPlaceholder placeholder,
        DbMetadata? metadata,
        DatabaseProvider provider,
        out QueryParameterHint hint)
    {
        hint = null!;
        if (metadata is null)
            return false;

        string? sourceReference = TryResolveSourceReference(sql, placeholder);
        if (string.IsNullOrWhiteSpace(sourceReference))
            return false;

        int dot = sourceReference.LastIndexOf('.');
        if (dot <= 0 || dot >= sourceReference.Length - 1)
            return false;

        string qualifier = sourceReference[..dot];
        string columnName = sourceReference[(dot + 1)..];
        SqlSymbolTable symbols = SymbolTableBuilder.Build(sql, provider);
        string tableRef = symbols.TryResolveBinding(qualifier, out SqlTableBindingSymbol? binding) && binding is not null
            ? binding.TableRef
            : qualifier;
        TableMetadata? table = ResolveTable(metadata, tableRef);
        ColumnMetadata? column = table?.Columns.FirstOrDefault(c =>
            c.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase));
        if (column is null)
            return false;

        hint = BuildColumnHint(table!, column, structuralContext: null);
        return true;
    }

    private static bool TryResolveFromSuggestedValue(
        QueryParameterPlaceholder placeholder,
        string sql,
        QueryParameter? suggestedParameter,
        QueryExecutionParameterContext? structuralContext,
        out QueryParameterHint hint)
    {
        if (suggestedParameter is null)
        {
            hint = null!;
            return false;
        }

        string bindingLabel = !string.IsNullOrWhiteSpace(suggestedParameter.Name)
            ? suggestedParameter.Name!
            : placeholder.Token;
        string contextLabel = BuildContextLabel(sql, placeholder, bindingLabel, structuralContext);
        string description = ResolveSuggestedValueDescription(structuralContext);

        if (suggestedParameter.Value is null)
        {
            hint = new QueryParameterHint("null", "null", "Valor atual nulo no pipeline visual.", contextLabel);
            return true;
        }

        switch (suggestedParameter.Value)
        {
            case bool boolValue:
                hint = new QueryParameterHint(
                    "boolean",
                    boolValue ? "true" : "false",
                    description,
                    contextLabel);
                return true;
            case sbyte or byte or short or ushort or int or uint or long or ulong:
                hint = new QueryParameterHint(
                    "integer",
                    Convert.ToString(suggestedParameter.Value, CultureInfo.InvariantCulture) ?? "42",
                    description,
                    contextLabel);
                return true;
            case float or double or decimal:
                hint = new QueryParameterHint(
                    "decimal",
                    Convert.ToString(suggestedParameter.Value, CultureInfo.InvariantCulture) ?? "19.99",
                    description,
                    contextLabel);
                return true;
            case DateTime dateTime:
                hint = new QueryParameterHint(
                    "date/time",
                    dateTime.ToString("O", CultureInfo.InvariantCulture),
                    description,
                    contextLabel);
                return true;
            case DateTimeOffset dateTimeOffset:
                hint = new QueryParameterHint(
                    "date/time",
                    dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
                    description,
                    contextLabel);
                return true;
            case Guid guid:
                hint = new QueryParameterHint(
                    "uuid",
                    guid.ToString(),
                    description,
                    contextLabel);
                return true;
            case string stringValue:
                hint = new QueryParameterHint(
                    "text",
                    string.IsNullOrWhiteSpace(stringValue) ? "value" : stringValue,
                    structuralContext is null ? "Valor inicial vindo do pipeline visual." : description,
                    contextLabel);
                return true;
            default:
                hint = new QueryParameterHint(
                    suggestedParameter.Value.GetType().Name.ToLowerInvariant(),
                    Convert.ToString(suggestedParameter.Value, CultureInfo.InvariantCulture) ?? "value",
                    description,
                    contextLabel);
                return true;
        }
    }

    private static string? TryResolveSourceReference(string sql, QueryParameterPlaceholder placeholder)
    {
        int tokenIndex = sql.IndexOf(placeholder.Token, StringComparison.OrdinalIgnoreCase);
        if (tokenIndex < 0)
            return null;

        int windowStart = Math.Max(0, tokenIndex - 120);
        int windowLength = tokenIndex - windowStart;
        if (windowLength <= 0)
            return null;

        string leftWindow = sql.Substring(windowStart, windowLength);
        Match match = SourceReferenceRegex().Match(leftWindow);
        if (!match.Success)
            return null;

        string alias = NormalizeIdentifier(match.Groups["alias"].Value);
        string column = NormalizeIdentifier(match.Groups["column"].Value);
        if (string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(column))
            return null;

        return $"{alias}.{column}";
    }

    private static string BuildContextLabel(
        string sql,
        QueryParameterPlaceholder placeholder,
        string? bindingLabel,
        QueryExecutionParameterContext? structuralContext)
    {
        List<string> parts = [];
        if (!string.IsNullOrWhiteSpace(bindingLabel))
            parts.Add($"Binding do pipeline visual: {bindingLabel}");

        string? structuralSummary = ResolveStructuralContextSummary(structuralContext);
        if (!string.IsNullOrWhiteSpace(structuralSummary))
            parts.Add(structuralSummary);

        if (!string.IsNullOrWhiteSpace(structuralContext?.ContextLabel))
            parts.Add(structuralContext.ContextLabel!);
        else if (!string.IsNullOrWhiteSpace(structuralContext?.SourceReference))
            parts.Add($"Origem estrutural: {structuralContext.SourceReference}");

        string? sourceReference = TryResolveSourceReference(sql, placeholder);
        if (!string.IsNullOrWhiteSpace(sourceReference)
            && !parts.Any(part => part.Contains(sourceReference, StringComparison.OrdinalIgnoreCase)))
            parts.Add($"Origem SQL: {sourceReference}");

        return string.Join(" | ", parts);
    }

    private static QueryParameterHint BuildColumnHint(
        TableMetadata table,
        ColumnMetadata column,
        QueryExecutionParameterContext? structuralContext)
    {
        string nullable = column.IsNullable ? "nullable" : "required";
        string keyFlag = column.IsPrimaryKey ? " PK" : column.IsForeignKey ? " FK" : string.Empty;
        string commentSuffix = string.IsNullOrWhiteSpace(column.Comment)
            ? string.Empty
            : $" | Comentário: {column.Comment!.Trim()}";
        string contextLabel = $"Origem: {table.FullName}.{column.Name} | Tipo: {column.DataType}{keyFlag}{commentSuffix}";
        string description = BuildColumnDescription(column, nullable, structuralContext);
        string exampleValue = ResolveColumnExampleValue(column);

        return column.SemanticType switch
        {
            ColumnSemanticType.Numeric when IsIntegerLike(column) =>
                new QueryParameterHint("integer", exampleValue, description, contextLabel),
            ColumnSemanticType.Numeric =>
                new QueryParameterHint("decimal", exampleValue, description, contextLabel),
            ColumnSemanticType.Boolean =>
                new QueryParameterHint("boolean", exampleValue, description, contextLabel),
            ColumnSemanticType.DateTime =>
                new QueryParameterHint("date/time", exampleValue, description, contextLabel),
            ColumnSemanticType.Guid =>
                new QueryParameterHint("uuid", exampleValue, description, contextLabel),
            _ =>
                new QueryParameterHint("text", exampleValue, description, contextLabel),
        };
    }

    private static string BuildColumnDescription(
        ColumnMetadata column,
        string nullable,
        QueryExecutionParameterContext? structuralContext)
    {
        List<string> traits =
        [
            $"Column type inferred from metadata ({nullable})."
        ];

        string? expressionTrait = ResolveExpressionTrait(structuralContext);
        if (!string.IsNullOrWhiteSpace(expressionTrait))
            traits.Add(expressionTrait);

        if (column.IsPrimaryKey)
            traits.Add("Primary key column.");
        else if (column.IsForeignKey)
            traits.Add("Foreign key column.");
        else if (column.IsUnique)
            traits.Add("Unique column.");

        if (!string.IsNullOrWhiteSpace(column.DefaultValue))
            traits.Add("Uses database default when omitted.");

        return string.Join(" ", traits);
    }

    private static string ResolveSuggestedValueDescription(QueryExecutionParameterContext? structuralContext)
    {
        List<string> parts = ["Tipo inferido a partir do binding real do pipeline visual."];

        string? expressionTrait = ResolveExpressionTrait(structuralContext);
        if (!string.IsNullOrWhiteSpace(expressionTrait))
            parts.Add(expressionTrait);

        return string.Join(" ", parts);
    }

    private static string? ResolveStructuralContextSummary(QueryExecutionParameterContext? structuralContext)
    {
        if (structuralContext is null)
            return null;

        return structuralContext.ExpressionKind switch
        {
            "aggregate" when structuralContext.SourceCount > 1 => "Filtro sobre agregado com multiplas entradas.",
            "aggregate" => "Filtro sobre agregado.",
            "aggregate-string" when structuralContext.SourceCount > 1 => "Filtro sobre agregacao textual ordenada.",
            "aggregate-string" => "Filtro sobre agregacao textual.",
            "window" when structuralContext.SourceCount > 1 => "Filtro sobre janela particionada.",
            "window" => "Filtro sobre funcao de janela.",
            "concat" => "Filtro sobre valor concatenado.",
            "arithmetic" => "Filtro sobre valor calculado.",
            "date-transform" => "Filtro sobre expressao de data/hora.",
            "string-transform" => "Filtro sobre transformacao textual.",
            "conditional" => "Filtro sobre expressao com fallback.",
            "json" => "Filtro sobre expressao JSON.",
            _ when structuralContext.SourceCount > 1 => "Filtro sobre expressao composta.",
            _ => null,
        };
    }

    private static string? ResolveExpressionTrait(QueryExecutionParameterContext? structuralContext)
    {
        if (structuralContext is null)
            return null;

        return structuralContext.ExpressionKind switch
        {
            "aggregate" => "Parametro aplicado sobre expressao agregada.",
            "aggregate-string" when structuralContext.SourceCount > 1 => "Parametro aplicado sobre agregacao textual ordenada.",
            "aggregate-string" => "Parametro aplicado sobre agregacao textual.",
            "window" when structuralContext.SourceCount > 1 => "Parametro aplicado sobre funcao de janela particionada.",
            "window" => "Parametro aplicado sobre funcao de janela.",
            "concat" => "Parametro aplicado sobre concatenacao de multiplas colunas.",
            "arithmetic" => "Parametro aplicado sobre expressao aritmetica derivada.",
            "date-transform" => "Parametro aplicado sobre expressao derivada de data/hora.",
            "string-transform" => "Parametro aplicado sobre transformacao textual.",
            "conditional" => "Parametro aplicado sobre expressao condicional com fallback.",
            "json" => "Parametro aplicado sobre expressao JSON derivada.",
            _ when structuralContext.SourceCount > 1 => "Parametro aplicado sobre expressao com multiplas origens estruturais.",
            _ => null,
        };
    }

    private static string ResolveColumnExampleValue(ColumnMetadata column)
    {
        string? normalizedDefault = NormalizeColumnDefaultValue(column.DefaultValue);
        if (!string.IsNullOrWhiteSpace(normalizedDefault))
            return normalizedDefault;

        return column.SemanticType switch
        {
            ColumnSemanticType.Numeric when IsIntegerLike(column) => "42",
            ColumnSemanticType.Numeric => "19.99",
            ColumnSemanticType.Boolean => "true",
            ColumnSemanticType.DateTime => "2026-01-31T10:00:00Z",
            ColumnSemanticType.Guid => "550e8400-e29b-41d4-a716-446655440000",
            _ => "value",
        };
    }

    private static string? NormalizeColumnDefaultValue(string? defaultValue)
    {
        if (string.IsNullOrWhiteSpace(defaultValue))
            return null;

        string trimmed = defaultValue.Trim();
        if (trimmed.Length == 0)
            return null;

        if (trimmed.StartsWith("nextval(", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("uuid", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("gen_random_uuid", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("newid(", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("current_", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("getdate(", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("now(", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if ((trimmed.StartsWith("('", StringComparison.Ordinal) && trimmed.EndsWith("')", StringComparison.Ordinal))
            || (trimmed.StartsWith("('", StringComparison.Ordinal) && trimmed.EndsWith("'::", StringComparison.OrdinalIgnoreCase)))
        {
            trimmed = trimmed.Trim('(', ')');
        }

        if (trimmed.Length >= 2 && trimmed[0] == '\'' && trimmed[^1] == '\'')
            return trimmed[1..^1].Replace("''", "'", StringComparison.Ordinal);

        if (string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.ToLowerInvariant();
        }

        return trimmed;
    }

    private static bool IsIntegerLike(ColumnMetadata column)
    {
        string type = $"{column.DataType} {column.NativeType}".ToUpperInvariant();
        return type.Contains("INT", StringComparison.Ordinal)
               || type.Contains("SERIAL", StringComparison.Ordinal)
               || type.Contains("BIGINT", StringComparison.Ordinal)
               || type.Contains("SMALLINT", StringComparison.Ordinal);
    }

    private static TableMetadata? ResolveTable(DbMetadata metadata, string tableRef)
    {
        string normalized = tableRef.Trim();
        return metadata.AllTables.FirstOrDefault(t =>
                   t.FullName.Equals(normalized, StringComparison.OrdinalIgnoreCase))
               ?? metadata.AllTables.FirstOrDefault(t =>
                   t.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeIdentifier(string raw)
    {
        string trimmed = raw.Trim();
        if (trimmed.Length >= 2)
        {
            if ((trimmed[0] == '"' && trimmed[^1] == '"')
                || (trimmed[0] == '[' && trimmed[^1] == ']')
                || (trimmed[0] == '`' && trimmed[^1] == '`'))
            {
                return trimmed[1..^1];
            }
        }

        return trimmed;
    }

    [GeneratedRegex(@"(?<alias>(?:\[[^\]]+\]|`[^`]+`|""[^""]+""|[A-Za-z_][A-Za-z0-9_]*))\s*\.\s*(?<column>(?:\[[^\]]+\]|`[^`]+`|""[^""]+""|[A-Za-z_][A-Za-z0-9_]*))\s*(?:=|<>|!=|>=|<=|>|<|LIKE|NOT\s+LIKE|IN|NOT\s+IN)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SourceReferenceRegex();
}

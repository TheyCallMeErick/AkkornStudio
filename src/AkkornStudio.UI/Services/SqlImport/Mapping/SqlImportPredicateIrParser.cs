using System.Globalization;
using System.Text.RegularExpressions;
using AkkornStudio.SqlImport.Contracts;
using AkkornStudio.SqlImport.Diagnostics;
using AkkornStudio.SqlImport.Ids;
using AkkornStudio.SqlImport.IR;
using AkkornStudio.SqlImport.IR.Expressions;
using AkkornStudio.SqlImport.IR.Metadata;
using AkkornStudio.SqlImport.Tracing;
using SqlImportBetweenExpr = AkkornStudio.SqlImport.IR.Expressions.BetweenExpr;
using SqlImportCaseExpr = AkkornStudio.SqlImport.IR.Expressions.CaseExpr;
using SqlImportCastExpr = AkkornStudio.SqlImport.IR.Expressions.CastExpr;
using SqlImportComparisonOperator = AkkornStudio.SqlImport.IR.Expressions.ComparisonOperator;
using SqlImportComparisonExpr = AkkornStudio.SqlImport.IR.Expressions.ComparisonExpr;
using SqlImportIsNullExpr = AkkornStudio.SqlImport.IR.Expressions.IsNullExpr;
using SqlImportLiteralExpr = AkkornStudio.SqlImport.IR.Expressions.LiteralExpr;
using SqlImportNotExpr = AkkornStudio.SqlImport.IR.Expressions.NotExpr;

namespace AkkornStudio.UI.Services.SqlImport.Mapping;

public sealed class SqlImportPredicateIrParser
{
    private static readonly HashSet<string> CanonicalFunctions =
    ["COALESCE", "NULLIF", "UPPER", "LOWER", "LENGTH", "COUNT", "SUM", "AVG", "MIN", "MAX"];

    private static readonly HashSet<string> DialectSpecificFunctions =
    ["ISNULL", "LEN", "FORMAT"];

    private static readonly HashSet<string> UnsupportedFunctions =
    ["ROW_NUMBER", "RANK", "DENSE_RANK", "NTILE", "LAG", "LEAD", "FIRST_VALUE", "LAST_VALUE"];

    public SqlExpression ParseScalarExpression(
        string expression,
        string queryId,
        string exprPath,
        SqlImportClause clause,
        IReadOnlyDictionary<string, string> sourceByAlias,
        string fallbackSourceId,
        ICollection<SqlImportDiagnostic> diagnostics
    )
    {
        return ParseScalar(
            expression,
            queryId,
            exprPath,
            sourceByAlias,
            fallbackSourceId,
            diagnostics,
            clause,
            parentExprId: null
        );
    }

    public SqlExpression Parse(
        string expression,
        string queryId,
        string exprPath,
        SqlImportClause clause,
        IReadOnlyDictionary<string, string> sourceByAlias,
        string fallbackSourceId,
        ICollection<SqlImportDiagnostic> diagnostics
    )
    {
        string trimmed = expression.Trim();
        return ParseInternal(
            trimmed,
            queryId,
            exprPath,
            clause,
            sourceByAlias,
            fallbackSourceId,
            diagnostics,
            parentExprId: null
        );
    }

    public int CountExpressions(SqlExpression expression)
    {
        return expression switch
        {
            LogicalExpr logical => 1 + logical.Operands.Sum(CountExpressions),
            SqlImportNotExpr not => 1 + CountExpressions(not.Operand),
            SqlImportComparisonExpr comparison => 1 + CountExpressions(comparison.Left) + CountExpressions(comparison.Right),
            InExpr inExpr => 1 + CountExpressions(inExpr.Value) + inExpr.Values.Sum(CountExpressions),
            SqlImportBetweenExpr between => 1 + CountExpressions(between.Value) + CountExpressions(between.Low) + CountExpressions(between.High),
            LikeExpr like => 1 + CountExpressions(like.Value) + CountExpressions(like.Pattern),
            SqlImportIsNullExpr isNull => 1 + CountExpressions(isNull.Value),
            FunctionExpr function => 1 + function.Arguments.Sum(CountExpressions),
            SqlImportCastExpr cast => 1 + CountExpressions(cast.Value),
            SqlImportCaseExpr caseExpr => 1
                + caseExpr.Branches.Sum(branch => CountExpressions(branch.WhenExpression) + CountExpressions(branch.ThenExpression))
                + (caseExpr.ElseExpression is null ? 0 : CountExpressions(caseExpr.ElseExpression)),
            _ => 1,
        };
    }

    private SqlExpression ParseInternal(
        string expression,
        string queryId,
        string exprPath,
        SqlImportClause clause,
        IReadOnlyDictionary<string, string> sourceByAlias,
        string fallbackSourceId,
        ICollection<SqlImportDiagnostic> diagnostics,
        string? parentExprId
    )
    {
        expression = StripEnclosingParentheses(expression);
        string exprHash = ComputeHash(expression);
        string exprId = StableSqlImportIdGenerator.BuildExprId(
            queryId,
            exprPath,
            "Predicate",
            exprHash,
            parentExprId
        );

        IReadOnlyList<string> orParts = SplitTopLevelKeyword(expression, "OR");
        if (orParts.Count > 1)
        {
            var operands = new List<SqlExpression>(orParts.Count);
            for (int index = 0; index < orParts.Count; index++)
            {
                operands.Add(ParseInternal(
                    orParts[index],
                    queryId,
                    $"{exprPath}/or/{index}",
                    clause,
                    sourceByAlias,
                    fallbackSourceId,
                    diagnostics,
                    exprId
                ));
            }

            return new LogicalExpr(
                exprId,
                null,
                SqlImportSemanticType.Boolean,
                CombineResolutionStatus(operands),
                CreateTrace(queryId, exprId),
                CreateNodeMetadata(),
                LogicalOperator.Or,
                operands
            );
        }

        IReadOnlyList<string> andParts = SplitTopLevelKeyword(expression, "AND");
        if (andParts.Count > 1)
        {
            var operands = new List<SqlExpression>(andParts.Count);
            for (int index = 0; index < andParts.Count; index++)
            {
                operands.Add(ParseInternal(
                    andParts[index],
                    queryId,
                    $"{exprPath}/and/{index}",
                    clause,
                    sourceByAlias,
                    fallbackSourceId,
                    diagnostics,
                    exprId
                ));
            }

            return new LogicalExpr(
                exprId,
                null,
                SqlImportSemanticType.Boolean,
                CombineResolutionStatus(operands),
                CreateTrace(queryId, exprId),
                CreateNodeMetadata(),
                LogicalOperator.And,
                operands
            );
        }

        if (Regex.IsMatch(expression, @"^NOT\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            string innerExpression = Regex.Replace(
                expression,
                @"^NOT\s+",
                string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
            );

            SqlExpression inner = ParseInternal(
                innerExpression,
                queryId,
                $"{exprPath}/not",
                clause,
                sourceByAlias,
                fallbackSourceId,
                diagnostics,
                exprId
            );

            return new SqlImportNotExpr(
                exprId,
                null,
                SqlImportSemanticType.Boolean,
                inner.ResolutionStatus,
                CreateTrace(queryId, exprId),
                CreateNodeMetadata(),
                inner
            );
        }

        Match isNullMatch = Regex.Match(
            expression,
            @"^(?<left>.+?)\s+IS\s+(?<neg>NOT\s+)?NULL$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );

        if (isNullMatch.Success)
        {
            SqlExpression value = ParseScalar(
                isNullMatch.Groups["left"].Value,
                queryId,
                $"{exprPath}/is-null/value",
                sourceByAlias,
                fallbackSourceId,
                diagnostics,
                clause,
                exprId
            );

            return new SqlImportIsNullExpr(
                exprId,
                null,
                SqlImportSemanticType.Boolean,
                value.ResolutionStatus,
                CreateTrace(queryId, exprId),
                CreateNodeMetadata(),
                value,
                isNullMatch.Groups["neg"].Success
            );
        }

        Match likeMatch = Regex.Match(
            expression,
            @"^(?<left>.+?)\s+(?<neg>NOT\s+)?LIKE\s+(?<right>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );

        if (likeMatch.Success)
        {
            SqlExpression left = ParseScalar(
                likeMatch.Groups["left"].Value,
                queryId,
                $"{exprPath}/like/left",
                sourceByAlias,
                fallbackSourceId,
                diagnostics,
                clause,
                exprId
            );

            SqlExpression right = ParseScalar(
                likeMatch.Groups["right"].Value,
                queryId,
                $"{exprPath}/like/right",
                sourceByAlias,
                fallbackSourceId,
                diagnostics,
                clause,
                exprId
            );

            return new LikeExpr(
                exprId,
                null,
                SqlImportSemanticType.Boolean,
                CombineResolutionStatus(left, right),
                CreateTrace(queryId, exprId),
                CreateNodeMetadata(),
                left,
                right,
                likeMatch.Groups["neg"].Success
            );
        }

        Match betweenMatch = Regex.Match(
            expression,
            @"^(?<value>.+?)\s+(?<neg>NOT\s+)?BETWEEN\s+(?<low>.+?)\s+AND\s+(?<high>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );

        if (betweenMatch.Success)
        {
            SqlExpression value = ParseScalar(
                betweenMatch.Groups["value"].Value,
                queryId,
                $"{exprPath}/between/value",
                sourceByAlias,
                fallbackSourceId,
                diagnostics,
                clause,
                exprId
            );

            SqlExpression low = ParseScalar(
                betweenMatch.Groups["low"].Value,
                queryId,
                $"{exprPath}/between/low",
                sourceByAlias,
                fallbackSourceId,
                diagnostics,
                clause,
                exprId
            );

            SqlExpression high = ParseScalar(
                betweenMatch.Groups["high"].Value,
                queryId,
                $"{exprPath}/between/high",
                sourceByAlias,
                fallbackSourceId,
                diagnostics,
                clause,
                exprId
            );

            return new SqlImportBetweenExpr(
                exprId,
                null,
                SqlImportSemanticType.Boolean,
                CombineResolutionStatus(value, low, high),
                CreateTrace(queryId, exprId),
                CreateNodeMetadata(),
                value,
                low,
                high,
                betweenMatch.Groups["neg"].Success
            );
        }

        Match inMatch = Regex.Match(
            expression,
            @"^(?<value>.+?)\s+(?<neg>NOT\s+)?IN\s*\((?<items>.+)\)$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant
        );

        if (inMatch.Success)
        {
            SqlExpression value = ParseScalar(
                inMatch.Groups["value"].Value,
                queryId,
                $"{exprPath}/in/value",
                sourceByAlias,
                fallbackSourceId,
                diagnostics,
                clause,
                exprId
            );

            IReadOnlyList<string> items = SplitTopLevelCommaList(inMatch.Groups["items"].Value);
            var values = new List<SqlExpression>(items.Count);

            for (int index = 0; index < items.Count; index++)
            {
                values.Add(ParseScalar(
                    items[index],
                    queryId,
                    $"{exprPath}/in/item/{index}",
                    sourceByAlias,
                    fallbackSourceId,
                    diagnostics,
                    clause,
                    exprId
                ));
            }

            return new InExpr(
                exprId,
                null,
                SqlImportSemanticType.Boolean,
                CombineResolutionStatus([value, ..values]),
                CreateTrace(queryId, exprId),
                CreateNodeMetadata(),
                value,
                values,
                Subquery: null,
                IsNegated: inMatch.Groups["neg"].Success
            );
        }

        Match comparisonMatch = Regex.Match(
            expression,
            @"^(?<left>.+?)\s*(?<op><>|!=|>=|<=|=|>|<)\s*(?<right>.+)$",
            RegexOptions.CultureInvariant
        );

        if (comparisonMatch.Success)
        {
            SqlExpression left = ParseScalar(
                comparisonMatch.Groups["left"].Value,
                queryId,
                $"{exprPath}/cmp/left",
                sourceByAlias,
                fallbackSourceId,
                diagnostics,
                clause,
                exprId
            );

            SqlExpression right = ParseScalar(
                comparisonMatch.Groups["right"].Value,
                queryId,
                $"{exprPath}/cmp/right",
                sourceByAlias,
                fallbackSourceId,
                diagnostics,
                clause,
                exprId
            );

            return new SqlImportComparisonExpr(
                exprId,
                null,
                SqlImportSemanticType.Boolean,
                CombineResolutionStatus(left, right),
                CreateTrace(queryId, exprId),
                CreateNodeMetadata(),
                left,
                MapComparisonOperator(comparisonMatch.Groups["op"].Value),
                right
            );
        }

        SqlExpression fallback = ParseScalar(
            expression,
            queryId,
            $"{exprPath}/fallback",
            sourceByAlias,
            fallbackSourceId,
            diagnostics,
            clause,
            exprId
        );

        if (fallback.ResolutionStatus is SqlResolutionStatus.Ambiguous or SqlResolutionStatus.Unresolved)
            return fallback;

        return new FunctionExpr(
            exprId,
            null,
            SqlImportSemanticType.Boolean,
            SqlResolutionStatus.Partial,
            CreateTrace(queryId, exprId),
            CreateNodeMetadata(),
            Name: "PREDICATE_GENERIC",
            CanonicalName: null,
            Classification: SqlFunctionClassification.GenericPreserved,
            Arguments: [fallback]
        );
    }

    private SqlExpression ParseScalar(
        string expression,
        string queryId,
        string exprPath,
        IReadOnlyDictionary<string, string> sourceByAlias,
        string fallbackSourceId,
        ICollection<SqlImportDiagnostic> diagnostics,
        SqlImportClause clause,
        string? parentExprId
    )
    {
        string trimmed = StripEnclosingParentheses(expression);
        string exprHash = ComputeHash(trimmed);
        string exprId = StableSqlImportIdGenerator.BuildExprId(
            queryId,
            exprPath,
            "Scalar",
            exprHash,
            parentExprId
        );

        if (trimmed.Equals("NULL", StringComparison.OrdinalIgnoreCase))
        {
            return new SqlImportLiteralExpr(
                exprId,
                null,
                SqlImportSemanticType.Null,
                SqlResolutionStatus.Resolved,
                CreateTrace(queryId, exprId),
                CreateNodeMetadata(),
                trimmed,
                "null",
                true
            );
        }

        if (trimmed.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
        {
            bool boolValue = trimmed.Equals("TRUE", StringComparison.OrdinalIgnoreCase);
            return new SqlImportLiteralExpr(
                exprId,
                null,
                SqlImportSemanticType.Boolean,
                SqlResolutionStatus.Resolved,
                CreateTrace(queryId, exprId),
                CreateNodeMetadata(),
                trimmed,
                boolValue ? "true" : "false",
                false
            );
        }

        if (decimal.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
        {
            return new SqlImportLiteralExpr(
                exprId,
                null,
                SqlImportSemanticType.Decimal,
                SqlResolutionStatus.Resolved,
                CreateTrace(queryId, exprId),
                CreateNodeMetadata(),
                trimmed,
                trimmed,
                false
            );
        }

        if (Regex.IsMatch(trimmed, @"^'([^']|'')*'$", RegexOptions.CultureInvariant))
        {
            return new SqlImportLiteralExpr(
                exprId,
                null,
                SqlImportSemanticType.Text,
                SqlResolutionStatus.Resolved,
                CreateTrace(queryId, exprId),
                CreateNodeMetadata(),
                trimmed,
                trimmed,
                false
            );
        }

        Match columnMatch = Regex.Match(
            trimmed,
            $@"^(?:(?<qualifier>{SqlImportIdentifierNormalizer.IdentifierPattern})\s*\.\s*)?(?<column>{SqlImportIdentifierNormalizer.IdentifierPattern})$",
            RegexOptions.CultureInvariant
        );

        if (columnMatch.Success)
        {
            string? qualifier = columnMatch.Groups["qualifier"].Success
                ? SqlImportIdentifierNormalizer.NormalizeIdentifierToken(columnMatch.Groups["qualifier"].Value)
                : null;

            string column = SqlImportIdentifierNormalizer.NormalizeIdentifierToken(columnMatch.Groups["column"].Value);

            if (!string.IsNullOrWhiteSpace(qualifier))
            {
                if (sourceByAlias.TryGetValue(qualifier, out string? resolvedSourceId))
                {
                    return new ColumnRefExpr(
                        exprId,
                        null,
                        SqlImportSemanticType.Unknown,
                        SqlResolutionStatus.Resolved,
                        CreateTrace(queryId, exprId),
                        CreateNodeMetadata(),
                        qualifier,
                        column,
                        resolvedSourceId
                    );
                }

                diagnostics.Add(CreateAmbiguousOrUnresolvedDiagnostic(
                    SqlImportDiagnosticCodes.ColumnUnresolved,
                    clause,
                    $"Column '{qualifier}.{column}' could not be resolved in visible source aliases.",
                    queryId
                ));

                return new ColumnRefExpr(
                    exprId,
                    null,
                    SqlImportSemanticType.Unknown,
                    SqlResolutionStatus.Unresolved,
                    CreateTrace(queryId, exprId),
                    CreateNodeMetadata(),
                    qualifier,
                    column,
                    null
                );
            }

            if (sourceByAlias.Count == 1)
            {
                return new ColumnRefExpr(
                    exprId,
                    null,
                    SqlImportSemanticType.Unknown,
                    SqlResolutionStatus.Resolved,
                    CreateTrace(queryId, exprId),
                    CreateNodeMetadata(),
                    null,
                    column,
                    fallbackSourceId
                );
            }

            diagnostics.Add(CreateAmbiguousOrUnresolvedDiagnostic(
                SqlImportDiagnosticCodes.ColumnAmbiguous,
                clause,
                $"Column '{column}' is ambiguous across multiple sources.",
                queryId
            ));

            return new ColumnRefExpr(
                exprId,
                null,
                SqlImportSemanticType.Unknown,
                SqlResolutionStatus.Ambiguous,
                CreateTrace(queryId, exprId),
                CreateNodeMetadata(),
                null,
                column,
                null
            );
        }

        Match functionMatch = Regex.Match(
            trimmed,
            @"^(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\((?<args>.*)\)$",
            RegexOptions.Singleline | RegexOptions.CultureInvariant
        );

        if (functionMatch.Success)
        {
            string functionName = functionMatch.Groups["name"].Value;
            string normalizedFunctionName = functionName.ToUpperInvariant();
            SqlFunctionClassification classification = ClassifyFunction(normalizedFunctionName);
            IReadOnlyList<SqlExpression> arguments = ParseFunctionArguments(
                functionMatch.Groups["args"].Value,
                queryId,
                $"{exprPath}/fn/{normalizedFunctionName}",
                sourceByAlias,
                fallbackSourceId,
                clause,
                exprId
            );

            if (classification == SqlFunctionClassification.Unsupported)
            {
                diagnostics.Add(SqlImportDiagnosticCatalog.Create(
                    SqlImportDiagnosticCodes.FunctionUnsupported,
                    clause,
                    $"Function '{functionName}' is not supported in the current AST→IR mapping slice.",
                    queryId
                ));
            }
            else if (classification == SqlFunctionClassification.GenericPreserved)
            {
                bool forbiddenContext = IsGenericFunctionForbiddenContext(clause);
                diagnostics.Add(SqlImportDiagnosticCatalog.Create(
                    forbiddenContext
                        ? SqlImportDiagnosticCodes.FunctionGenericForbiddenContext
                        : SqlImportDiagnosticCodes.FunctionGenericPreserved,
                    clause,
                    forbiddenContext
                        ? $"Generic function '{functionName}' is not allowed in {clause} because it is a structural boolean context."
                        : $"Generic function '{functionName}' was preserved in IR and may reduce semantic equivalence.",
                    queryId
                ));
            }

            return new FunctionExpr(
                exprId,
                null,
                SqlImportSemanticType.Unknown,
                SqlResolutionStatus.Partial,
                CreateTrace(queryId, exprId),
                CreateNodeMetadata(),
                Name: functionName,
                CanonicalName: classification == SqlFunctionClassification.Canonical ? normalizedFunctionName : null,
                Classification: classification,
                Arguments: arguments
            );
        }

        diagnostics.Add(SqlImportDiagnosticCatalog.Create(
            SqlImportDiagnosticCodes.TypeInferenceFallback,
            clause,
            $"Expression '{trimmed}' could not be semantically typed and was preserved as generic literal.",
            queryId
        ));

        return new SqlImportLiteralExpr(
            exprId,
            null,
            SqlImportSemanticType.Unknown,
            SqlResolutionStatus.Partial,
            CreateTrace(queryId, exprId),
            CreateNodeMetadata(),
            trimmed,
            null,
            false
        );
    }

    private static SqlImportComparisonOperator MapComparisonOperator(string op)
    {
        return op switch
        {
            "=" => SqlImportComparisonOperator.Equals,
            "<>" => SqlImportComparisonOperator.NotEquals,
            "!=" => SqlImportComparisonOperator.NotEquals,
            ">" => SqlImportComparisonOperator.GreaterThan,
            ">=" => SqlImportComparisonOperator.GreaterOrEqual,
            "<" => SqlImportComparisonOperator.LessThan,
            "<=" => SqlImportComparisonOperator.LessOrEqual,
            _ => SqlImportComparisonOperator.Equals,
        };
    }

    private static SqlResolutionStatus CombineResolutionStatus(params SqlExpression[] expressions)
    {
        return CombineResolutionStatus(expressions.AsEnumerable());
    }

    private static SqlResolutionStatus CombineResolutionStatus(IEnumerable<SqlExpression> expressions)
    {
        bool hasAmbiguous = false;
        bool hasUnresolved = false;
        bool hasPartial = false;

        foreach (SqlExpression expression in expressions)
        {
            hasAmbiguous |= expression.ResolutionStatus == SqlResolutionStatus.Ambiguous;
            hasUnresolved |= expression.ResolutionStatus == SqlResolutionStatus.Unresolved;
            hasPartial |= expression.ResolutionStatus == SqlResolutionStatus.Partial;
        }

        if (hasAmbiguous)
            return SqlResolutionStatus.Ambiguous;

        if (hasUnresolved)
            return SqlResolutionStatus.Unresolved;

        if (hasPartial)
            return SqlResolutionStatus.Partial;

        return SqlResolutionStatus.Resolved;
    }

    private static IReadOnlyList<string> SplitTopLevelKeyword(string input, string keyword)
    {
        var result = new List<string>();
        int depth = 0;
        int start = 0;
        int index = 0;

        while (index < input.Length)
        {
            char character = input[index];
            if (character == '(')
            {
                depth++;
                index++;
                continue;
            }

            if (character == ')')
            {
                depth = Math.Max(0, depth - 1);
                index++;
                continue;
            }

            if (depth == 0
                && IsTokenAt(input, index, keyword)
                && IsTokenBoundary(input, index - 1)
                && IsTokenBoundary(input, index + keyword.Length))
            {
                string slice = input[start..index].Trim();
                if (!string.IsNullOrWhiteSpace(slice))
                    result.Add(slice);

                index += keyword.Length;
                start = index;
                continue;
            }

            index++;
        }

        string tail = input[start..].Trim();
        if (!string.IsNullOrWhiteSpace(tail))
            result.Add(tail);

        return result.Count == 0 ? [input.Trim()] : result;
    }

    private static IReadOnlyList<string> SplitTopLevelCommaList(string input)
    {
        var result = new List<string>();
        int depth = 0;
        int start = 0;

        for (int index = 0; index < input.Length; index++)
        {
            if (input[index] == '(')
            {
                depth++;
                continue;
            }

            if (input[index] == ')')
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }

            if (input[index] == ',' && depth == 0)
            {
                string item = input[start..index].Trim();
                if (!string.IsNullOrWhiteSpace(item))
                    result.Add(item);

                start = index + 1;
            }
        }

        string tail = input[start..].Trim();
        if (!string.IsNullOrWhiteSpace(tail))
            result.Add(tail);

        return result;
    }

    private static bool IsTokenAt(string input, int start, string token)
    {
        if (start < 0 || start + token.Length > input.Length)
            return false;

        return string.Compare(input, start, token, 0, token.Length, StringComparison.OrdinalIgnoreCase) == 0;
    }

    private static bool IsTokenBoundary(string input, int index)
    {
        if (index < 0 || index >= input.Length)
            return true;

        char character = input[index];
        return !char.IsLetterOrDigit(character) && character != '_';
    }

    private IReadOnlyList<SqlExpression> ParseFunctionArguments(
        string rawArguments,
        string queryId,
        string exprPath,
        IReadOnlyDictionary<string, string> sourceByAlias,
        string fallbackSourceId,
        SqlImportClause clause,
        string parentExprId)
    {
        string trimmedArguments = rawArguments.Trim();
        if (string.IsNullOrWhiteSpace(trimmedArguments))
            return [];

        IReadOnlyList<string> argumentTokens = SplitTopLevelCommaList(trimmedArguments);
        var arguments = new List<SqlExpression>(argumentTokens.Count);

        for (int index = 0; index < argumentTokens.Count; index++)
        {
            string argumentToken = argumentTokens[index].Trim();
            if (argumentToken == "*")
            {
                arguments.Add(CreateStarArgumentLiteral(queryId, exprPath, index, parentExprId));
                continue;
            }

            if (argumentToken.StartsWith("DISTINCT ", StringComparison.OrdinalIgnoreCase))
            {
                string distinctInnerToken = argumentToken["DISTINCT ".Length..].Trim();
                SqlExpression innerArgument = ParseScalarBestEffort(
                    distinctInnerToken,
                    queryId,
                    $"{exprPath}/arg/{index}/distinct-inner",
                    sourceByAlias,
                    fallbackSourceId,
                    clause,
                    parentExprId
                );

                arguments.Add(CreateDistinctArgumentWrapper(
                    argumentToken,
                    innerArgument,
                    queryId,
                    exprPath,
                    index,
                    parentExprId
                ));
                continue;
            }

            SqlExpression parsedArgument = ParseScalarBestEffort(
                argumentToken,
                queryId,
                $"{exprPath}/arg/{index}",
                sourceByAlias,
                fallbackSourceId,
                clause,
                parentExprId
            );

            arguments.Add(parsedArgument);
        }

        return arguments;
    }

    private SqlExpression ParseScalarBestEffort(
        string expression,
        string queryId,
        string exprPath,
        IReadOnlyDictionary<string, string> sourceByAlias,
        string fallbackSourceId,
        SqlImportClause clause,
        string parentExprId)
    {
        var diagnostics = new List<SqlImportDiagnostic>();
        return ParseScalar(
            expression,
            queryId,
            exprPath,
            sourceByAlias,
            fallbackSourceId,
            diagnostics,
            clause,
            parentExprId
        );
    }

    private static SqlImportLiteralExpr CreateStarArgumentLiteral(
        string queryId,
        string exprPath,
        int argumentIndex,
        string parentExprId)
    {
        string rawStar = "*";
        string exprId = StableSqlImportIdGenerator.BuildExprId(
            queryId,
            $"{exprPath}/arg/{argumentIndex}",
            "Scalar",
            ComputeHash(rawStar),
            parentExprId
        );

        return new SqlImportLiteralExpr(
            exprId,
            null,
            SqlImportSemanticType.Unknown,
            SqlResolutionStatus.Resolved,
            CreateTrace(queryId, exprId),
            CreateNodeMetadata(),
            rawStar,
            rawStar,
            false
        );
    }

    private static FunctionExpr CreateDistinctArgumentWrapper(
        string rawArgument,
        SqlExpression innerArgument,
        string queryId,
        string exprPath,
        int argumentIndex,
        string parentExprId)
    {
        string exprId = StableSqlImportIdGenerator.BuildExprId(
            queryId,
            $"{exprPath}/arg/{argumentIndex}/distinct",
            "Scalar",
            ComputeHash(rawArgument),
            parentExprId
        );

        return new FunctionExpr(
            exprId,
            null,
            SqlImportSemanticType.Unknown,
            innerArgument.ResolutionStatus,
            CreateTrace(queryId, exprId),
            CreateNodeMetadata(),
            Name: "DISTINCT",
            CanonicalName: "DISTINCT",
            Classification: SqlFunctionClassification.GenericPreserved,
            Arguments: [innerArgument]
        );
    }

    private static string ComputeHash(string input)
    {
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }

    private static string StripEnclosingParentheses(string expression)
    {
        string current = expression.Trim();
        while (current.Length >= 2
               && current[0] == '('
               && current[^1] == ')'
               && IsWrappedBySingleOuterParentheses(current))
        {
            current = current[1..^1].Trim();
        }

        return current;
    }

    private static bool IsWrappedBySingleOuterParentheses(string expression)
    {
        int depth = 0;
        bool inString = false;

        for (int index = 0; index < expression.Length; index++)
        {
            char character = expression[index];
            if (character == '\'')
            {
                bool escapedQuote = index + 1 < expression.Length && expression[index + 1] == '\'';
                if (escapedQuote)
                {
                    index++;
                    continue;
                }

                inString = !inString;
                continue;
            }

            if (inString)
                continue;

            if (character == '(')
            {
                depth++;
                continue;
            }

            if (character != ')')
                continue;

            depth--;
            if (depth == 0 && index != expression.Length - 1)
                return false;
            if (depth < 0)
                return false;
        }

        return depth == 0;
    }

    private static TraceMeta CreateTrace(string queryId, string exprId)
    {
        return new TraceMeta(queryId, exprId, queryId, null);
    }

    private static SqlIrNodeMetadata CreateNodeMetadata()
    {
        return new SqlIrNodeMetadata(false, null, [], []);
    }

    private static SqlImportDiagnostic CreateAmbiguousOrUnresolvedDiagnostic(
        string code,
        SqlImportClause clause,
        string message,
        string queryId
    )
    {
        return SqlImportDiagnosticCatalog.Create(
            code,
            clause,
            message,
            queryId,
            SqlImportDiagnosticMessages.QualifyColumnRecommendedAction
        );
    }

    private static SqlFunctionClassification ClassifyFunction(string normalizedFunctionName)
    {
        if (CanonicalFunctions.Contains(normalizedFunctionName))
            return SqlFunctionClassification.Canonical;

        if (DialectSpecificFunctions.Contains(normalizedFunctionName))
            return SqlFunctionClassification.DialectSpecific;

        if (UnsupportedFunctions.Contains(normalizedFunctionName))
            return SqlFunctionClassification.Unsupported;

        return SqlFunctionClassification.GenericPreserved;
    }

    private static bool IsGenericFunctionForbiddenContext(SqlImportClause clause)
    {
        return clause is SqlImportClause.Where
            or SqlImportClause.Join
            or SqlImportClause.Having
            or SqlImportClause.GroupBy;
    }
}

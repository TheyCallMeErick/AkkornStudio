using System.Text.RegularExpressions;

namespace AkkornStudio.UI.Services.SqlImport.Rewriting;

public sealed class SqlImportCteRewriteService
{
    private const string SimpleSubqueryForbiddenKeywordsPattern =
        @"\b(WHERE|GROUP\s+BY|HAVING|ORDER\s+BY|JOIN|UNION|LIMIT|OFFSET|TOP|DISTINCT)\b";

    private const string SimpleFilteredSubqueryForbiddenKeywordsPattern =
        @"\b(GROUP\s+BY|HAVING|ORDER\s+BY|JOIN|UNION|LIMIT|OFFSET|TOP|DISTINCT)\b";

    private const string JoinBoundaryPattern =
        @"(?=\s+(?:INNER\s+JOIN|LEFT\s+(?:OUTER\s+)?JOIN|RIGHT\s+(?:OUTER\s+)?JOIN|FULL\s+(?:OUTER\s+)?JOIN|JOIN|WHERE|GROUP\s+BY|HAVING|ORDER\s+BY|LIMIT|OFFSET)\b|$)";

    public IEnumerable<string> AnalyzeCteNameIssues(string sql)
    {
        var issues = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        MatchCollection cteMatches = Regex.Matches(
            sql,
            @"(?:\bWITH\b|,)\s*([^\s,()]+)\s*(?:\([^)]*\))?\s+AS\s*\(",
            RegexOptions.IgnoreCase
        );

        foreach (Match m in cteMatches)
        {
            string rawName = m.Groups[1].Value.Trim();
            if (!Regex.IsMatch(rawName, @"^[A-Za-z_][A-Za-z0-9_]*$"))
            {
                issues.Add($"Invalid CTE name '{rawName}'. Use letters, digits, and underscore; first char must be a letter or underscore.");
                continue;
            }

            if (!seen.Add(rawName))
            {
                issues.Add($"Duplicate CTE name '{rawName}'. CTE names must be unique in a WITH block.");
            }
        }

        return issues;
    }

    public IEnumerable<string> AnalyzeBlockedCteSafetyIssues(string sql)
    {
        if (!Regex.IsMatch(sql, @"^\s*WITH\b", RegexOptions.IgnoreCase))
            yield break;

        if (!TryExtractCteDefinitions(sql, out List<(string name, string body)> definitions, out string mainQuery))
            yield break;

        foreach ((string name, string body) in definitions)
        {
            if (!TryExtractSimpleCteSource(body, out CteSourceRewrite sourceRewrite)
                || string.IsNullOrWhiteSpace(sourceRewrite.WhereClause))
            {
                continue;
            }

            Match outerJoinUse = Regex.Match(
                mainQuery,
                $@"\b(?<join>LEFT\s+(?:OUTER\s+)?JOIN|RIGHT\s+(?:OUTER\s+)?JOIN|FULL\s+(?:OUTER\s+)?JOIN)\s+{Regex.Escape(name)}\b",
                RegexOptions.IgnoreCase);
            if (!outerJoinUse.Success)
                continue;

            string joinKind = Regex.Replace(outerJoinUse.Groups["join"].Value, @"\s+", " ").Trim().ToUpperInvariant();
            yield return $"Filtered CTE '{name}' is used in {joinKind}; rewrite is blocked to avoid changing outer join semantics.";
        }
    }

    public bool TryRewriteSimpleCteQuery(string sql, out string rewrittenSql, out int cteCount)
    {
        rewrittenSql = sql;
        cteCount = 0;

        if (!Regex.IsMatch(sql, @"^\s*WITH\b", RegexOptions.IgnoreCase))
            return false;

        if (!TryExtractCteDefinitions(sql, out List<(string name, string body)> definitions, out string mainQuery))
            return false;

        if (AnalyzeCteNameIssues(sql).Any())
            return false;

        if (TryRewriteSingleOrderedLimitedCteQuery(definitions, mainQuery, out rewrittenSql))
        {
            cteCount = definitions.Count;
            return true;
        }

        if (TryRewriteSingleJoinCteQuery(definitions, mainQuery, out rewrittenSql))
        {
            cteCount = definitions.Count;
            return true;
        }

        var resolvedSources = new Dictionary<string, CteSourceRewrite>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, string body) in definitions)
        {
            if (!TryExtractSimpleCteSource(body, out CteSourceRewrite sourceRewrite))
                return false;

            if (resolvedSources.TryGetValue(sourceRewrite.SourceName, out CteSourceRewrite chainedSource))
            {
                if (!string.IsNullOrWhiteSpace(sourceRewrite.WhereClause))
                    return false;

                resolvedSources[name] = chainedSource;
            }
            else
            {
                resolvedSources[name] = sourceRewrite;
            }
        }

        string resolvedSourceAlternation = string.Join("|", resolvedSources.Keys.Select(Regex.Escape));
        if (resolvedSources.Any(pair => !string.IsNullOrWhiteSpace(pair.Value.WhereClause))
            && Regex.IsMatch(
                mainQuery,
                $@"\b(?:LEFT\s+(?:OUTER\s+)?JOIN|RIGHT\s+(?:OUTER\s+)?JOIN|FULL\s+(?:OUTER\s+)?JOIN)\s+(?:{resolvedSourceAlternation})\b",
                RegexOptions.IgnoreCase))
        {
            return false;
        }

        var extractedWhereClauses = new List<string>();

        string rewrittenMain = Regex.Replace(
            mainQuery,
            @"\b(FROM|JOIN)\s+([A-Za-z_][A-Za-z0-9_]*)(?:\s+(?:AS\s+)?(?!WHERE\b|GROUP\b|HAVING\b|ORDER\b|LIMIT\b|OFFSET\b|INNER\b|LEFT\b|RIGHT\b|FULL\b|JOIN\b)([A-Za-z_][A-Za-z0-9_]*))?",
            m =>
            {
                string keyword = m.Groups[1].Value;
                string source = m.Groups[2].Value;
                string alias = m.Groups[3].Success ? m.Groups[3].Value : source;
                if (!resolvedSources.TryGetValue(source, out CteSourceRewrite resolved))
                    return m.Value;

                if (!string.IsNullOrWhiteSpace(resolved.WhereClause))
                {
                    if (!keyword.Equals("FROM", StringComparison.OrdinalIgnoreCase)
                        && !keyword.Equals("JOIN", StringComparison.OrdinalIgnoreCase))
                    {
                        return m.Value;
                    }

                    extractedWhereClauses.Add(RewriteQualifiedAliasReferences(resolved.WhereClause, resolved.SourceAlias, alias));
                }

                if (!string.IsNullOrWhiteSpace(resolved.WhereClause) && !m.Groups[3].Success)
                    return $"{keyword} {resolved.SourceName} {alias}";

                return m.Groups[3].Success
                    ? $"{keyword} {resolved.SourceName} {alias}"
                    : $"{keyword} {resolved.SourceName}";
            },
            RegexOptions.IgnoreCase
        );

        foreach (string whereClause in extractedWhereClauses)
            rewrittenMain = MergeWhereClause(rewrittenMain, whereClause);

        cteCount = definitions.Count;
        rewrittenSql = rewrittenMain;
        return true;
    }

    public bool TryRewriteSimpleFromSubquery(string sql, out string rewrittenSql)
    {
        rewrittenSql = sql;

        Match fromSubqueryMatch = Regex.Match(
            sql,
            @"\bFROM\s*\(\s*(?<subquery>SELECT.+?)\s*\)\s+(?:AS\s+)?(?<alias>[A-Za-z_][A-Za-z0-9_]*)\b",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );

        if (!fromSubqueryMatch.Success)
            return false;

        string subquerySql = fromSubqueryMatch.Groups["subquery"].Value.Trim();
        string alias = fromSubqueryMatch.Groups["alias"].Value.Trim();

        if (!TryExtractSimplePassThroughFromSubquery(subquerySql, out string sourceName, out string? sourceAlias, out string? whereClause, out IReadOnlyDictionary<string, string> projectionAliases))
            return false;

        string rewrittenBase = sql[..fromSubqueryMatch.Index]
            + $"FROM {sourceName} {alias}"
            + sql[(fromSubqueryMatch.Index + fromSubqueryMatch.Length)..];

        rewrittenBase = RewriteProjectionAliasReferences(rewrittenBase, alias, projectionAliases);

        if (!string.IsNullOrWhiteSpace(whereClause))
        {
            string normalizedWhere = RewriteQualifiedAliasReferences(whereClause, sourceAlias, alias);
            rewrittenSql = MergeWhereClause(rewrittenBase, normalizedWhere);
        }
        else
        {
            rewrittenSql = rewrittenBase;
        }

        return true;
    }

    public bool TryRewriteSimpleJoinSubqueries(string sql, out string rewrittenSql, out int rewriteCount)
    {
        int localRewriteCount = 0;
        var extractedWhereClauses = new List<string>();
        var projectionAliasMaps = new List<(string alias, IReadOnlyDictionary<string, string> aliases)>();
        string leftJoinRewrittenSql = Regex.Replace(
            sql,
            $@"\b(?<join>LEFT\s+(?:OUTER\s+)?JOIN)\s*\(\s*(?<subquery>SELECT.+?)\s*\)\s+(?:AS\s+)?(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s+ON\s+(?<on>.+?){JoinBoundaryPattern}",
            match =>
            {
                string subquerySql = match.Groups["subquery"].Value.Trim();
                string alias = match.Groups["alias"].Value;

                if (!TryExtractSimplePassThroughFromSubquery(subquerySql, out string sourceName, out string? sourceAlias, out string? whereClause, out IReadOnlyDictionary<string, string> projectionAliases))
                    return match.Value;

                if (string.IsNullOrWhiteSpace(whereClause))
                    return match.Value;

                projectionAliasMaps.Add((alias, projectionAliases));
                string normalizedWhere = RewriteQualifiedAliasReferences(whereClause, sourceAlias, alias);
                normalizedWhere = RewriteProjectionAliasReferences(normalizedWhere, alias, projectionAliases);
                localRewriteCount++;
                string normalizedOn = RewriteProjectionAliasReferences(match.Groups["on"].Value.Trim(), alias, projectionAliases);
                return $"{match.Groups["join"].Value} {sourceName} {alias} ON ({normalizedOn}) AND ({normalizedWhere})";
            },
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );

        rewrittenSql = Regex.Replace(
            leftJoinRewrittenSql,
            @"\b(?<join>INNER\s+JOIN|LEFT\s+(?:OUTER\s+)?JOIN|RIGHT\s+(?:OUTER\s+)?JOIN|FULL\s+(?:OUTER\s+)?JOIN|JOIN)\s*\(\s*(?<subquery>SELECT.+?)\s*\)\s+(?:AS\s+)?(?<alias>[A-Za-z_][A-Za-z0-9_]*)\b",
            match =>
            {
                string subquerySql = match.Groups["subquery"].Value.Trim();
                string joinKeyword = match.Groups["join"].Value;
                string alias = match.Groups["alias"].Value;

                if (!TryExtractSimplePassThroughFromSubquery(subquerySql, out string sourceName, out string? sourceAlias, out string? whereClause, out IReadOnlyDictionary<string, string> projectionAliases))
                    return match.Value;

                if (!string.IsNullOrWhiteSpace(whereClause))
                {
                    if (!IsInnerJoinKeyword(joinKeyword))
                        return match.Value;

                    string normalizedWhere = RewriteQualifiedAliasReferences(whereClause, sourceAlias, alias);
                    extractedWhereClauses.Add(RewriteProjectionAliasReferences(normalizedWhere, alias, projectionAliases));
                }

                projectionAliasMaps.Add((alias, projectionAliases));
                localRewriteCount++;
                return $"{joinKeyword} {sourceName} {alias}";
            },
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );

        foreach (string whereClause in extractedWhereClauses)
            rewrittenSql = MergeWhereClause(rewrittenSql, whereClause);

        foreach ((string alias, IReadOnlyDictionary<string, string> aliases) in projectionAliasMaps)
            rewrittenSql = RewriteProjectionAliasReferences(rewrittenSql, alias, aliases);

        rewriteCount = localRewriteCount;
        return rewriteCount > 0;
    }

    public HashSet<string> ExtractSourceAliases(string fromSql)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(fromSql))
            return aliases;

        void AddMatchAliases(Match m)
        {
            string source = m.Groups[1].Value.Trim();
            string explicitAlias = m.Groups[2].Success ? m.Groups[2].Value.Trim() : string.Empty;
            string implicitAlias = m.Groups[3].Success ? m.Groups[3].Value.Trim() : string.Empty;

            if (!string.IsNullOrWhiteSpace(explicitAlias))
                aliases.Add(explicitAlias);
            if (!string.IsNullOrWhiteSpace(implicitAlias))
                aliases.Add(implicitAlias);

            string shortName = source.Split('.').Last();
            if (!string.IsNullOrWhiteSpace(shortName))
                aliases.Add(shortName);
        }

        Match primary = Regex.Match(
            fromSql,
            @"^([A-Za-z_][A-Za-z0-9_\.]*)\s*(?:AS\s+([A-Za-z_][A-Za-z0-9_]*)|([A-Za-z_][A-Za-z0-9_]*))?",
            RegexOptions.IgnoreCase
        );
        if (primary.Success)
            AddMatchAliases(primary);

        MatchCollection joins = Regex.Matches(
            fromSql,
            @"(?:INNER\s+JOIN|LEFT\s+(?:OUTER\s+)?JOIN|RIGHT\s+(?:OUTER\s+)?JOIN|FULL\s+(?:OUTER\s+)?JOIN|JOIN)\s+([A-Za-z_][A-Za-z0-9_\.]*)\s*(?:AS\s+([A-Za-z_][A-Za-z0-9_]*)|([A-Za-z_][A-Za-z0-9_]*))?",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );
        foreach (Match jm in joins)
            AddMatchAliases(jm);

        return aliases;
    }

    public static string DescribeCorrelatedOuterReferences(string sql, HashSet<string> outerAliases)
    {
        if (outerAliases.Count == 0 || string.IsNullOrWhiteSpace(sql))
            return string.Empty;

        var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        MatchCollection qualifiedRefs = Regex.Matches(sql, @"\b([A-Za-z_][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)\b");
        foreach (Match match in qualifiedRefs)
        {
            string alias = match.Groups[1].Value;
            string col = match.Groups[2].Value;
            if (outerAliases.Contains(alias))
                refs.Add($"{alias}.{col}");
        }

        return string.Join(", ", refs.OrderBy(r => r, StringComparer.OrdinalIgnoreCase));
    }

    private static bool TryExtractCteDefinitions(
        string sql,
        out List<(string name, string body)> definitions,
        out string mainQuery
    )
    {
        definitions = [];
        mainQuery = sql;

        int index = 0;
        while (index < sql.Length && char.IsWhiteSpace(sql[index]))
            index++;

        if (!sql.AsSpan(index).StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
            return false;

        index += 4;
        while (index < sql.Length)
        {
            while (index < sql.Length && (char.IsWhiteSpace(sql[index]) || sql[index] == ','))
                index++;
            if (index >= sql.Length)
                return false;

            int nameStart = index;
            if (!IsIdentifierStart(sql[index]))
                return false;

            index++;
            while (index < sql.Length && IsIdentifierPart(sql[index]))
                index++;

            string name = sql[nameStart..index];

            while (index < sql.Length && char.IsWhiteSpace(sql[index]))
                index++;

            if (index < sql.Length && sql[index] == '(')
            {
                index++;
                int columnDepth = 1;
                while (index < sql.Length && columnDepth > 0)
                {
                    if (sql[index] == '(')
                        columnDepth++;
                    else if (sql[index] == ')')
                        columnDepth--;
                    index++;
                }

                if (columnDepth != 0)
                    return false;

                while (index < sql.Length && char.IsWhiteSpace(sql[index]))
                    index++;
            }

            if (!sql.AsSpan(index).StartsWith("AS", StringComparison.OrdinalIgnoreCase))
                return false;
            index += 2;

            while (index < sql.Length && char.IsWhiteSpace(sql[index]))
                index++;
            if (index >= sql.Length || sql[index] != '(')
                return false;

            index++;
            int bodyStart = index;
            int depth = 1;
            while (index < sql.Length && depth > 0)
            {
                if (sql[index] == '(')
                    depth++;
                else if (sql[index] == ')')
                    depth--;
                index++;
            }

            if (depth != 0)
                return false;

            int bodyEnd = index - 1;
            string body = sql[bodyStart..bodyEnd].Trim();
            definitions.Add((name, body));

            while (index < sql.Length && char.IsWhiteSpace(sql[index]))
                index++;

            if (index < sql.Length && sql[index] == ',')
            {
                index++;
                continue;
            }

            mainQuery = sql[index..].Trim();
            return Regex.IsMatch(mainQuery, @"^SELECT\b", RegexOptions.IgnoreCase) && definitions.Count > 0;
        }

        return false;
    }

    private static bool TryExtractSimpleSelectSource(string cteBody, out string sourceName)
    {
        sourceName = string.Empty;

        if (Regex.IsMatch(cteBody, SimpleSubqueryForbiddenKeywordsPattern, RegexOptions.IgnoreCase))
            return false;

        Match m = Regex.Match(
            cteBody,
            @"^\s*SELECT\s+.+?\s+FROM\s+([A-Za-z_][A-Za-z0-9_\.]*)\s*(?:AS\s+[A-Za-z_][A-Za-z0-9_]*|[A-Za-z_][A-Za-z0-9_]*)?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );

        if (!m.Success)
            return false;

        sourceName = m.Groups[1].Value.Trim();
        return !string.IsNullOrWhiteSpace(sourceName);
    }

    private static bool TryExtractSimpleCteSource(string cteBody, out CteSourceRewrite sourceRewrite)
    {
        sourceRewrite = default;

        if (TryExtractSimplePassThroughFromSubquery(cteBody, out string sourceName, out string? sourceAlias, out string? whereClause, out _))
        {
            sourceRewrite = new CteSourceRewrite(sourceName, sourceAlias, whereClause);
            return true;
        }

        if (TryExtractSimpleSelectSource(cteBody, out sourceName))
        {
            sourceRewrite = new CteSourceRewrite(sourceName, null, null);
            return true;
        }

        return false;
    }

    private static bool TryRewriteSingleOrderedLimitedCteQuery(
        List<(string name, string body)> definitions,
        string mainQuery,
        out string rewrittenSql)
    {
        rewrittenSql = mainQuery;

        if (definitions.Count != 1)
            return false;

        (string cteName, string cteBody) = definitions[0];
        if (!TryExtractOrderedLimitedCteBody(cteBody, out OrderedLimitedCteBody parsedBody))
            return false;

        string mainPattern =
            $@"^\s*SELECT\s+(?<projection>.+?)\s+FROM\s+{Regex.Escape(cteName)}(?:\s+(?:AS\s+)?(?<alias>[A-Za-z_][A-Za-z0-9_]*))?\s*$";
        Match mainMatch = Regex.Match(mainQuery, mainPattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!mainMatch.Success)
            return false;

        string mainProjection = mainMatch.Groups["projection"].Value.Trim();
        string cteAlias = mainMatch.Groups["alias"].Success ? mainMatch.Groups["alias"].Value.Trim() : cteName;
        if (Regex.IsMatch(mainProjection, @"\b(FROM|JOIN|WHERE|UNION|GROUP\s+BY|HAVING|ORDER\s+BY|LIMIT|OFFSET|TOP|DISTINCT)\b", RegexOptions.IgnoreCase))
            return false;

        if (!TryBuildProjectionMap(parsedBody.Projection, out Dictionary<string, string> projectionMap))
            return false;

        if (!TryRewriteCteOuterProjection(mainProjection, cteAlias, parsedBody.Projection, projectionMap, out string rewrittenProjection))
            return false;

        rewrittenSql = $"SELECT {rewrittenProjection} FROM {parsedBody.FromClause}";
        if (!string.IsNullOrWhiteSpace(parsedBody.WhereClause))
            rewrittenSql += $" WHERE {parsedBody.WhereClause}";
        if (!string.IsNullOrWhiteSpace(parsedBody.OrderByClause))
            rewrittenSql += $" ORDER BY {parsedBody.OrderByClause}";
        if (parsedBody.LimitClause is int limit)
            rewrittenSql += $" LIMIT {limit}";
        if (parsedBody.OffsetClause is int offset)
            rewrittenSql += $" OFFSET {offset}";

        return true;
    }

    private static bool TryExtractOrderedLimitedCteBody(
        string cteBody,
        out OrderedLimitedCteBody parsed)
    {
        parsed = default;

        if (!Regex.IsMatch(cteBody, @"\b(ORDER\s+BY|LIMIT)\b", RegexOptions.IgnoreCase)
            || Regex.IsMatch(cteBody, @"\b(JOIN|GROUP\s+BY|HAVING|UNION|TOP|DISTINCT)\b|\(\s*SELECT\b|\bWITH\b", RegexOptions.IgnoreCase))
        {
            return false;
        }

        Match match = Regex.Match(
            cteBody,
            @"^\s*SELECT\s+(?<projection>.+?)\s+FROM\s+(?<source>[A-Za-z_][A-Za-z0-9_\.]*)(?:\s+(?:AS\s+)?(?!WHERE\b|ORDER\b|LIMIT\b|OFFSET\b)(?<alias>[A-Za-z_][A-Za-z0-9_]*))?(?:\s+WHERE\s+(?<where>.+?))?(?:\s+ORDER\s+BY\s+(?<order>.+?))?(?:\s+LIMIT\s+(?<limit>\d+))?(?:\s+OFFSET\s+(?<offset>\d+))?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!match.Success)
            return false;

        string projection = match.Groups["projection"].Value.Trim();
        if (!IsSimplePassThroughProjection(projection))
            return false;

        string? whereClause = match.Groups["where"].Success ? match.Groups["where"].Value.Trim() : null;
        string? orderByClause = match.Groups["order"].Success ? match.Groups["order"].Value.Trim() : null;
        if (string.IsNullOrWhiteSpace(orderByClause) && !match.Groups["limit"].Success)
            return false;
        if (!string.IsNullOrWhiteSpace(whereClause)
            && Regex.IsMatch(whereClause, @"\b(ORDER\s+BY|LIMIT|OFFSET)\b|\(\s*SELECT\b|\bWITH\b", RegexOptions.IgnoreCase))
        {
            return false;
        }

        int? limit = match.Groups["limit"].Success
            ? int.Parse(match.Groups["limit"].Value)
            : null;
        int? offset = match.Groups["offset"].Success
            ? int.Parse(match.Groups["offset"].Value)
            : null;

        string fromClause = match.Groups["source"].Value.Trim();
        if (match.Groups["alias"].Success)
            fromClause += $" {match.Groups["alias"].Value.Trim()}";

        parsed = new OrderedLimitedCteBody(
            projection,
            fromClause,
            whereClause,
            orderByClause,
            limit,
            offset);
        return true;
    }

    private static bool TryRewriteSingleJoinCteQuery(
        List<(string name, string body)> definitions,
        string mainQuery,
        out string rewrittenSql)
    {
        rewrittenSql = mainQuery;

        if (definitions.Count != 1)
            return false;

        (string cteName, string cteBody) = definitions[0];
        if (!TryExtractSimpleJoinCteBody(cteBody, out string cteProjection, out string cteFromClause))
            return false;

        string mainPattern =
            $@"^\s*SELECT\s+(?<projection>.+?)\s+FROM\s+{Regex.Escape(cteName)}(?:\s+(?:AS\s+)?(?<alias>[A-Za-z_][A-Za-z0-9_]*))?(?:\s+WHERE\s+(?<where>.+))?\s*$";
        Match mainMatch = Regex.Match(mainQuery, mainPattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!mainMatch.Success)
            return false;

        string mainProjection = mainMatch.Groups["projection"].Value.Trim();
        string cteAlias = mainMatch.Groups["alias"].Success ? mainMatch.Groups["alias"].Value.Trim() : cteName;
        string? mainWhere = mainMatch.Groups["where"].Success ? mainMatch.Groups["where"].Value.Trim() : null;
        if (Regex.IsMatch(mainProjection, @"\b(FROM|JOIN|UNION|GROUP\s+BY|HAVING|ORDER\s+BY|LIMIT|OFFSET|TOP|DISTINCT)\b", RegexOptions.IgnoreCase)
            || (!string.IsNullOrWhiteSpace(mainWhere)
                && Regex.IsMatch(mainWhere, @"\(\s*SELECT\b|\bWITH\b|\bGROUP\s+BY\b|\bHAVING\b|\bORDER\s+BY\b|\bLIMIT\b|\bOFFSET\b", RegexOptions.IgnoreCase)))
        {
            return false;
        }

        if (!TryBuildProjectionMap(cteProjection, out Dictionary<string, string> projectionMap))
            return false;

        if (!TryRewriteCteOuterProjection(mainProjection, cteAlias, cteProjection, projectionMap, out string rewrittenProjection))
            return false;

        string rewrittenWhere = string.IsNullOrWhiteSpace(mainWhere)
            ? string.Empty
            : RewriteCteOuterReferences(mainWhere, cteAlias, projectionMap);

        rewrittenSql = $"SELECT {rewrittenProjection} FROM {cteFromClause}";
        if (!string.IsNullOrWhiteSpace(rewrittenWhere))
            rewrittenSql += $" WHERE {rewrittenWhere}";

        return true;
    }

    private static bool TryExtractSimpleJoinCteBody(
        string cteBody,
        out string projection,
        out string fromClause)
    {
        projection = string.Empty;
        fromClause = string.Empty;

        if (!Regex.IsMatch(cteBody, @"\bJOIN\b", RegexOptions.IgnoreCase)
            || Regex.IsMatch(cteBody, @"\b(WHERE|GROUP\s+BY|HAVING|ORDER\s+BY|UNION|LIMIT|OFFSET|TOP|DISTINCT)\b|\(\s*SELECT\b|\bWITH\b", RegexOptions.IgnoreCase))
        {
            return false;
        }

        Match match = Regex.Match(
            cteBody,
            @"^\s*SELECT\s+(?<projection>.+?)\s+FROM\s+(?<from>.+?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!match.Success)
            return false;

        projection = match.Groups["projection"].Value.Trim();
        fromClause = match.Groups["from"].Value.Trim();
        return IsSimplePassThroughProjection(projection);
    }

    private static bool TryBuildProjectionMap(
        string projection,
        out Dictionary<string, string> projectionMap)
    {
        projectionMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string rawPart in projection.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            string part = rawPart.Trim();
            if (part == "*" || Regex.IsMatch(part, @"^[A-Za-z_][A-Za-z0-9_]*\.\*$", RegexOptions.IgnoreCase))
                continue;

            Match aliasMatch = Regex.Match(
                part,
                @"^(?<source>[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)?)\s+AS\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)$",
                RegexOptions.IgnoreCase);
            if (aliasMatch.Success)
            {
                projectionMap[aliasMatch.Groups["alias"].Value] = aliasMatch.Groups["source"].Value;
                continue;
            }

            if (Regex.IsMatch(part, @"^[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)?$", RegexOptions.IgnoreCase))
            {
                projectionMap[part.Split('.').Last()] = part;
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool TryRewriteCteOuterProjection(
        string mainProjection,
        string cteAlias,
        string cteProjection,
        IReadOnlyDictionary<string, string> projectionMap,
        out string rewrittenProjection)
    {
        rewrittenProjection = mainProjection;

        if (mainProjection == "*")
        {
            rewrittenProjection = cteProjection;
            return true;
        }

        var rewrittenParts = new List<string>();
        foreach (string rawPart in mainProjection.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            string part = rawPart.Trim();
            Match aliasMatch = Regex.Match(
                part,
                $@"^(?:(?:{Regex.Escape(cteAlias)})\.)?(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:\s+AS\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*))?$",
                RegexOptions.IgnoreCase);
            if (!aliasMatch.Success)
                return false;

            string name = aliasMatch.Groups["name"].Value;
            if (!projectionMap.TryGetValue(name, out string? sourceExpression))
                return false;

            string outputAlias = aliasMatch.Groups["alias"].Success ? aliasMatch.Groups["alias"].Value : name;
            string sourceColumn = sourceExpression.Split('.').Last();
            rewrittenParts.Add(string.Equals(sourceColumn, outputAlias, StringComparison.OrdinalIgnoreCase)
                ? sourceExpression
                : $"{sourceExpression} AS {outputAlias}");
        }

        rewrittenProjection = string.Join(", ", rewrittenParts);
        return rewrittenParts.Count > 0;
    }

    private static string RewriteCteOuterReferences(
        string sql,
        string cteAlias,
        IReadOnlyDictionary<string, string> projectionMap)
    {
        string rewritten = sql;
        foreach (KeyValuePair<string, string> projection in projectionMap)
        {
            rewritten = Regex.Replace(
                rewritten,
                $@"\b{Regex.Escape(cteAlias)}\.{Regex.Escape(projection.Key)}\b",
                projection.Value,
                RegexOptions.IgnoreCase);
        }

        return rewritten;
    }

    private static bool TryExtractSimplePassThroughSelectSource(string sql, out string sourceName)
    {
        sourceName = string.Empty;

        if (Regex.IsMatch(sql, SimpleSubqueryForbiddenKeywordsPattern, RegexOptions.IgnoreCase))
            return false;

        Match m = Regex.Match(
            sql,
            @"^\s*SELECT\s+(?<projection>.+?)\s+FROM\s+(?<source>[A-Za-z_][A-Za-z0-9_\.]*)\s*(?:AS\s+[A-Za-z_][A-Za-z0-9_]*|[A-Za-z_][A-Za-z0-9_]*)?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );

        if (!m.Success)
            return false;

        if (!IsSimplePassThroughProjection(m.Groups["projection"].Value))
            return false;

        sourceName = m.Groups["source"].Value.Trim();
        return !string.IsNullOrWhiteSpace(sourceName);
    }

    private static bool TryExtractSimplePassThroughFromSubquery(
        string sql,
        out string sourceName,
        out string? sourceAlias,
        out string? whereClause,
        out IReadOnlyDictionary<string, string> projectionAliases
    )
    {
        sourceName = string.Empty;
        sourceAlias = null;
        whereClause = null;
        projectionAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (Regex.IsMatch(sql, SimpleFilteredSubqueryForbiddenKeywordsPattern, RegexOptions.IgnoreCase))
            return false;

        Match m = Regex.Match(
            sql,
            @"^\s*SELECT\s+(?<projection>.+?)\s+FROM\s+(?<source>[A-Za-z_][A-Za-z0-9_\.]*)(?:\s+(?:AS\s+)?(?<alias>[A-Za-z_][A-Za-z0-9_]*))?(?:\s+WHERE\s+(?<where>.+))?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );

        if (!m.Success)
            return false;

        if (!IsSimplePassThroughProjection(m.Groups["projection"].Value, out Dictionary<string, string> aliases))
            return false;

        string parsedWhere = m.Groups["where"].Success ? m.Groups["where"].Value.Trim() : string.Empty;
        if (Regex.IsMatch(parsedWhere, @"\(\s*SELECT\b|\bWITH\b", RegexOptions.IgnoreCase))
            return false;

        sourceName = m.Groups["source"].Value.Trim();
        sourceAlias = m.Groups["alias"].Success ? m.Groups["alias"].Value.Trim() : null;
        whereClause = string.IsNullOrWhiteSpace(parsedWhere) ? null : parsedWhere;
        projectionAliases = aliases;
        return !string.IsNullOrWhiteSpace(sourceName);
    }

    private static string RewriteQualifiedAliasReferences(string whereClause, string? sourceAlias, string outerAlias)
    {
        if (string.IsNullOrWhiteSpace(sourceAlias)
            || string.Equals(sourceAlias, outerAlias, StringComparison.OrdinalIgnoreCase))
            return whereClause;

        return Regex.Replace(
            whereClause,
            $@"\b{Regex.Escape(sourceAlias)}\.",
            outerAlias + ".",
            RegexOptions.IgnoreCase
        );
    }

    private static string RewriteProjectionAliasReferences(
        string sql,
        string outerAlias,
        IReadOnlyDictionary<string, string> projectionAliases)
    {
        if (projectionAliases.Count == 0)
            return sql;

        string rewritten = sql;
        foreach (KeyValuePair<string, string> aliasPair in projectionAliases)
        {
            rewritten = Regex.Replace(
                rewritten,
                $@"\b{Regex.Escape(outerAlias)}\.{Regex.Escape(aliasPair.Key)}\b",
                $"{outerAlias}.{aliasPair.Value}",
                RegexOptions.IgnoreCase);
        }

        return rewritten;
    }

    private static string MergeWhereClause(string sql, string whereClause)
    {
        Match existingWhere = Regex.Match(sql, @"\bWHERE\s+", RegexOptions.IgnoreCase);
        if (existingWhere.Success)
        {
            int insertAt = existingWhere.Index + existingWhere.Length;
            return sql[..insertAt] + $"({whereClause}) AND " + sql[insertAt..];
        }

        Match clauseBoundary = Regex.Match(
            sql,
            @"\s+(GROUP\s+BY|HAVING|ORDER\s+BY|LIMIT|OFFSET)\b",
            RegexOptions.IgnoreCase
        );

        if (!clauseBoundary.Success)
            return $"{sql} WHERE {whereClause}";

        return sql[..clauseBoundary.Index] + $" WHERE {whereClause}" + sql[clauseBoundary.Index..];
    }

    private static bool IsInnerJoinKeyword(string joinKeyword)
    {
        string normalized = Regex.Replace(joinKeyword, @"\s+", " ").Trim();
        return normalized.Equals("JOIN", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("INNER JOIN", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSimplePassThroughProjection(string projection) =>
        IsSimplePassThroughProjection(projection, out _);

    private static bool IsSimplePassThroughProjection(string projection, out Dictionary<string, string> aliases)
    {
        aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string rawPart in projection.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            string part = rawPart.Trim();
            if (part == "*")
                continue;

            if (Regex.IsMatch(part, @"^[A-Za-z_][A-Za-z0-9_]*\.\*$", RegexOptions.IgnoreCase))
                continue;

            if (Regex.IsMatch(part, @"^[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)?$", RegexOptions.IgnoreCase))
                continue;

            Match aliasMatch = Regex.Match(
                part,
                @"^(?<source>[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)?)\s+AS\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)$",
                RegexOptions.IgnoreCase);
            if (aliasMatch.Success)
            {
                string source = aliasMatch.Groups["source"].Value;
                string sourceColumn = source.Split('.').Last();
                string alias = aliasMatch.Groups["alias"].Value;
                if (!string.Equals(sourceColumn, alias, StringComparison.OrdinalIgnoreCase))
                    aliases[alias] = sourceColumn;
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_';

    private readonly record struct CteSourceRewrite(string SourceName, string? SourceAlias, string? WhereClause);

    private readonly record struct OrderedLimitedCteBody(
        string Projection,
        string FromClause,
        string? WhereClause,
        string? OrderByClause,
        int? LimitClause,
        int? OffsetClause);
}

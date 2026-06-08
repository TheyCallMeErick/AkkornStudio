namespace AkkornStudio.UI.Services.SqlEditor;

public sealed class SqlSelectionExtractor
{
    public string? ExtractSelectionOrCurrentStatement(
        string fullText,
        int selectionStart,
        int selectionLength,
        int caretOffset)
    {
        ArgumentNullException.ThrowIfNull(fullText);

        if (selectionStart < 0 || selectionStart > fullText.Length)
            throw new ArgumentOutOfRangeException(nameof(selectionStart));

        if (selectionLength < 0 || selectionStart + selectionLength > fullText.Length)
            throw new ArgumentOutOfRangeException(nameof(selectionLength));

        if (caretOffset < 0 || caretOffset > fullText.Length)
            throw new ArgumentOutOfRangeException(nameof(caretOffset));

        if (selectionLength > 0)
        {
            string selected = fullText.Substring(selectionStart, selectionLength).Trim();
            return string.IsNullOrWhiteSpace(selected) ? null : selected;
        }

        if (string.IsNullOrWhiteSpace(fullText))
            return null;

        List<(int RawStart, int RawEnd, string Sql)> statements = ExtractStatements(fullText);
        if (statements.Count == 0)
            return null;

        foreach ((int rawStart, int rawEnd, string sql) in statements)
        {
            if (caretOffset >= rawStart && caretOffset <= rawEnd)
                return sql;
        }

        if (statements.Any(s => s.RawEnd < caretOffset))
        {
            (int RawStart, int RawEnd, string Sql) previous = statements.Last(s => s.RawEnd < caretOffset);
            return previous.Sql;
        }

        return statements[0].Sql;
    }

    private static List<(int RawStart, int RawEnd, string Sql)> ExtractStatements(string sql)
    {
        var results = new List<(int RawStart, int RawEnd, string Sql)>();

        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        bool inLineComment = false;
        bool inBlockComment = false;
        int segmentStart = 0;

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
                    i++;
                    inBlockComment = false;
                }
                continue;
            }

            if (!inSingleQuote && !inDoubleQuote)
            {
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
            }

            if (c == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (c == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (c == ';' && !inSingleQuote && !inDoubleQuote)
            {
                AddSegment(sql, results, segmentStart, i - 1);
                segmentStart = i + 1;
            }
        }

        AddSegment(sql, results, segmentStart, sql.Length - 1);
        return results;
    }

    private static void AddSegment(
        string fullText,
        ICollection<(int RawStart, int RawEnd, string Sql)> results,
        int rawStart,
        int rawEnd)
    {
        if (rawEnd < rawStart)
            return;

        string raw = fullText.Substring(rawStart, rawEnd - rawStart + 1);
        if (string.IsNullOrWhiteSpace(raw))
            return;

        string trimmed = raw.Trim();
        if (string.IsNullOrWhiteSpace(RemoveLeadingComments(trimmed)))
            return;

        int leading = raw.Length - raw.TrimStart().Length;
        int trailing = raw.Length - raw.TrimEnd().Length;

        int effectiveStart = rawStart + leading;
        int effectiveEnd = rawEnd - trailing;

        results.Add((effectiveStart, effectiveEnd, trimmed));
    }

    private static string RemoveLeadingComments(string sql)
    {
        string current = sql.TrimStart();

        while (true)
        {
            if (current.StartsWith("--", StringComparison.Ordinal))
            {
                int newLineIndex = current.IndexOf('\n');
                if (newLineIndex < 0)
                    return string.Empty;

                current = current[(newLineIndex + 1)..].TrimStart();
                continue;
            }

            if (current.StartsWith("/*", StringComparison.Ordinal))
            {
                int endIndex = current.IndexOf("*/", StringComparison.Ordinal);
                if (endIndex < 0)
                    return string.Empty;

                current = current[(endIndex + 2)..].TrimStart();
                continue;
            }

            return current;
        }
    }
}

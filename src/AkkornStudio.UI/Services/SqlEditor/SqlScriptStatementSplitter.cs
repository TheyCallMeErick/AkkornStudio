namespace AkkornStudio.UI.Services.SqlEditor;

public sealed class SqlScriptStatementSplitter
{
    public IReadOnlyList<SqlStatement> Split(string script)
    {
        ArgumentNullException.ThrowIfNull(script);

        var result = new List<SqlStatement>();
        if (string.IsNullOrWhiteSpace(script))
            return result;

        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        bool inLineComment = false;
        bool inBlockComment = false;
        int segmentStart = 0;

        for (int i = 0; i < script.Length; i++)
        {
            char c = script[i];
            char next = i + 1 < script.Length ? script[i + 1] : '\0';

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
                AddSegment(script, result, segmentStart, i - 1);
                segmentStart = i + 1;
            }
        }

        AddSegment(script, result, segmentStart, script.Length - 1);
        return result;
    }

    private static void AddSegment(
        string fullText,
        ICollection<SqlStatement> target,
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

        int startLine = GetLine(fullText, effectiveStart);
        int endLine = GetLine(fullText, effectiveEnd);
        StatementKind kind = DetectKind(trimmed);

        target.Add(new SqlStatement(trimmed, startLine, endLine, kind));
    }

    private static int GetLine(string text, int offset)
    {
        int line = 1;
        for (int i = 0; i < offset && i < text.Length; i++)
        {
            if (text[i] == '\n')
                line++;
        }

        return line;
    }

    private static StatementKind DetectKind(string sql)
    {
        string upper = RemoveLeadingComments(sql).ToUpperInvariant();

        if (upper.StartsWith("SELECT ", StringComparison.Ordinal))
            return StatementKind.Select;
        if (upper.StartsWith("INSERT ", StringComparison.Ordinal))
            return StatementKind.Insert;
        if (upper.StartsWith("UPDATE ", StringComparison.Ordinal))
            return StatementKind.Update;
        if (upper.StartsWith("DELETE ", StringComparison.Ordinal))
            return StatementKind.Delete;
        if (upper.StartsWith("CREATE TABLE ", StringComparison.Ordinal))
            return StatementKind.CreateTable;
        if (upper.StartsWith("ALTER TABLE ", StringComparison.Ordinal))
            return StatementKind.AlterTable;
        if (upper.StartsWith("CREATE VIEW ", StringComparison.Ordinal))
            return StatementKind.CreateView;
        if (upper.StartsWith("CREATE INDEX ", StringComparison.Ordinal))
            return StatementKind.CreateIndex;
        if (upper.StartsWith("DROP TABLE ", StringComparison.Ordinal))
            return StatementKind.DropTable;
        if (upper.StartsWith("DROP VIEW ", StringComparison.Ordinal))
            return StatementKind.DropView;

        return StatementKind.Other;
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

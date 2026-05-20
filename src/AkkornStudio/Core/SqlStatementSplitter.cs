using System.Text;

namespace AkkornStudio.Core;

/// <summary>
/// Splits SQL scripts into executable statements while respecting quoted literals
/// and comment blocks.
/// </summary>
public static class SqlStatementSplitter
{
    public static IReadOnlyList<string> Split(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return [];

        var statements = new List<string>();
        var sb = new StringBuilder();

        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        bool inLineComment = false;
        int blockCommentDepth = 0;
        bool inDollarQuote = false;
        string dollarQuoteDelimiter = string.Empty;

        for (int i = 0; i < sql.Length; i++)
        {
            char c = sql[i];
            char next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (inLineComment)
            {
                sb.Append(c);
                if (c == '\n')
                    inLineComment = false;
                continue;
            }

            if (blockCommentDepth > 0)
            {
                sb.Append(c);
                if (c == '/' && next == '*')
                {
                    sb.Append(next);
                    i++;
                    blockCommentDepth++;
                    continue;
                }

                if (c == '*' && next == '/')
                {
                    sb.Append(next);
                    i++;
                    blockCommentDepth--;
                }

                continue;
            }

            if (inDollarQuote)
            {
                if (IsDelimiterAt(sql, i, dollarQuoteDelimiter))
                {
                    sb.Append(dollarQuoteDelimiter);
                    i += dollarQuoteDelimiter.Length - 1;
                    inDollarQuote = false;
                    dollarQuoteDelimiter = string.Empty;
                    continue;
                }

                sb.Append(c);
                continue;
            }

            if (!inSingleQuote && !inDoubleQuote)
            {
                if (c == '-' && next == '-')
                {
                    sb.Append(c);
                    sb.Append(next);
                    i++;
                    inLineComment = true;
                    continue;
                }

                if (c == '/' && next == '*')
                {
                    sb.Append(c);
                    sb.Append(next);
                    i++;
                    blockCommentDepth = 1;
                    continue;
                }

                if (c == '$' && TryReadDollarQuoteDelimiter(sql, i, out string delimiter))
                {
                    sb.Append(delimiter);
                    i += delimiter.Length - 1;
                    inDollarQuote = true;
                    dollarQuoteDelimiter = delimiter;
                    continue;
                }
            }

            if (c == '\'' && !inDoubleQuote)
            {
                if (inSingleQuote && next == '\'')
                {
                    sb.Append(c);
                    sb.Append(next);
                    i++;
                    continue;
                }

                inSingleQuote = !inSingleQuote;
                sb.Append(c);
                continue;
            }

            if (c == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                sb.Append(c);
                continue;
            }

            if (c == ';' && !inSingleQuote && !inDoubleQuote)
            {
                string statement = sb.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(statement))
                    statements.Add(statement);
                sb.Clear();
                continue;
            }

            sb.Append(c);
        }

        string tail = sb.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(tail))
            statements.Add(tail);

        return statements;
    }

    private static bool TryReadDollarQuoteDelimiter(string sql, int startIndex, out string delimiter)
    {
        delimiter = string.Empty;
        int cursor = startIndex + 1;
        while (cursor < sql.Length)
        {
            char c = sql[cursor];
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                cursor++;
                continue;
            }

            break;
        }

        if (cursor >= sql.Length)
            return false;

        if (sql[cursor] != '$')
            return false;

        delimiter = sql.Substring(startIndex, cursor - startIndex + 1);
        return true;
    }

    private static bool IsDelimiterAt(string sql, int index, string delimiter)
    {
        if (index + delimiter.Length > sql.Length)
            return false;

        return string.CompareOrdinal(sql, index, delimiter, 0, delimiter.Length) == 0;
    }
}

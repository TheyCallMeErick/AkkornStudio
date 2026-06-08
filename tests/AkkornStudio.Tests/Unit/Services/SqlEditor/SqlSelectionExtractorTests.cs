using AkkornStudio.UI.Services.SqlEditor;

namespace AkkornStudio.Tests.Unit.Services.SqlEditor;

public sealed class SqlSelectionExtractorTests
{
    [Fact]
    public void ExtractSelectionOrCurrentStatement_WhenSelectionHasSql_ReturnsSelection()
    {
        var sut = new SqlSelectionExtractor();
        const string script = "SELECT 1; SELECT 2;";
        int start = script.IndexOf("SELECT 2", StringComparison.Ordinal);

        string? result = sut.ExtractSelectionOrCurrentStatement(script, start, "SELECT 2".Length, caretOffset: 0);

        Assert.Equal("SELECT 2", result);
    }

    [Fact]
    public void ExtractSelectionOrCurrentStatement_WhenSelectionIsWhitespace_ReturnsNull()
    {
        var sut = new SqlSelectionExtractor();

        string? result = sut.ExtractSelectionOrCurrentStatement("   ", 0, 3, 0);

        Assert.Null(result);
    }

    [Fact]
    public void ExtractSelectionOrCurrentStatement_WithoutSelection_ReturnsCurrentStatementByCaret()
    {
        var sut = new SqlSelectionExtractor();
        const string script = "SELECT 1;\nSELECT 2;";
        int caret = script.IndexOf("SELECT 2", StringComparison.Ordinal) + 2;

        string? result = sut.ExtractSelectionOrCurrentStatement(script, 0, 0, caret);

        Assert.Equal("SELECT 2", result);
    }

    [Fact]
    public void ExtractSelectionOrCurrentStatement_SemicolonInsideLiteral_DoesNotBreakStatement()
    {
        var sut = new SqlSelectionExtractor();
        const string script = "SELECT 'a;b' AS value; SELECT 2;";
        int caret = script.IndexOf("value", StringComparison.Ordinal);

        string? result = sut.ExtractSelectionOrCurrentStatement(script, 0, 0, caret);

        Assert.Equal("SELECT 'a;b' AS value", result);
    }

    [Fact]
    public void ExtractSelectionOrCurrentStatement_SemicolonInsideComment_DoesNotBreakStatement()
    {
        var sut = new SqlSelectionExtractor();
        const string script = "SELECT 1 -- keep;comment\n; SELECT 2;";
        int caret = script.IndexOf("keep", StringComparison.Ordinal);

        string? result = sut.ExtractSelectionOrCurrentStatement(script, 0, 0, caret);

        Assert.Equal("SELECT 1 -- keep;comment", result);
    }

    [Fact]
    public void ExtractSelectionOrCurrentStatement_WhenCaretAfterLastStatement_ReturnsLastStatement()
    {
        var sut = new SqlSelectionExtractor();
        const string script = "SELECT 1; SELECT 2;";

        string? result = sut.ExtractSelectionOrCurrentStatement(script, 0, 0, script.Length);

        Assert.Equal("SELECT 2", result);
    }

    [Fact]
    public void ExtractSelectionOrCurrentStatement_InvalidSelectionStart_Throws()
    {
        var sut = new SqlSelectionExtractor();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            sut.ExtractSelectionOrCurrentStatement("SELECT 1", -1, 0, 0));
    }

    [Fact]
    public void ExtractSelectionOrCurrentStatement_InvalidSelectionLength_Throws()
    {
        var sut = new SqlSelectionExtractor();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            sut.ExtractSelectionOrCurrentStatement("SELECT 1", 0, 100, 0));
    }

    [Fact]
    public void ExtractSelectionOrCurrentStatement_InvalidCaretOffset_Throws()
    {
        var sut = new SqlSelectionExtractor();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            sut.ExtractSelectionOrCurrentStatement("SELECT 1", 0, 0, -1));
    }

    [Fact]
    public void ExtractSelectionOrCurrentStatement_OnlyComments_ReturnsNull()
    {
        var sut = new SqlSelectionExtractor();
        const string script = """
                              -- ========= SITUACAO =========
                              -- 8 => SUSPENSO
                              -- 9 => ENCERRADO
                              """;

        string? result = sut.ExtractSelectionOrCurrentStatement(script, 0, 0, script.Length);

        Assert.Null(result);
    }

    [Fact]
    public void ExtractSelectionOrCurrentStatement_CommentPrefixPlusQuery_ReturnsQueryStatement()
    {
        var sut = new SqlSelectionExtractor();
        const string script = """
                              -- ========= SITUACAO =========
                              -- 8 => SUSPENSO
                              SELECT 1;
                              """;

        int caret = script.IndexOf("SITUACAO", StringComparison.Ordinal);
        string? result = sut.ExtractSelectionOrCurrentStatement(script, 0, 0, caret);

        Assert.Equal("-- ========= SITUACAO =========\n-- 8 => SUSPENSO\nSELECT 1", result);
    }
}

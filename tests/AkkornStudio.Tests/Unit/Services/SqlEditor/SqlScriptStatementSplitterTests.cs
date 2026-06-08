using AkkornStudio.UI.Services.SqlEditor;

namespace AkkornStudio.Tests.Unit.Services.SqlEditor;

public sealed class SqlScriptStatementSplitterTests
{
    [Fact]
    public void Split_WhenMultipleStatements_ReturnsSeparatedStatements()
    {
        var sut = new SqlScriptStatementSplitter();

        IReadOnlyList<SqlStatement> result = sut.Split("SELECT 1; SELECT 2;");

        Assert.Equal(2, result.Count);
        Assert.Equal("SELECT 1", result[0].Sql);
        Assert.Equal("SELECT 2", result[1].Sql);
        Assert.Equal(StatementKind.Select, result[0].Kind);
        Assert.Equal(StatementKind.Select, result[1].Kind);
    }

    [Fact]
    public void Split_IgnoresSemicolonInsideStringAndComment()
    {
        var sut = new SqlScriptStatementSplitter();
        const string sql = """
                           SELECT ';' AS semi;
                           -- comment with ; here
                           UPDATE orders SET note = 'a;b' WHERE id = 1;
                           """;

        IReadOnlyList<SqlStatement> result = sut.Split(sql);

        Assert.Equal(2, result.Count);
        Assert.Equal(StatementKind.Select, result[0].Kind);
        Assert.Equal(StatementKind.Update, result[1].Kind);
    }

    [Fact]
    public void Split_ComputesLineRange()
    {
        var sut = new SqlScriptStatementSplitter();
        const string sql = """
                           SELECT 1;
                           UPDATE orders
                           SET status = 'x'
                           WHERE id = 10;
                           """;

        IReadOnlyList<SqlStatement> result = sut.Split(sql);

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].StartLine);
        Assert.Equal(1, result[0].EndLine);
        Assert.Equal(2, result[1].StartLine);
        Assert.Equal(4, result[1].EndLine);
    }

    [Fact]
    public void Split_IgnoresCommentOnlySegments()
    {
        var sut = new SqlScriptStatementSplitter();
        const string sql = """
                           -- bloco solto de comentário
                           -- sem instrução executável
                           ;
                           -- ========= SITUACAO =========
                           -- 8 => SUSPENSO
                           -- 9 => ENCERRADO
                           SELECT 1;
                           """;

        IReadOnlyList<SqlStatement> result = sut.Split(sql);

        Assert.Single(result);
        Assert.Contains("SELECT 1", result[0].Sql, StringComparison.Ordinal);
        Assert.Equal(StatementKind.Select, result[0].Kind);
    }
}

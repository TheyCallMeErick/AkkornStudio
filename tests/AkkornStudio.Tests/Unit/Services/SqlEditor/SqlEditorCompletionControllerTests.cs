using AkkornStudio.Core;
using AkkornStudio.UI.Services.SqlEditor;

namespace AkkornStudio.Tests.Unit.Services.SqlEditor;

public sealed class SqlEditorCompletionControllerTests
{
    [Fact]
    public void GetCompletionRequest_WithKeywordPrefix_ReturnsKeywordSuggestion()
    {
        var sut = new SqlEditorCompletionController();

        SqlCompletionRequest request = sut.GetCompletionRequest(
            fullText: "SEL",
            caretOffset: 3,
            metadata: null,
            provider: DatabaseProvider.Postgres,
            connectionProfileId: null);

        Assert.Equal(3, request.PrefixLength);
        Assert.Contains(request.Suggestions, suggestion =>
            suggestion.Kind == SqlCompletionKind.Keyword
            && suggestion.Label == "SELECT");
    }

    [Fact]
    public void TryResolveSignatureHelp_WithFunctionCall_ReturnsSignature()
    {
        var sut = new SqlEditorCompletionController();
        const string sql = "SELECT DATE_TRUNC('day', NOW())";
        int caretOffset = sql.IndexOf("NOW", StringComparison.Ordinal);

        SignatureHelpInfo? help = sut.TryResolveSignatureHelp(
            fullText: sql,
            caretOffset: caretOffset,
            provider: DatabaseProvider.Postgres);

        Assert.NotNull(help);
        Assert.Equal("DATE_TRUNC", help!.Signature.Name);
        Assert.False(string.IsNullOrWhiteSpace(help!.DisplayText));
    }

    [Fact]
    public async Task RequestCompletionAsync_ReturnsFinalSnapshotAndReportsProgress()
    {
        var sut = new SqlEditorCompletionController();
        var stages = new List<SqlCompletionPipelineStage>();
        var progress = new RecordingProgress(snapshot => stages.Add(snapshot.Stage));

        SqlCompletionStageSnapshot snapshot = await sut.RequestCompletionAsync(
            fullText: "SEL",
            caretOffset: 3,
            metadata: null,
            provider: DatabaseProvider.Postgres,
            connectionProfileId: null,
            progress: progress,
            cancellationToken: CancellationToken.None);

        Assert.True(snapshot.IsFinal);
        Assert.Equal(SqlCompletionPipelineStage.Final, snapshot.Stage);
        Assert.Contains(SqlCompletionPipelineStage.Tier0, stages);
        Assert.Contains(SqlCompletionPipelineStage.Tier3, stages);
        Assert.Contains(SqlCompletionPipelineStage.Final, stages);
    }

    private sealed class RecordingProgress(Action<SqlCompletionStageSnapshot> onReport) : IProgress<SqlCompletionStageSnapshot>
    {
        private readonly Action<SqlCompletionStageSnapshot> _onReport = onReport;

        public void Report(SqlCompletionStageSnapshot value) => _onReport(value);
    }
}

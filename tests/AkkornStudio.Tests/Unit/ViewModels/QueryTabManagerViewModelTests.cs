using AkkornStudio.UI.Services.Explain;
using AkkornStudio.UI.Services.Benchmark;

using AkkornStudio.UI.ViewModels.Canvas;

namespace AkkornStudio.Tests.Unit.ViewModels;

public class QueryTabManagerViewModelTests
{
    [Fact]
    public void Initialize_StoresExplainHistoryInFirstTab()
    {
        var sut = new QueryTabManagerViewModel();
        var history = new List<ExplainHistoryState>
        {
            new(DateTimeOffset.UtcNow, "Seq Scan", 98, 1),
        };

        sut.Initialize("{}", "file.vsa", true, history);

        QueryTabState tab = Assert.Single(sut.Tabs);
        Assert.Single(tab.ExplainHistory);
        Assert.Equal("Seq Scan", tab.ExplainHistory[0].TopOperation);
    }

    [Fact]
    public void CaptureActive_UpdatesExplainHistory()
    {
        var sut = new QueryTabManagerViewModel();
        sut.Initialize("{}", null, false, null);

        var history = new List<ExplainHistoryState>
        {
            new(DateTimeOffset.UtcNow, "Hash Join", 203, 2),
        };

        sut.CaptureActive("{a}", "q.vsa", true, history);

        QueryTabState tab = Assert.Single(sut.Tabs);
        Assert.Equal("{a}", tab.SnapshotJson);
        Assert.Single(tab.ExplainHistory);
        Assert.Equal("Hash Join", tab.ExplainHistory[0].TopOperation);
    }

    [Fact]
    public void ActiveMutations_WhenNoTabsConfigured_AreNoOp()
    {
        var sut = new QueryTabManagerViewModel();

        sut.CaptureActive("{x}", "file.vsa", isDirty: true, explainHistory: null);
        sut.SyncActiveMetadata("file2.vsa", isDirty: false);
        sut.ResetActive("{y}");

        Assert.Empty(sut.Tabs);
        Assert.Equal(0, sut.ActiveTabIndex);
    }
}


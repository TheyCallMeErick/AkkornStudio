using AkkornStudio.Core;
using AkkornStudio.UI.Services.ConnectionManager;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.ViewModels.Shell;

public sealed class ShellReturnToStartTests
{
    [Fact]
    public async Task ReturnToStart_ClosesActiveModalsAndConnectionOverlay()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();
        shell.OutputPreview.IsVisible = true;
        await shell.QuickDataPreview.OpenSqlPreviewAsync(
            title: "Preview",
            subtitle: string.Empty,
            sql: "SELECT 1",
            connection: null,
            provider: DatabaseProvider.Postgres,
            metadata: null,
            focusTableFullName: null,
            sourceDocumentType: null);

        CanvasViewModel queryCanvas = shell.EnsureCanvas();
        queryCanvas.ConnectionManager.IsVisible = true;
        shell.AttachConnectionModalToActiveDocument();

        Assert.True(shell.OutputPreview.IsVisible);
        Assert.True(shell.QuickDataPreview.IsVisible);
        Assert.True(shell.IsConnectionManagerOverlayVisible);

        shell.ReturnToStart();

        Assert.True(shell.IsStartVisible);
        Assert.False(shell.OutputPreview.IsVisible);
        Assert.False(shell.QuickDataPreview.IsVisible);
        Assert.False(queryCanvas.ConnectionManager.IsVisible);
        Assert.False(shell.IsConnectionManagerOverlayVisible);
    }
}

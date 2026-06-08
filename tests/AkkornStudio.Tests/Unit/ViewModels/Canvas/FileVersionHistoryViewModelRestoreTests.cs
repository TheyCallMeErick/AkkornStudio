using Avalonia;
using AkkornStudio.Nodes;
using AkkornStudio.UI.Serialization;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.Canvas;

namespace AkkornStudio.Tests.Unit.ViewModels.Canvas;

public sealed class FileVersionHistoryViewModelRestoreTests
{
    [Fact]
    public async Task RestoreSelectedAsync_RestoresChosenLocalVersionToCanvas()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"akkorn-file-history-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        string path = Path.Combine(tempRoot, "workspace.vsaq");

        try
        {
            CanvasViewModel source = new();
            source.Nodes.Clear();
            source.Connections.Clear();
            source.UndoRedo.Clear();
            source.SpawnNode(NodeDefinitionRegistry.Get(NodeType.Equals), new Point(20, 20));
            await CanvasSerializer.SaveToFileAsync(path, source);
            byte[] baselinePayload = await File.ReadAllBytesAsync(path);

            source.Nodes.Clear();
            source.Connections.Clear();
            source.UndoRedo.Clear();
            source.SpawnNode(NodeDefinitionRegistry.Get(NodeType.ResultOutput), new Point(40, 40));
            await CanvasSerializer.SaveToFileAsync(path, source);

            string historyDir = Path.Combine(tempRoot, ".vsaq_history", "workspace");
            Directory.CreateDirectory(historyDir);
            string baselineVersionPath = Path.Combine(historyDir, "2000010100000000000_workspace.vsaq");
            await File.WriteAllBytesAsync(baselineVersionPath, baselinePayload);

            CanvasViewModel workingCanvas = new();
            CanvasLoadResult load = await CanvasSerializer.LoadFromFileAsync(path, workingCanvas);
            Assert.True(load.Success);
            workingCanvas.CurrentFilePath = path;
            Assert.Contains(workingCanvas.Nodes, node => node.Type == NodeType.ResultOutput);

            var history = new FileVersionHistoryViewModel(workingCanvas);
            await history.ReloadAsync();
            LocalFileVersionInfo baselineVersion = Assert.Single(history.Versions.Where(version =>
                string.Equals(version.VersionPath, baselineVersionPath, StringComparison.Ordinal)));
            history.SelectedVersion = baselineVersion;
            await history.RestoreSelectedAsync();

            Assert.Contains(workingCanvas.Nodes, node => node.Type == NodeType.Equals);
            Assert.DoesNotContain(workingCanvas.Nodes, node => node.Type == NodeType.ResultOutput);
            Assert.False(string.IsNullOrWhiteSpace(history.StatusMessage));
            Assert.DoesNotContain("failed", history.StatusMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }
}

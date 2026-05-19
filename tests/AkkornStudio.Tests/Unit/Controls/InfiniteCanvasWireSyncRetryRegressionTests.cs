using System.IO;
using Xunit;

namespace AkkornStudio.Tests.Unit.Controls;

public class InfiniteCanvasWireSyncRetryRegressionTests
{
    [Fact]
    public void SyncWires_DefersAndRetries_WhenNoPinsWereUpdatedYet()
    {
        string source = ReadInfiniteCanvasSource();

        Assert.Contains("updatedPins == 0", source);
        Assert.Contains("hasUnpositionedConnectionPin", source);
        Assert.DoesNotContain("_wireSyncRetryCount < MaxWireSyncRetries", source);
        Assert.Contains("keep retrying across subsequent frames", source);
        Assert.Contains("SyncWires deferred", source);
        Assert.Contains("DispatcherPriority.Background", source);
    }

    [Fact]
    public void UpdatePinPositions_ReturnsCount_ForDeferredSyncDecision()
    {
        string source = ReadInfiniteCanvasSource();

        Assert.Contains("private int UpdatePinPositions()", source);
        Assert.Contains("return updatedPins.Count;", source);
    }

    [Fact]
    public void UpdatePinPositions_ResetsAbsolutePositions_WithoutManualNodeArrange()
    {
        string source = ReadInfiniteCanvasSource();

        Assert.Contains("pin.ResetAbsolutePosition();", source);
        Assert.Contains("foreach (NodeViewModel node in ViewModel.Nodes)", source);
        Assert.DoesNotContain("nc.Arrange(new Rect(new Point(node.Position.X, node.Position.Y), nc.DesiredSize));", source);
    }

    private static string ReadInfiniteCanvasSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(
                dir.FullName,
                "src",
                "AkkornStudio.UI",
                "Controls",
                "InfiniteCanvas",
                "InfiniteCanvas.cs"
            );

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate InfiniteCanvas.cs from test base directory.");
    }
}

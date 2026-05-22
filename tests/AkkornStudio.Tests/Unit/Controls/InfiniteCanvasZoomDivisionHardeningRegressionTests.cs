using System.IO;

namespace AkkornStudio.Tests.Unit.Controls;

public sealed class InfiniteCanvasZoomDivisionHardeningRegressionTests
{
    [Fact]
    public void NodeDrag_UsesNormalizedZoomBeforeDividingDelta()
    {
        string source = ReadInfiniteCanvasNodeDragSource();

        Assert.Contains("CanvasViewportController.NormalizeZoom(_zoom)", source);
    }

    private static string ReadInfiniteCanvasNodeDragSource()
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
                "InfiniteCanvas.NodeDrag.cs"
            );

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate InfiniteCanvas.NodeDrag.cs from test base directory.");
    }
}

using System.IO;

namespace AkkornStudio.Tests.Unit.Controls;

public sealed class NodeControlPinHitToleranceRegressionTests
{
    [Fact]
    public void HitTestPin_UsesZoomAwareToleranceComputation()
    {
        string source = ReadNodeControlSource();

        Assert.Contains("ComputePinHitTestTolerance()", source);
        Assert.Contains("CanvasViewportController.NormalizeZoom(canvasVm.Zoom)", source);
        Assert.Contains("baseTolerance * Math.Abs(normalizedZoom)", source);
    }

    private static string ReadNodeControlSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(
                dir.FullName,
                "src",
                "AkkornStudio.UI",
                "Controls",
                "Node",
                "NodeControl.axaml.cs"
            );

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate NodeControl.axaml.cs from test base directory.");
    }
}

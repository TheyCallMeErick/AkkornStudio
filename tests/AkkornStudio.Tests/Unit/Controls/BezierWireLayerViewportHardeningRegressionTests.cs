using System.IO;

namespace AkkornStudio.Tests.Unit.Controls;

public sealed class BezierWireLayerViewportHardeningRegressionTests
{
    [Fact]
    public void Render_UsesFiniteViewportGuardBeforeBoundsBasedCulling()
    {
        string source = ReadWireLayerSource();

        Assert.Contains("private static bool HasValidViewportDimensions(double width, double height)", source);
        Assert.Contains("double.IsFinite(width) && double.IsFinite(height) && width > 1 && height > 1", source);
        Assert.Contains("Rect viewport = HasValidViewportDimensions(Bounds.Width, Bounds.Height)", source);
    }

    private static string ReadWireLayerSource()
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
                "BezierWireLayer.cs"
            );

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate BezierWireLayer.cs from test base directory.");
    }
}

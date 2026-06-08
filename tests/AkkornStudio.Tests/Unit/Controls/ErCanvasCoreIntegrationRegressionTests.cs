using System.IO;
using Xunit;

namespace AkkornStudio.Tests.Unit.Controls;

public sealed class ErCanvasCoreIntegrationRegressionTests
{
    [Fact]
    public void ErCanvasControl_Uses_SharedInfiniteCanvasCoreHost()
    {
        string xaml = ReadRepoFile("src", "AkkornStudio.UI", "Controls", "ErDiagram", "ErCanvasControl.axaml");

        Assert.Contains("<ctrl:InfiniteCanvasCoreControl", xaml);
        Assert.Contains("<ctrl:InfiniteCanvasCoreControl.SceneContent>", xaml);
        Assert.Contains("<ctrl:InfiniteCanvasCoreControl.OverlayContent>", xaml);
        Assert.Contains("Text=\"Centralizar selecao\"", xaml);
        Assert.Contains("Text=\"Enquadrar selecao\"", xaml);
        Assert.DoesNotContain("<ctrl:CanvasViewportSurface", xaml);
    }

    private static string ReadRepoFile(params string[] relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var segments = new string[relativePath.Length + 1];
            segments[0] = dir.FullName;
            for (int i = 0; i < relativePath.Length; i++)
                segments[i + 1] = relativePath[i];

            string candidate = Path.Combine(segments);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(relativePath)} from test base directory.");
    }
}

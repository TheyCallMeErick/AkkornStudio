using AkkornStudio.UI.Services.Export;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.Services.Export;

public sealed class FlowDocumentExporterTests
{
    [Fact]
    public async Task WriteAsync_WithValidPath_WritesFileAndReturnsAbsolutePath()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"akkorn-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string outputPath = Path.Combine(tempDir, "flow.md");
        var canvas = new CanvasViewModel();

        try
        {
            string? writtenPath = await FlowDocumentExporter.WriteAsync(canvas, outputPath);

            Assert.NotNull(writtenPath);
            Assert.True(Path.IsPathFullyQualified(writtenPath!));
            Assert.True(File.Exists(writtenPath));
            Assert.True(new FileInfo(writtenPath).Length > 0);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task WriteAsync_WhenTargetPathIsDirectory_ReturnsNullAndDoesNotLeaveTempFile()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"akkorn-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string directoryAsTarget = Path.Combine(tempDir, "flow.md");
        Directory.CreateDirectory(directoryAsTarget);
        var canvas = new CanvasViewModel();

        try
        {
            string? writtenPath = await FlowDocumentExporter.WriteAsync(canvas, directoryAsTarget);

            Assert.Null(writtenPath);
            string[] leakedTempFiles = Directory.GetFiles(
                tempDir,
                "flow.md.tmp-*",
                SearchOption.TopDirectoryOnly);
            Assert.Empty(leakedTempFiles);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}

using System.IO;

namespace AkkornStudio.Tests.Unit.ViewModels;

public sealed class CanvasSubEditorParentRevalidationHardeningTests
{
    [Fact]
    public void ExitViewEditor_NotifiesParentParameterChanges_ForRevalidation()
    {
        string source = ReadSubCanvasEditingControllerSource();

        Assert.Contains("_canvas.NotifyNodeParameterChanged(viewNode, CanvasSerializer.ViewSubgraphParameterKey);", source);
        Assert.Contains("_canvas.NotifyNodeParameterChanged(viewNode, CanvasSerializer.ViewFromTableParameterKey);", source);
        Assert.Contains("_canvas.NotifyNodeParameterChanged(viewNode, \"SelectSql\");", source);
    }

    [Fact]
    public void NotifyNodeParameterChanged_SchedulesValidation()
    {
        string source = ReadCanvasViewModelSource();

        Assert.Contains("_validationManager?.ScheduleValidation();", source);
    }

    private static string ReadSubCanvasEditingControllerSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(
                dir.FullName,
                "src",
                "AkkornStudio.UI",
                "ViewModels",
                "Canvas",
                "SubCanvasEditingController.cs");

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate SubCanvasEditingController.cs from test base directory.");
    }

    private static string ReadCanvasViewModelSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(
                dir.FullName,
                "src",
                "AkkornStudio.UI",
                "ViewModels",
                "CanvasViewModel.cs");

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate CanvasViewModel.cs from test base directory.");
    }
}

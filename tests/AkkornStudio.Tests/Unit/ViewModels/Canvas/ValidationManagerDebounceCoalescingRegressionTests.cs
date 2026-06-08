using System.IO;

namespace AkkornStudio.Tests.Unit.ViewModels.Canvas;

public sealed class ValidationManagerDebounceCoalescingRegressionTests
{
    [Fact]
    public void ScheduleValidation_TracksRequestedVersionForCoalescing()
    {
        string source = ReadValidationManagerSource();

        Assert.Contains("private int _requestedValidationVersion;", source);
        Assert.Contains("_requestedValidationVersion++;", source);
    }

    [Fact]
    public void RunValidationSafely_RequeuesWhenNewRequestsArriveDuringExecution()
    {
        string source = ReadValidationManagerSource();

        Assert.Contains("private int _completedValidationVersion;", source);
        Assert.Contains("private bool _validationInProgress;", source);
        Assert.Contains("shouldRerun = _requestedValidationVersion > _completedValidationVersion;", source);
        Assert.Contains("Avalonia.Threading.Dispatcher.UIThread.Post(RunValidationSafely);", source);
    }

    private static string ReadValidationManagerSource()
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
                "Managers",
                "ValidationManager.cs"
            );

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate ValidationManager.cs from test base directory.");
    }
}

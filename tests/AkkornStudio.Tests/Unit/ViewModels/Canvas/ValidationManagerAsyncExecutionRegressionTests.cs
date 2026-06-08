using System.IO;

namespace AkkornStudio.Tests.Unit.ViewModels.Canvas;

public sealed class ValidationManagerAsyncExecutionRegressionTests
{
    [Fact]
    public void RunValidationSafely_UsesBackgroundComputationViaTaskRun()
    {
        string source = ReadValidationManagerSource();

        Assert.Contains("private async void RunValidationSafely()", source);
        Assert.Contains("await Task.Run(ComputeValidation)", source);
    }

    [Fact]
    public void ValidationFlow_SplitsComputeAndApplyPhases()
    {
        string source = ReadValidationManagerSource();

        Assert.Contains("private ValidationComputation ComputeValidation()", source);
        Assert.Contains("private void ApplyValidation(ValidationComputation computation)", source);
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

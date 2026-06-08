using AkkornStudio.UI.Services;
using AkkornStudio.UI.ViewModels;
using Xunit;

namespace AkkornStudio.Tests.Unit.Services;

/// <summary>
/// Contract-level tests for PreviewService disposal and safety checks.
/// Kept headless-safe: no Avalonia window instantiation required.
/// </summary>
public class PreviewServiceDisposalTests
{
    [Fact]
    public void PreviewService_ImplementsIDisposable()
    {
        Assert.Contains(typeof(IDisposable), typeof(PreviewService).GetInterfaces());
    }

    [Fact]
    public void PreviewService_ExposesExpectedPublicMethods()
    {
        Assert.NotNull(typeof(PreviewService).GetMethod(nameof(PreviewService.Wire)));
        Assert.NotNull(typeof(PreviewService).GetMethod(nameof(PreviewService.RunPreviewAsync)));
        Assert.NotNull(typeof(PreviewService).GetMethod(nameof(PreviewService.NotifyConnectionContextChanged)));
        Assert.NotNull(typeof(PreviewService).GetMethod(nameof(PreviewService.Dispose)));
    }

    [Fact]
    public void PreviewService_HasExpectedConstructorDependencies()
    {
        var ctor = typeof(PreviewService).GetConstructors().Single();
        var parameters = ctor.GetParameters();

        Assert.Equal(3, parameters.Length);
        Assert.Equal(typeof(Avalonia.Controls.Window), parameters[0].ParameterType);
        Assert.Equal(typeof(CanvasViewModel), parameters[1].ParameterType);
    }

    [Fact]
    public void PreviewService_RunPreviewAsync_ReturnsTask()
    {
        var method = typeof(PreviewService).GetMethod(nameof(PreviewService.RunPreviewAsync));
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);
    }
}

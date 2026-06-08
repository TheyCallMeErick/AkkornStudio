using System.Reflection;
using AkkornStudio.UI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AkkornStudio.Tests.Unit.App;

public class AppStartupLoggingTests
{
    [Fact]
    public void BuildServices_RegistersStartupLoggerFactoryInsteadOfNullLoggerFactory()
    {
        MethodInfo buildServices = typeof(global::AkkornStudio.UI.App).GetMethod(
            "BuildServices",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        var provider = (IServiceProvider)buildServices.Invoke(null, null)!;
        Assert.IsAssignableFrom<IDisposable>(provider);
        using IDisposable _ = (IDisposable)provider;

        ILoggerFactory loggerFactory = (ILoggerFactory)provider.GetService(typeof(ILoggerFactory))!;
        Assert.NotNull(loggerFactory);
        Assert.IsNotType<NullLoggerFactory>(loggerFactory);
    }

    [Fact]
    public void TryValidateCriticalStartupServices_WhenRequiredServicesMissing_ReturnsFalseWithDiagnostic()
    {
        MethodInfo validate = typeof(global::AkkornStudio.UI.App).GetMethod(
            "TryValidateCriticalStartupServices",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        using IDisposable _ = provider;

        object?[] args = [provider, null];
        bool isValid = (bool)validate.Invoke(null, args)!;
        string diagnostic = Assert.IsType<string>(args[1]);

        Assert.False(isValid);
        Assert.Contains("ICriticalFlowTelemetryService", diagnostic, StringComparison.Ordinal);
        Assert.Contains("MainWindow", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void TryValidateCriticalStartupServices_WithBuildServicesProvider_ReturnsTrue()
    {
        MethodInfo buildServices = typeof(global::AkkornStudio.UI.App).GetMethod(
            "BuildServices",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo validate = typeof(global::AkkornStudio.UI.App).GetMethod(
            "TryValidateCriticalStartupServices",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        var provider = (IServiceProvider)buildServices.Invoke(null, null)!;
        Assert.IsAssignableFrom<IDisposable>(provider);
        using IDisposable _ = (IDisposable)provider;

        object?[] args = [provider, null];
        bool isValid = (bool)validate.Invoke(null, args)!;
        string diagnostic = Assert.IsType<string>(args[1]);

        Assert.True(isValid);
        Assert.True(string.IsNullOrWhiteSpace(diagnostic));
    }
}

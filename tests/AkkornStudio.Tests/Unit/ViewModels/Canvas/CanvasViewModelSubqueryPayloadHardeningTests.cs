using System.Reflection;
using Avalonia;
using AkkornStudio.Nodes;
using AkkornStudio.UI.Serialization;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.ViewModels.Canvas;

public sealed class CanvasViewModelSubqueryPayloadHardeningTests
{
    [Fact]
    public void HandleSubqueryInputDisconnectWarnings_WhenPayloadIsCorrupted_EmitsWarningFeedback()
    {
        var canvas = new CanvasViewModel();
        NodeViewModel subquery = new(NodeDefinitionRegistry.Get(NodeType.Subquery), new Point(0, 0));
        subquery.Parameters[CanvasSerializer.SubquerySubgraphParameterKey] = "{ not-valid-json ";

        PinViewModel? inputPin = subquery.InputPins.FirstOrDefault(pin =>
            pin.Name.StartsWith("input_", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(inputPin);

        var removedConnection = new ConnectionViewModel(
            subquery.OutputPins.First(),
            new Point(0, 0),
            new Point(10, 10))
        {
            ToPin = inputPin,
        };

        int diagnosticsBefore = canvas.Diagnostics.SnapshotEntries().Count;

        InvokeHandleSubqueryInputDisconnectWarnings(canvas, [removedConnection]);

        Assert.True(canvas.Toasts.IsVisible);
        Assert.Equal(ToastSeverity.Warning, canvas.Toasts.Severity);
        Assert.Contains("Node:", canvas.Toasts.Details ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        IReadOnlyList<AppDiagnosticEntry> entries = canvas.Diagnostics.SnapshotEntries();
        Assert.True(entries.Count > diagnosticsBefore);
        Assert.Contains(
            entries,
            entry =>
                entry.Status == DiagnosticStatus.Warning
                && entry.Name.Contains("Subquery", StringComparison.OrdinalIgnoreCase)
                && entry.Details.Contains(subquery.Title, StringComparison.OrdinalIgnoreCase));
    }

    private static void InvokeHandleSubqueryInputDisconnectWarnings(
        CanvasViewModel canvas,
        IEnumerable<ConnectionViewModel> removedConnections)
    {
        MethodInfo? method = typeof(CanvasViewModel).GetMethod(
            "HandleSubqueryInputDisconnectWarnings",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        method!.Invoke(canvas, [removedConnections]);
    }
}

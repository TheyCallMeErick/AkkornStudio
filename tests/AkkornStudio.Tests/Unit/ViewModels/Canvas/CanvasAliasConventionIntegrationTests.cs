using AkkornStudio.UI.Services.Canvas.AutoJoin;
using AkkornStudio.UI.Services.Explain;
using Avalonia;
using AkkornStudio.Nodes;
using AkkornStudio.UI.Services.Settings;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.ViewModels.Canvas;

public class CanvasAliasConventionIntegrationTests
{
    [Fact]
    public void AutoFixNaming_UsesConventionConfiguredInPropertyPanel()
    {
        var vm = new CanvasViewModel();
        vm.Nodes.Clear();
        vm.Connections.Clear();
        vm.UndoRedo.Clear();

        var node = new NodeViewModel(NodeDefinitionRegistry.Get(NodeType.Alias), new Point(0, 0))
        {
            Alias = "order_total",
        };
        vm.Nodes.Add(node);

        vm.ApplyProjectConventionSettings(new ProjectConventionSettings
        {
            NamingConvention = "camelCase",
            EnforceAliasNaming = true,
            WarnOnReservedKeywords = true,
            MaxAliasLength = 64,
            DefaultWireCurveMode = "Bezier",
        });
        vm.AutoFixNaming();

        Assert.Equal("orderTotal", node.Alias);
    }

    [Fact]
    public void GraphValidator_WithCamelCasePolicy_DoesNotReportSnakeCaseViolation()
    {
        var vm = new CanvasViewModel();
        vm.Nodes.Clear();
        vm.Connections.Clear();

        var node = new NodeViewModel(NodeDefinitionRegistry.Get(NodeType.Alias), new Point(0, 0))
        {
            Alias = "orderTotal",
        };
        vm.Nodes.Add(node);

        vm.ApplyProjectConventionSettings(new ProjectConventionSettings
        {
            NamingConvention = "camelCase",
            EnforceAliasNaming = true,
            WarnOnReservedKeywords = true,
            MaxAliasLength = 64,
            DefaultWireCurveMode = "Bezier",
        });

        var issues = GraphValidator.Validate(
            vm,
            vm.PropertyPanel.BuildNamingConventionPolicy(),
            vm.AliasConventions);

        Assert.DoesNotContain(issues, i => i.Code.StartsWith("NAMING_", StringComparison.Ordinal));
    }

    [Fact]
    public void GraphValidator_AfterSidebarConventionSwap_ReflectsNewConvention()
    {
        var vm = new CanvasViewModel();
        vm.Nodes.Clear();
        vm.Connections.Clear();

        var node = new NodeViewModel(NodeDefinitionRegistry.Get(NodeType.Alias), new Point(0, 0))
        {
            Alias = "order_total",
        };
        vm.Nodes.Add(node);

        vm.ApplyProjectConventionSettings(new ProjectConventionSettings
        {
            NamingConvention = "snake_case",
            EnforceAliasNaming = true,
            WarnOnReservedKeywords = true,
            MaxAliasLength = 64,
            DefaultWireCurveMode = "Bezier",
        });
        var snakeIssues = GraphValidator.Validate(
            vm,
            vm.PropertyPanel.BuildNamingConventionPolicy(),
            vm.AliasConventions);
        Assert.DoesNotContain(snakeIssues, i => i.Code.StartsWith("NAMING_", StringComparison.Ordinal));

        vm.ApplyProjectConventionSettings(new ProjectConventionSettings
        {
            NamingConvention = "camelCase",
            EnforceAliasNaming = true,
            WarnOnReservedKeywords = true,
            MaxAliasLength = 64,
            DefaultWireCurveMode = "Bezier",
        });
        var camelIssues = GraphValidator.Validate(
            vm,
            vm.PropertyPanel.BuildNamingConventionPolicy(),
            vm.AliasConventions);
        Assert.Contains(camelIssues, i => i.Code == "NAMING_CAMEL_CASE");
    }

}



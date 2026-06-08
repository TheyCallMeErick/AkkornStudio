using AkkornStudio.UI.Services.Input.ShortcutRegistry;

namespace AkkornStudio.Tests.Unit.Services;

public sealed class ShortcutRegressionTests
{
    [Fact]
    public void DefaultCatalog_ContainsCriticalActions()
    {
        var registry = new global::AkkornStudio.UI.Services.Input.ShortcutRegistry.ShortcutRegistry(
            customizationStore: new NoOpShortcutCustomizationStore());

        string[] criticalActions =
        [
            ShortcutActionIds.OpenShortcutsReference,
            ShortcutActionIds.OpenCommandPalette,
            ShortcutActionIds.Save,
            ShortcutActionIds.OpenFile,
            ShortcutActionIds.NewCanvas,
            ShortcutActionIds.Undo,
            ShortcutActionIds.Redo,
            ShortcutActionIds.TogglePreview,
            ShortcutActionIds.ExplainPlan,
        ];

        foreach (string actionId in criticalActions)
            Assert.NotNull(registry.FindByActionId(actionId));
    }

    [Fact]
    public void DefaultCatalog_GlobalEffectiveGestures_AreUnique()
    {
        var registry = new global::AkkornStudio.UI.Services.Input.ShortcutRegistry.ShortcutRegistry(
            customizationStore: new NoOpShortcutCustomizationStore());

        IReadOnlyList<ShortcutDefinition> duplicates = registry.GetAll()
            .Where(definition => definition.Context == ShortcutContext.Global)
            .Where(definition => definition.EffectiveGesture is not null)
            .GroupBy(definition => definition.EffectiveGesture!.NormalizedText, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group)
            .ToList();

        Assert.Empty(duplicates);
    }
}

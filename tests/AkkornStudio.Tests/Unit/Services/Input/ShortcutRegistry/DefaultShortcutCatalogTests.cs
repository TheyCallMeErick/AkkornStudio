using AkkornStudio.UI.Services.Input.ShortcutRegistry;

namespace AkkornStudio.Tests.Unit.Services.Input.ShortcutRegistry;

public sealed class DefaultShortcutCatalogTests
{
    [Fact]
    public void Build_ContainsCriticalShortcuts()
    {
        var catalog = new DefaultShortcutCatalog();
        IReadOnlyList<ShortcutDefinition> definitions = catalog.Build();

        Assert.Contains(definitions, d => d.ActionId.Value == ShortcutActionIds.OpenShortcutsReference);
        Assert.Contains(definitions, d => d.ActionId.Value == ShortcutActionIds.NewCanvas);
        Assert.Contains(definitions, d => d.ActionId.Value == ShortcutActionIds.OpenFile);
        Assert.Contains(definitions, d => d.ActionId.Value == ShortcutActionIds.Save);
        Assert.Contains(definitions, d => d.ActionId.Value == ShortcutActionIds.Undo);
        Assert.Contains(definitions, d => d.ActionId.Value == ShortcutActionIds.Redo);
        Assert.Contains(definitions, d => d.ActionId.Value == ShortcutActionIds.TogglePreview);
        Assert.Contains(definitions, d => d.ActionId.Value == ShortcutActionIds.ExplainPlan);
        Assert.Contains(definitions, d => d.ActionId.Value == ShortcutActionIds.SqlRunCurrent);
    }

    [Fact]
    public void Build_ActionIdsAreUnique()
    {
        var catalog = new DefaultShortcutCatalog();
        IReadOnlyList<ShortcutDefinition> definitions = catalog.Build();

        int distinctCount = definitions.Select(d => d.ActionId.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        Assert.Equal(definitions.Count, distinctCount);
    }
}

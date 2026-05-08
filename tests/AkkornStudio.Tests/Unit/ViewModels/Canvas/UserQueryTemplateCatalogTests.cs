using AkkornStudio.Nodes;
using AkkornStudio.UI.ViewModels;
using Avalonia;
using Xunit;

namespace AkkornStudio.Tests.Unit.ViewModels.Canvas;

public sealed class UserQueryTemplateCatalogTests
{
    [Fact]
    public void SaveFromCanvas_PersistsTemplate_AndLoadAllReturnsUserTemplate()
    {
        var store = new InMemoryUserQueryTemplateStore();
        var canvas = new CanvasViewModel();
        canvas.Nodes.Add(new NodeViewModel("public.orders", [("id", PinDataType.Integer)], new Point(10, 20)));

        UserQueryTemplate saved = QueryTemplateCatalog.SaveFromCanvas(
            canvas,
            "Orders starter",
            store,
            description: "Saved orders graph");

        IReadOnlyList<QueryTemplate> templates = QueryTemplateCatalog.LoadAll(store);

        QueryTemplate userTemplate = Assert.Single(templates.Where(t => t.Id == saved.Id));
        Assert.True(userTemplate.IsUserCreated);
        Assert.Equal("Orders starter", userTemplate.Name);
        Assert.Equal(QueryTemplateCatalog.UserTemplateCategory, userTemplate.Category);
    }

    [Fact]
    public void ToQueryTemplate_BuildRestoresSavedCanvasGraph()
    {
        var source = new CanvasViewModel();
        source.Nodes.Add(new NodeViewModel("public.customers", [("id", PinDataType.Integer)], new Point(100, 120)));

        UserQueryTemplate saved = QueryTemplateCatalog.CreateFromCanvas(source, "Customers starter");
        QueryTemplate template = QueryTemplateCatalog.ToQueryTemplate(saved);

        var target = new CanvasViewModel();
        target.LoadTemplate(template);

        NodeViewModel restored = Assert.Single(target.Nodes);
        Assert.Equal("customers", restored.Title);
        Assert.Equal("public.customers", restored.Subtitle);
        Assert.False(target.IsDirty);
    }

    private sealed class InMemoryUserQueryTemplateStore : IUserQueryTemplateStore
    {
        private readonly List<UserQueryTemplate> _templates = [];

        public IReadOnlyList<UserQueryTemplate> Load() => [.. _templates];

        public void Save(UserQueryTemplate template)
        {
            int index = _templates.FindIndex(t => t.Id == template.Id);
            if (index >= 0)
                _templates[index] = template;
            else
                _templates.Insert(0, template);
        }

        public bool Delete(string id) =>
            _templates.RemoveAll(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
    }
}

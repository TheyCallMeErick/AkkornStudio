using AkkornStudio.UI.Services.Workspace.Models;
using AkkornStudio.UI.Serialization;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.ErDiagram;
using AkkornStudio.Core;
using AkkornStudio.Nodes;
using AkkornStudio.Metadata;

namespace AkkornStudio.Tests.Unit.ViewModels.Shell;

public class ShellWorkspaceDocumentRoutingTests
{
    private const string AutoProjectionMarkerParameter = "__akkorn_auto_projection";

    [Fact]
    public void EnterCanvas_RegistersAndActivatesQueryDocument()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());

        shell.EnterCanvas();

        Assert.Single(shell.OpenWorkspaceDocuments);
        OpenWorkspaceDocument active = Assert.IsType<OpenWorkspaceDocument>(shell.ActiveWorkspaceDocument);
        Assert.Equal(WorkspaceDocumentType.QueryCanvas, active.Descriptor.DocumentType);
        Assert.Equal(active.Descriptor.DocumentId, shell.ActiveWorkspaceDocumentId);
    }

    [Fact]
    public void EnterCanvas_DoesNotCreateDdlOrSqlEditorDocumentsUntilRequested()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());

        shell.EnterCanvas();

        Assert.Single(shell.OpenWorkspaceDocuments);
        Assert.Equal(WorkspaceDocumentType.QueryCanvas, shell.OpenWorkspaceDocuments[0].Descriptor.DocumentType);
    }

    [Fact]
    public void SetActiveDocumentType_SwitchesActiveWorkspaceDocumentByType()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();

        shell.ActivateDocument(WorkspaceDocumentType.DdlCanvas);
        OpenWorkspaceDocument ddlActive = Assert.IsType<OpenWorkspaceDocument>(shell.ActiveWorkspaceDocument);
        Assert.Equal(WorkspaceDocumentType.DdlCanvas, ddlActive.Descriptor.DocumentType);

        shell.ActivateDocument(WorkspaceDocumentType.SqlEditor);
        OpenWorkspaceDocument sqlEditorActive = Assert.IsType<OpenWorkspaceDocument>(shell.ActiveWorkspaceDocument);
        Assert.Equal(WorkspaceDocumentType.SqlEditor, sqlEditorActive.Descriptor.DocumentType);

        shell.ActivateDocument(WorkspaceDocumentType.QueryCanvas);
        OpenWorkspaceDocument queryActive = Assert.IsType<OpenWorkspaceDocument>(shell.ActiveWorkspaceDocument);
        Assert.Equal(WorkspaceDocumentType.QueryCanvas, queryActive.Descriptor.DocumentType);
    }

    [Fact]
    public void SetActiveDocumentType_SqlEditor_CreatesSingleSqlEditorDocumentLazily()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();

        shell.ActivateDocument(WorkspaceDocumentType.SqlEditor);
        shell.ActivateDocument(WorkspaceDocumentType.SqlEditor);

        int sqlEditorDocumentCount = 0;
        foreach (OpenWorkspaceDocument document in shell.OpenWorkspaceDocuments)
        {
            if (document.Descriptor.DocumentType == WorkspaceDocumentType.SqlEditor)
                sqlEditorDocumentCount++;
        }

        Assert.Equal(1, sqlEditorDocumentCount);
        Assert.Equal(2, shell.OpenWorkspaceDocuments.Count);
    }

    [Fact]
    public void SetActiveDocumentType_ErDiagram_CreatesSingleErDiagramDocumentLazily()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();

        shell.ActivateDocument(WorkspaceDocumentType.ErDiagram);
        shell.ActivateDocument(WorkspaceDocumentType.ErDiagram);

        int erDocumentCount = shell.OpenWorkspaceDocuments.Count(document =>
            document.Descriptor.DocumentType == WorkspaceDocumentType.ErDiagram);

        Assert.Equal(1, erDocumentCount);
        Assert.Equal(2, shell.OpenWorkspaceDocuments.Count);
        Assert.True(shell.IsErDiagramDocumentPageActive);
        Assert.NotNull(shell.ActiveErDiagramDocument);
    }

    [Fact]
    public void ErDiagramDocument_OpenBeforeMetadata_RefreshesWhenCanvasMetadataArrives()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();
        shell.ActivateDocument(WorkspaceDocumentType.ErDiagram);
        ErCanvasViewModel erCanvas = Assert.IsType<ErCanvasViewModel>(shell.ActiveErDiagramDocument);
        Assert.Equal(0, erCanvas.EntityCount);

        CanvasViewModel queryCanvas = Assert.IsType<CanvasViewModel>(
            shell.OpenWorkspaceDocuments
                .Single(document => document.Descriptor.DocumentType == WorkspaceDocumentType.QueryCanvas)
                .DocumentViewModel);
        queryCanvas.SetDatabaseContext(CreateMetadata(), null);

        Assert.Equal(2, erCanvas.EntityCount);
        Assert.Equal(1, erCanvas.EdgeCount);
        Assert.DoesNotContain("W-ER-NO-METADATA", erCanvas.TechnicalWarnings);
    }

    [Fact]
    public void ErDiagramSelection_OpenInQuery_CreatesSimpleJoinInQueryCanvas()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();
        CanvasViewModel queryCanvas = Assert.IsType<CanvasViewModel>(shell.ActiveQueryCanvasDocument);
        queryCanvas.SetDatabaseContext(CreateMetadata(), null);

        shell.ActivateDocument(WorkspaceDocumentType.ErDiagram);
        ErCanvasViewModel erCanvas = Assert.IsType<ErCanvasViewModel>(shell.ActiveErDiagramDocument);
        ErRelationEdgeViewModel edge = Assert.Single(erCanvas.Edges);
        erCanvas.SelectedEdge = edge;

        erCanvas.OpenSelectionInQueryCommand.Execute(null);

        Assert.Equal(WorkspaceDocumentType.QueryCanvas, shell.ActiveWorkspaceDocumentType);
        Assert.Equal(2, queryCanvas.Nodes.Count(node => node.IsTableSource));
        NodeViewModel joinNode = Assert.Single(queryCanvas.Nodes.Where(node => node.IsJoin));
        Assert.Equal("INNER", joinNode.Parameters["join_type"]);
        Assert.Equal("public.customers", joinNode.Parameters["right_source"]);
        Assert.Equal("public.orders.customer_id", joinNode.Parameters["left_expr"]);
        Assert.Equal("public.customers.id", joinNode.Parameters["right_expr"]);
    }

    [Fact]
    public void ErDiagramSelection_OpenInQuery_CreatesCompositeConditionGraph()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();
        CanvasViewModel queryCanvas = Assert.IsType<CanvasViewModel>(shell.ActiveQueryCanvasDocument);
        queryCanvas.SetDatabaseContext(CreateCompositeMetadata(), null);

        shell.ActivateDocument(WorkspaceDocumentType.ErDiagram);
        ErCanvasViewModel erCanvas = Assert.IsType<ErCanvasViewModel>(shell.ActiveErDiagramDocument);
        ErRelationEdgeViewModel edge = Assert.Single(erCanvas.Edges);
        erCanvas.SelectedEdge = edge;

        erCanvas.OpenSelectionInQueryCommand.Execute(null);

        Assert.Equal(WorkspaceDocumentType.QueryCanvas, shell.ActiveWorkspaceDocumentType);
        Assert.Single(queryCanvas.Nodes.Where(node => node.IsJoin));
        Assert.Equal(2, queryCanvas.Nodes.Count(node => node.Type == NodeType.Equals));
        Assert.Single(queryCanvas.Nodes.Where(node => node.Type == NodeType.And));
    }

    [Fact]
    public void ErDiagramSelection_OpenInQuery_SuggestsProjectionWhenOutputIsMissing()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();
        CanvasViewModel queryCanvas = Assert.IsType<CanvasViewModel>(shell.ActiveQueryCanvasDocument);
        queryCanvas.SetDatabaseContext(CreateMetadata(), null);

        shell.ActivateDocument(WorkspaceDocumentType.ErDiagram);
        ErCanvasViewModel erCanvas = Assert.IsType<ErCanvasViewModel>(shell.ActiveErDiagramDocument);
        ErRelationEdgeViewModel edge = Assert.Single(erCanvas.Edges);
        erCanvas.SelectedEdge = edge;

        erCanvas.OpenSelectionInQueryCommand.Execute(null);

        NodeViewModel resultOutput = Assert.Single(queryCanvas.Nodes.Where(node => node.Type == NodeType.ResultOutput));
        NodeViewModel columnSetBuilder = Assert.Single(queryCanvas.Nodes.Where(node => node.Type == NodeType.ColumnSetBuilder));
        Assert.Contains(queryCanvas.Connections, connection =>
            ReferenceEquals(connection.FromPin.Owner, columnSetBuilder)
            && string.Equals(connection.FromPin.Name, "result", StringComparison.OrdinalIgnoreCase)
            && ReferenceEquals(connection.ToPin?.Owner, resultOutput)
            && string.Equals(connection.ToPin?.Name, "columns", StringComparison.OrdinalIgnoreCase));

        var projectionPins = queryCanvas.Connections
            .Where(connection =>
                ReferenceEquals(connection.ToPin?.Owner, columnSetBuilder)
                && string.Equals(connection.ToPin?.Name, "columns", StringComparison.OrdinalIgnoreCase))
            .Select(connection => $"{connection.FromPin.Owner.Subtitle}.{connection.FromPin.Name}")
            .ToArray();

        Assert.Contains("public.orders.id", projectionPins);
        Assert.Contains("public.orders.customer_id", projectionPins);
        Assert.Contains("public.customers.id", projectionPins);
        Assert.Contains("public.customers.name", projectionPins);
    }

    [Fact]
    public void ErDiagramSelection_OpenInQuery_DoesNotOverrideExistingProjection()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();
        CanvasViewModel queryCanvas = Assert.IsType<CanvasViewModel>(shell.ActiveQueryCanvasDocument);
        queryCanvas.SetDatabaseContext(CreateMetadata(), null);

        NodeViewModel orders = AssertTable(queryCanvas, "public.orders", new Avalonia.Point(80, 80));
        NodeViewModel existingResultOutput = queryCanvas.SpawnNode(
            NodeDefinitionRegistry.Get(NodeType.ResultOutput),
            new Avalonia.Point(760, 140));
        queryCanvas.ConnectPins(
            orders.OutputPins.Single(pin => pin.Name == "id"),
            existingResultOutput.InputPins.Single(pin => pin.Name == "column"));

        shell.ActivateDocument(WorkspaceDocumentType.ErDiagram);
        ErCanvasViewModel erCanvas = Assert.IsType<ErCanvasViewModel>(shell.ActiveErDiagramDocument);
        ErRelationEdgeViewModel edge = Assert.Single(erCanvas.Edges);
        erCanvas.SelectedEdge = edge;

        erCanvas.OpenSelectionInQueryCommand.Execute(null);

        Assert.Single(queryCanvas.Nodes.Where(node => node.Type == NodeType.ResultOutput));
        Assert.Empty(queryCanvas.Nodes.Where(node => node.Type == NodeType.ColumnSetBuilder));
        Assert.Single(queryCanvas.Connections.Where(connection =>
            ReferenceEquals(connection.ToPin?.Owner, existingResultOutput)
            && string.Equals(connection.ToPin?.Name, "column", StringComparison.OrdinalIgnoreCase)));
        Assert.Empty(queryCanvas.Connections.Where(connection =>
            ReferenceEquals(connection.ToPin?.Owner, existingResultOutput)
            && string.Equals(connection.ToPin?.Name, "columns", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void ErDiagramSelection_OpenInQuery_PrefersDescriptiveColumnsOverTechnicalFields()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();
        CanvasViewModel queryCanvas = Assert.IsType<CanvasViewModel>(shell.ActiveQueryCanvasDocument);
        queryCanvas.SetDatabaseContext(CreateTechnicalProjectionMetadata(), null);

        shell.ActivateDocument(WorkspaceDocumentType.ErDiagram);
        ErCanvasViewModel erCanvas = Assert.IsType<ErCanvasViewModel>(shell.ActiveErDiagramDocument);
        ErRelationEdgeViewModel edge = Assert.Single(erCanvas.Edges);
        erCanvas.SelectedEdge = edge;

        erCanvas.OpenSelectionInQueryCommand.Execute(null);

        NodeViewModel columnSetBuilder = Assert.Single(queryCanvas.Nodes.Where(node => node.Type == NodeType.ColumnSetBuilder));
        var projectionPins = queryCanvas.Connections
            .Where(connection =>
                ReferenceEquals(connection.ToPin?.Owner, columnSetBuilder)
                && string.Equals(connection.ToPin?.Name, "columns", StringComparison.OrdinalIgnoreCase))
            .Select(connection => $"{connection.FromPin.Owner.Subtitle}.{connection.FromPin.Name}")
            .ToArray();

        Assert.Contains("public.customers.display_name", projectionPins);
        Assert.DoesNotContain("public.customers.created_at", projectionPins);
        Assert.DoesNotContain("public.customers.updated_at", projectionPins);
    }

    [Fact]
    public void AutoProjectionResultOutput_RefineSuggestion_AddsMissingDescriptiveColumns()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();
        CanvasViewModel queryCanvas = Assert.IsType<CanvasViewModel>(shell.ActiveQueryCanvasDocument);
        queryCanvas.SetDatabaseContext(CreateMetadata(), null);

        shell.ActivateDocument(WorkspaceDocumentType.ErDiagram);
        ErCanvasViewModel erCanvas = Assert.IsType<ErCanvasViewModel>(shell.ActiveErDiagramDocument);
        ErRelationEdgeViewModel edge = Assert.Single(erCanvas.Edges);
        erCanvas.SelectedEdge = edge;
        erCanvas.OpenSelectionInQueryCommand.Execute(null);

        NodeViewModel resultOutput = Assert.Single(queryCanvas.Nodes.Where(node => node.Type == NodeType.ResultOutput));
        Assert.Equal("true", resultOutput.Parameters[AutoProjectionMarkerParameter]);

        queryCanvas.PropertyPanel.ShowNode(resultOutput);
        queryCanvas.PropertyPanel.RefineAutoProjectionCommand.Execute(null);

        NodeViewModel columnSetBuilder = Assert.Single(queryCanvas.Nodes.Where(node => node.Type == NodeType.ColumnSetBuilder));
        var projectionPins = queryCanvas.Connections
            .Where(connection =>
                ReferenceEquals(connection.ToPin?.Owner, columnSetBuilder)
                && string.Equals(connection.ToPin?.Name, "columns", StringComparison.OrdinalIgnoreCase))
            .Select(connection => $"{connection.FromPin.Owner.Subtitle}.{connection.FromPin.Name}")
            .ToArray();

        Assert.Contains("public.orders.order_number", projectionPins);
        Assert.Contains("public.customers.name", projectionPins);
    }

    [Fact]
    public void TryRefineSelectedQueryAutoProjection_WithSelectedAutoResultOutput_ReturnsTrue()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();
        CanvasViewModel queryCanvas = Assert.IsType<CanvasViewModel>(shell.ActiveQueryCanvasDocument);
        queryCanvas.SetDatabaseContext(CreateMetadata(), null);

        shell.ActivateDocument(WorkspaceDocumentType.ErDiagram);
        ErCanvasViewModel erCanvas = Assert.IsType<ErCanvasViewModel>(shell.ActiveErDiagramDocument);
        ErRelationEdgeViewModel edge = Assert.Single(erCanvas.Edges);
        erCanvas.SelectedEdge = edge;
        erCanvas.OpenSelectionInQueryCommand.Execute(null);

        NodeViewModel resultOutput = Assert.Single(queryCanvas.Nodes.Where(node => node.Type == NodeType.ResultOutput));
        queryCanvas.DeselectAll();
        queryCanvas.SelectNode(resultOutput);

        bool refined = shell.TryRefineSelectedQueryAutoProjection();

        Assert.True(refined);
        NodeViewModel columnSetBuilder = Assert.Single(queryCanvas.Nodes.Where(node => node.Type == NodeType.ColumnSetBuilder));
        Assert.Contains(queryCanvas.Connections, connection =>
            ReferenceEquals(connection.ToPin?.Owner, columnSetBuilder)
            && string.Equals(connection.ToPin?.Name, "columns", StringComparison.OrdinalIgnoreCase)
            && string.Equals(connection.FromPin.Owner.Subtitle, "public.orders", StringComparison.OrdinalIgnoreCase)
            && string.Equals(connection.FromPin.Name, "order_number", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TryResetSelectedQueryAutoProjection_AfterRefine_RestoresBaseProjection()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();
        CanvasViewModel queryCanvas = Assert.IsType<CanvasViewModel>(shell.ActiveQueryCanvasDocument);
        queryCanvas.SetDatabaseContext(CreateMetadata(), null);

        shell.ActivateDocument(WorkspaceDocumentType.ErDiagram);
        ErCanvasViewModel erCanvas = Assert.IsType<ErCanvasViewModel>(shell.ActiveErDiagramDocument);
        ErRelationEdgeViewModel edge = Assert.Single(erCanvas.Edges);
        erCanvas.SelectedEdge = edge;
        erCanvas.OpenSelectionInQueryCommand.Execute(null);

        NodeViewModel resultOutput = Assert.Single(queryCanvas.Nodes.Where(node => node.Type == NodeType.ResultOutput));
        queryCanvas.DeselectAll();
        queryCanvas.SelectNode(resultOutput);
        Assert.True(shell.TryRefineSelectedQueryAutoProjection());

        Assert.True(shell.TryResetSelectedQueryAutoProjection());

        NodeViewModel columnSetBuilder = Assert.Single(queryCanvas.Nodes.Where(node => node.Type == NodeType.ColumnSetBuilder));
        var projectionPins = queryCanvas.Connections
            .Where(connection =>
                ReferenceEquals(connection.ToPin?.Owner, columnSetBuilder)
                && string.Equals(connection.ToPin?.Name, "columns", StringComparison.OrdinalIgnoreCase))
            .Select(connection => $"{connection.FromPin.Owner.Subtitle}.{connection.FromPin.Name}")
            .ToArray();

        Assert.Contains("public.orders.id", projectionPins);
        Assert.Contains("public.orders.customer_id", projectionPins);
        Assert.Contains("public.customers.id", projectionPins);
        Assert.Contains("public.customers.name", projectionPins);
        Assert.DoesNotContain("public.orders.order_number", projectionPins);
    }

    [Fact]
    public void TryAddSuggestedFilterToSelectedAutoProjection_CreatesEditableWhereGraph()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();
        CanvasViewModel queryCanvas = Assert.IsType<CanvasViewModel>(shell.ActiveQueryCanvasDocument);
        queryCanvas.SetDatabaseContext(CreateMetadata(), null);

        shell.ActivateDocument(WorkspaceDocumentType.ErDiagram);
        ErCanvasViewModel erCanvas = Assert.IsType<ErCanvasViewModel>(shell.ActiveErDiagramDocument);
        ErRelationEdgeViewModel edge = Assert.Single(erCanvas.Edges);
        erCanvas.SelectedEdge = edge;
        erCanvas.OpenSelectionInQueryCommand.Execute(null);

        NodeViewModel resultOutput = Assert.Single(queryCanvas.Nodes.Where(node => node.Type == NodeType.ResultOutput));
        queryCanvas.DeselectAll();
        queryCanvas.SelectNode(resultOutput);

        bool added = shell.TryAddSuggestedFilterToSelectedAutoProjection();

        Assert.True(added);
        NodeViewModel equalsNode = Assert.Single(queryCanvas.Nodes.Where(node => node.Type == NodeType.Equals));
        NodeViewModel valueNode = Assert.Single(queryCanvas.Nodes.Where(node => node.Type == NodeType.ValueString));
        Assert.Contains(queryCanvas.Connections, connection =>
            ReferenceEquals(connection.ToPin?.Owner, resultOutput)
            && string.Equals(connection.ToPin?.Name, "where", StringComparison.OrdinalIgnoreCase)
            && ReferenceEquals(connection.FromPin.Owner, equalsNode));
        Assert.Contains(queryCanvas.Connections, connection =>
            ReferenceEquals(connection.ToPin?.Owner, equalsNode)
            && string.Equals(connection.ToPin?.Name, "left", StringComparison.OrdinalIgnoreCase)
            && string.Equals(connection.FromPin.Owner.Subtitle, "public.customers", StringComparison.OrdinalIgnoreCase)
            && string.Equals(connection.FromPin.Name, "name", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(queryCanvas.Connections, connection =>
            ReferenceEquals(connection.ToPin?.Owner, equalsNode)
            && string.Equals(connection.ToPin?.Name, "right", StringComparison.OrdinalIgnoreCase)
            && ReferenceEquals(connection.FromPin.Owner, valueNode));
    }

    [Fact]
    public void TryApplySuggestedAggregationToSelectedAutoProjection_RewritesOutputAsGroupByCount()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();
        CanvasViewModel queryCanvas = Assert.IsType<CanvasViewModel>(shell.ActiveQueryCanvasDocument);
        queryCanvas.SetDatabaseContext(CreateMetadata(), null);

        shell.ActivateDocument(WorkspaceDocumentType.ErDiagram);
        ErCanvasViewModel erCanvas = Assert.IsType<ErCanvasViewModel>(shell.ActiveErDiagramDocument);
        ErRelationEdgeViewModel edge = Assert.Single(erCanvas.Edges);
        erCanvas.SelectedEdge = edge;
        erCanvas.OpenSelectionInQueryCommand.Execute(null);

        NodeViewModel resultOutput = Assert.Single(queryCanvas.Nodes.Where(node => node.Type == NodeType.ResultOutput));
        queryCanvas.DeselectAll();
        queryCanvas.SelectNode(resultOutput);

        bool applied = shell.TryApplySuggestedAggregationToSelectedAutoProjection();

        Assert.True(applied);
        NodeViewModel countNode = Assert.Single(queryCanvas.Nodes.Where(node => node.Type == NodeType.CountStar));
        NodeViewModel aliasNode = Assert.Single(queryCanvas.Nodes.Where(node => node.Type == NodeType.Alias));
        Assert.Equal("related_count", aliasNode.Parameters["alias"]);
        Assert.Contains(queryCanvas.Connections, connection =>
            ReferenceEquals(connection.ToPin?.Owner, resultOutput)
            && string.Equals(connection.ToPin?.Name, "group_by", StringComparison.OrdinalIgnoreCase)
            && string.Equals(connection.FromPin.Owner.Subtitle, "public.customers", StringComparison.OrdinalIgnoreCase)
            && string.Equals(connection.FromPin.Name, "name", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(queryCanvas.Connections, connection =>
            ReferenceEquals(connection.ToPin?.Owner, resultOutput)
            && string.Equals(connection.ToPin?.Name, "column", StringComparison.OrdinalIgnoreCase)
            && string.Equals(connection.FromPin.Owner.Subtitle, "public.customers", StringComparison.OrdinalIgnoreCase)
            && string.Equals(connection.FromPin.Name, "name", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(queryCanvas.Connections, connection =>
            ReferenceEquals(connection.ToPin?.Owner, aliasNode)
            && string.Equals(connection.ToPin?.Name, "expression", StringComparison.OrdinalIgnoreCase)
            && ReferenceEquals(connection.FromPin.Owner, countNode));
        Assert.DoesNotContain(queryCanvas.Connections, connection =>
            ReferenceEquals(connection.ToPin?.Owner, resultOutput)
            && string.Equals(connection.ToPin?.Name, "columns", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SelectedQueryJoin_OpenInErDiagram_FocusesSimpleRelation()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();
        CanvasViewModel queryCanvas = Assert.IsType<CanvasViewModel>(shell.ActiveQueryCanvasDocument);
        queryCanvas.SetDatabaseContext(CreateMetadata(), null);

        NodeViewModel orders = AssertTable(queryCanvas, "public.orders", new Avalonia.Point(80, 80));
        NodeViewModel customers = AssertTable(queryCanvas, "public.customers", new Avalonia.Point(80, 280));
        NodeViewModel join = queryCanvas.SpawnNode(NodeDefinitionRegistry.Get(NodeType.Join), new Avalonia.Point(420, 180));
        join.Parameters["join_type"] = "INNER";
        join.Parameters["right_source"] = "public.customers";
        join.Parameters["left_expr"] = "public.orders.customer_id";
        join.Parameters["right_expr"] = "public.customers.id";
        queryCanvas.ConnectPins(orders.OutputPins.Single(pin => pin.Name == "customer_id"), join.InputPins.Single(pin => pin.Name == "left"));
        queryCanvas.ConnectPins(customers.OutputPins.Single(pin => pin.Name == "id"), join.InputPins.Single(pin => pin.Name == "right"));
        queryCanvas.SelectNode(join);

        bool opened = shell.TryOpenSelectedQueryJoinInErDiagram();

        Assert.True(opened);
        Assert.Equal(WorkspaceDocumentType.ErDiagram, shell.ActiveWorkspaceDocumentType);
        ErCanvasViewModel erCanvas = Assert.IsType<ErCanvasViewModel>(shell.ActiveErDiagramDocument);
        ErRelationEdgeViewModel edge = Assert.IsType<ErRelationEdgeViewModel>(erCanvas.SelectedEdge);
        Assert.Equal("public.orders", edge.ChildEntityId);
        Assert.Equal("public.customers", edge.ParentEntityId);
        Assert.True(erCanvas.FocusRequestVersion > 0);
    }

    [Fact]
    public void SelectedQueryJoin_OpenInErDiagram_FocusesCompositeRelation()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();
        CanvasViewModel queryCanvas = Assert.IsType<CanvasViewModel>(shell.ActiveQueryCanvasDocument);
        queryCanvas.SetDatabaseContext(CreateCompositeMetadata(), null);

        NodeViewModel orders = AssertTable(queryCanvas, "public.orders", new Avalonia.Point(80, 80));
        NodeViewModel customers = AssertTable(queryCanvas, "public.customers", new Avalonia.Point(80, 320));
        NodeViewModel join = queryCanvas.SpawnNode(NodeDefinitionRegistry.Get(NodeType.Join), new Avalonia.Point(520, 210));
        NodeViewModel equalsTenant = queryCanvas.SpawnNode(NodeDefinitionRegistry.Get(NodeType.Equals), new Avalonia.Point(260, 150));
        NodeViewModel equalsCustomer = queryCanvas.SpawnNode(NodeDefinitionRegistry.Get(NodeType.Equals), new Avalonia.Point(260, 250));
        NodeViewModel andNode = queryCanvas.SpawnNode(NodeDefinitionRegistry.Get(NodeType.And), new Avalonia.Point(390, 210));

        queryCanvas.ConnectPins(orders.OutputPins.Single(pin => pin.Name == "tenant_id"), join.InputPins.Single(pin => pin.Name == "left"));
        queryCanvas.ConnectPins(customers.OutputPins.Single(pin => pin.Name == "tenant_id"), join.InputPins.Single(pin => pin.Name == "right"));
        queryCanvas.ConnectPins(orders.OutputPins.Single(pin => pin.Name == "tenant_id"), equalsTenant.InputPins.Single(pin => pin.Name == "left"));
        queryCanvas.ConnectPins(customers.OutputPins.Single(pin => pin.Name == "tenant_id"), equalsTenant.InputPins.Single(pin => pin.Name == "right"));
        queryCanvas.ConnectPins(orders.OutputPins.Single(pin => pin.Name == "customer_id"), equalsCustomer.InputPins.Single(pin => pin.Name == "left"));
        queryCanvas.ConnectPins(customers.OutputPins.Single(pin => pin.Name == "id"), equalsCustomer.InputPins.Single(pin => pin.Name == "right"));
        queryCanvas.ConnectPins(equalsTenant.OutputPins.Single(pin => pin.Name == "result"), andNode.InputPins.Single(pin => pin.Name == "conditions"));
        queryCanvas.ConnectPins(equalsCustomer.OutputPins.Single(pin => pin.Name == "result"), andNode.InputPins.Single(pin => pin.Name == "conditions"));
        queryCanvas.ConnectPins(andNode.OutputPins.Single(pin => pin.Name == "result"), join.InputPins.Single(pin => pin.Name == "condition"));
        queryCanvas.SelectNode(join);

        bool opened = shell.TryOpenSelectedQueryJoinInErDiagram();

        Assert.True(opened);
        Assert.Equal(WorkspaceDocumentType.ErDiagram, shell.ActiveWorkspaceDocumentType);
        ErCanvasViewModel erCanvas = Assert.IsType<ErCanvasViewModel>(shell.ActiveErDiagramDocument);
        ErRelationEdgeViewModel edge = Assert.IsType<ErRelationEdgeViewModel>(erCanvas.SelectedEdge);
        Assert.Equal(["tenant_id", "customer_id"], edge.ChildColumns);
        Assert.Equal(["tenant_id", "id"], edge.ParentColumns);
        Assert.True(erCanvas.FocusRequestVersion > 0);
    }

    [Fact]
    public void OpenNewDocument_WhenTypeAlreadyExists_ActivatesExistingInsteadOfCreatingDuplicate()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();
        Guid firstSqlEditorId = shell.OpenNewDocument(WorkspaceDocumentType.SqlEditor);

        Guid secondSqlEditorId = shell.OpenNewDocument(WorkspaceDocumentType.SqlEditor);

        Assert.Equal(firstSqlEditorId, secondSqlEditorId);
        int sqlEditorDocumentCount = 0;
        foreach (OpenWorkspaceDocument document in shell.OpenWorkspaceDocuments)
        {
            if (document.Descriptor.DocumentType == WorkspaceDocumentType.SqlEditor)
                sqlEditorDocumentCount++;
        }

        Assert.Equal(1, sqlEditorDocumentCount);
    }

    [Fact]
    public void OpenNewDocument_SqlResult_WhenTypeAlreadyExists_ActivatesExistingInsteadOfCreatingDuplicate()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();
        Guid firstSqlResultId = shell.OpenNewDocument(WorkspaceDocumentType.SqlResult);

        Guid secondSqlResultId = shell.OpenNewDocument(WorkspaceDocumentType.SqlResult);

        Assert.Equal(firstSqlResultId, secondSqlResultId);
        int sqlResultDocumentCount = 0;
        foreach (OpenWorkspaceDocument document in shell.OpenWorkspaceDocuments)
        {
            if (document.Descriptor.DocumentType == WorkspaceDocumentType.SqlResult)
                sqlResultDocumentCount++;
        }

        Assert.Equal(1, sqlResultDocumentCount);
    }

    [Fact]
    public void SetActiveDocumentType_SqlEditor_HidesDiagramOnlyOverlaysDefensively()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();
        CanvasViewModel queryCanvas = Assert.IsType<CanvasViewModel>(shell.ActiveQueryCanvasDocument);
        queryCanvas.ConnectionManager.IsVisible = true;
        shell.OutputPreview.IsVisible = true;

        Assert.True(shell.IsConnectionManagerOverlayVisible);
        Assert.True(shell.IsOutputPreviewModalVisible);

        shell.ActivateDocument(WorkspaceDocumentType.SqlEditor);

        Assert.False(shell.IsDiagramOverlayLayerVisible);
        Assert.False(shell.IsConnectionManagerOverlayVisible);
        Assert.False(shell.IsOutputPreviewModalVisible);
        Assert.False(shell.OutputPreview.IsVisible);
        Assert.False(queryCanvas.ConnectionManager.IsVisible);
    }

    [Fact]
    public void IsConnectionManagerOverlayVisible_TracksDdlConnectionManagerWhenDdlDocumentIsActive()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();
        shell.ActivateDocument(WorkspaceDocumentType.DdlCanvas);

        CanvasViewModel ddlCanvas = Assert.IsType<CanvasViewModel>(shell.ActiveDdlCanvasDocument);
        ddlCanvas.ConnectionManager.Open();

        Assert.Same(ddlCanvas.ConnectionManager, shell.ActiveConnectionManager);
        Assert.True(shell.IsConnectionManagerVisible);
        Assert.True(shell.IsConnectionManagerOverlayVisible);
    }

    [Fact]
    public void IsConnectionManagerOverlayVisible_AllowsGlobalVisibleManagerWhenDdlDocumentIsActive()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();
        shell.ActivateDocument(WorkspaceDocumentType.DdlCanvas);

        CanvasViewModel queryCanvas = shell.EnsureCanvas();
        queryCanvas.ConnectionManager.Open();

        Assert.NotSame(queryCanvas.ConnectionManager, shell.ActiveConnectionManager);
        Assert.False(shell.IsConnectionManagerVisible);
        Assert.True(shell.IsConnectionManagerOverlayVisible);
    }

    [Fact]
    public void IsConnectionManagerOverlayVisible_InSqlEditorModeFollowsSqlEditorManager()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();
        shell.ActivateDocument(WorkspaceDocumentType.SqlEditor);

        CanvasViewModel queryCanvas = shell.EnsureCanvas();
        var sqlManager = shell.ActiveConnectionManager;
        Assert.NotNull(sqlManager);
        sqlManager.Open();

        Assert.NotSame(queryCanvas.ConnectionManager, sqlManager);
        Assert.Same(shell.SqlEditor.SharedConnectionManager, sqlManager);
        Assert.True(shell.IsConnectionManagerVisible);
        Assert.True(shell.IsConnectionManagerOverlayVisible);
    }

    [Fact]
    public void IsConnectionManagerOverlayVisible_InErModeFollowsSharedManager()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();
        shell.ActivateDocument(WorkspaceDocumentType.ErDiagram);

        CanvasViewModel queryCanvas = shell.EnsureCanvas();
        queryCanvas.ConnectionManager.Open();

        Assert.True(shell.IsConnectionManagerOverlayVisible);
        Assert.Same(queryCanvas.ConnectionManager, shell.ConnectionManagerOverlay);
    }

    [Fact]
    public void ConnectionManagerVisibility_IsExclusiveBetweenQueryAndDdlManagers()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();

        CanvasViewModel queryCanvas = shell.EnsureCanvas();
        CanvasViewModel ddlCanvas = shell.EnsureDdlCanvas();

        queryCanvas.ConnectionManager.Open();
        Assert.True(queryCanvas.ConnectionManager.IsVisible);
        Assert.False(ddlCanvas.ConnectionManager.IsVisible);

        ddlCanvas.ConnectionManager.Open();
        Assert.False(queryCanvas.ConnectionManager.IsVisible);
        Assert.True(ddlCanvas.ConnectionManager.IsVisible);

        queryCanvas.ConnectionManager.Open();
        Assert.True(queryCanvas.ConnectionManager.IsVisible);
        Assert.False(ddlCanvas.ConnectionManager.IsVisible);
    }

    [Fact]
    public void DdlSidebarConnectFlow_OpensOnlyDdlConnectionManager()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();
        shell.ActivateDocument(WorkspaceDocumentType.DdlCanvas);

        CanvasViewModel queryCanvas = shell.EnsureCanvas();
        CanvasViewModel ddlCanvas = shell.EnsureDdlCanvas();

        ddlCanvas.Sidebar.ConnectionManager.ConnectOrOpenManagerCommand.Execute(null);

        Assert.Equal(WorkspaceDocumentType.DdlCanvas, shell.ActiveWorkspaceDocumentType);
        Assert.False(queryCanvas.ConnectionManager.IsVisible);
        Assert.True(ddlCanvas.ConnectionManager.IsVisible);
        Assert.True(shell.IsConnectionManagerOverlayVisible);
        Assert.True(shell.IsDdlConnectionManagerOverlayVisible);
        Assert.False(shell.IsQueryConnectionManagerOverlayVisible);
    }

    [Fact]
    public void DdlSidebarConnectFlow_ShowsOverlay_EvenWhenModalRouteWasPreviouslyBoundToQuery()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();

        CanvasViewModel queryCanvas = shell.EnsureCanvas();
        CanvasViewModel ddlCanvas = shell.EnsureDdlCanvas();

        shell.AttachConnectionModalToActiveDocument();
        queryCanvas.ConnectionManager.Open();
        queryCanvas.ConnectionManager.CloseCommand.Execute(null);

        shell.ActivateDocument(WorkspaceDocumentType.DdlCanvas);
        ddlCanvas.Sidebar.ConnectionManager.ConnectOrOpenManagerCommand.Execute(null);

        Assert.True(ddlCanvas.ConnectionManager.IsVisible);
        Assert.True(shell.IsConnectionManagerOverlayVisible);
        Assert.Same(ddlCanvas.ConnectionManager, shell.ConnectionManagerOverlay);
    }

    [Fact]
    public void DdlSidebarConnectFlow_CloseCommand_HidesOverlayAfterOpeningManager()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();
        shell.ActivateDocument(WorkspaceDocumentType.DdlCanvas);

        CanvasViewModel ddlCanvas = shell.EnsureDdlCanvas();
        ddlCanvas.Sidebar.ConnectionManager.ConnectOrOpenManagerCommand.Execute(null);

        Assert.True(ddlCanvas.ConnectionManager.IsVisible);
        Assert.True(shell.IsConnectionManagerOverlayVisible);
        Assert.Same(ddlCanvas.ConnectionManager, shell.ConnectionManagerOverlay);

        ddlCanvas.ConnectionManager.CloseCommand.Execute(null);

        Assert.False(ddlCanvas.ConnectionManager.IsVisible);
        Assert.False(shell.IsConnectionManagerOverlayVisible);
    }

    [Fact]
    public void SqlEditorMode_ExplainPreview_DoesNotRequireSwitchingToCanvas()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();
        shell.ActivateDocument(WorkspaceDocumentType.SqlEditor);

        bool opened = shell.TryOpenSqlExplainPreview(
            sql: "SELECT 1",
            provider: DatabaseProvider.Postgres,
            connectionConfig: null);

        Assert.True(opened);
        Assert.Equal(WorkspaceDocumentType.SqlEditor, shell.ActiveWorkspaceDocumentType);
        Assert.True(shell.OutputPreview.IsVisible);
        Assert.True(shell.OutputPreview.IsSqlExplainMode);
    }

    [Fact]
    public void SqlEditorMode_BenchmarkPreview_DoesNotRequireSwitchingToCanvas()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();
        shell.ActivateDocument(WorkspaceDocumentType.SqlEditor);

        bool opened = shell.TryOpenSqlBenchmarkPreview(
            sql: "SELECT 1",
            connectionConfig: null);

        Assert.True(opened);
        Assert.Equal(WorkspaceDocumentType.SqlEditor, shell.ActiveWorkspaceDocumentType);
        Assert.True(shell.OutputPreview.IsVisible);
        Assert.True(shell.OutputPreview.IsSqlBenchmarkMode);
    }

    [Fact]
    public void TryActivateWorkspaceDocument_WithUnknownId_KeepsCurrentActiveDocument()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();
        OpenWorkspaceDocument activeBefore = Assert.IsType<OpenWorkspaceDocument>(shell.ActiveWorkspaceDocument);

        bool changed = shell.TryActivateWorkspaceDocument(Guid.NewGuid());

        Assert.False(changed);
        OpenWorkspaceDocument activeAfter = Assert.IsType<OpenWorkspaceDocument>(shell.ActiveWorkspaceDocument);
        Assert.Equal(activeBefore.Descriptor.DocumentId, activeAfter.Descriptor.DocumentId);
    }

    [Fact]
    public void CloseActiveWorkspaceDocument_ActivatesNextDocumentDeterministically()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();
        Guid ddlId = shell.OpenNewDocument(WorkspaceDocumentType.DdlCanvas);
        Guid sqlId = shell.OpenNewDocument(WorkspaceDocumentType.SqlEditor);
        Assert.True(shell.TryActivateWorkspaceDocument(ddlId));

        bool closed = shell.TryCloseWorkspaceDocument(ddlId);

        Assert.True(closed);
        OpenWorkspaceDocument active = Assert.IsType<OpenWorkspaceDocument>(shell.ActiveWorkspaceDocument);
        Assert.Equal(sqlId, active.Descriptor.DocumentId);
    }

    [Fact]
    public async Task TryCloseWorkspaceDocument_WhenSubEditorHasUnsavedChanges_BlocksClose()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();
        CanvasViewModel queryCanvas = Assert.IsType<CanvasViewModel>(shell.ActiveQueryCanvasDocument);
        NodeViewModel cte = BuildBasicCteEditableGraph(queryCanvas);

        Assert.True(await queryCanvas.EnterCteEditorAsync(cte));
        NodeViewModel editedNode = queryCanvas.SpawnNode(
            NodeDefinitionRegistry.Get(NodeType.Equals),
            new Avalonia.Point(420, 220));
        Assert.True(queryCanvas.IsInCteEditor);
        Assert.True(queryCanvas.IsDirty);

        Guid queryId = Assert.Single(shell.OpenWorkspaceDocuments
            .Where(document => document.Descriptor.DocumentType == WorkspaceDocumentType.QueryCanvas))
            .Descriptor.DocumentId;

        bool closed = shell.TryCloseWorkspaceDocument(queryId);

        Assert.False(closed);
        Assert.Contains(shell.OpenWorkspaceDocuments, document => document.Descriptor.DocumentId == queryId);
        Assert.True(queryCanvas.IsInCteEditor);
        Assert.Contains(queryCanvas.Nodes, node => node.Id == editedNode.Id);
    }

    [Fact]
    public void RestoreWorkspaceDocuments_RebuildsDocumentOrderAndActiveSelection()
    {
        var shell = new ShellViewModel(connectionManagerViewModelFactory: global::AkkornStudio.UI.Services.ConnectionManager.ConnectionManagerViewModelFactory.CreateDefault());
        shell.EnterCanvas();

        Guid queryId = Guid.NewGuid();
        Guid ddlId = Guid.NewGuid();
        Guid sqlId = Guid.NewGuid();
        var workspace = new SavedWorkspaceDocumentsCanvas(
            Version: 5,
            Documents:
            [
                new SavedWorkspaceDocument(
                    DocumentId: queryId,
                    DocumentType: WorkspaceDocumentType.QueryCanvas.ToString(),
                    Title: "Query A",
                    IsDirty: false,
                    PersistenceSchemaVersion: "1.0",
                    CanvasPayload: EmptyCanvasPayload()),
                new SavedWorkspaceDocument(
                    DocumentId: ddlId,
                    DocumentType: WorkspaceDocumentType.DdlCanvas.ToString(),
                    Title: "DDL A",
                    IsDirty: false,
                    PersistenceSchemaVersion: "1.0",
                    CanvasPayload: EmptyCanvasPayload()),
                new SavedWorkspaceDocument(
                    DocumentId: sqlId,
                    DocumentType: WorkspaceDocumentType.SqlEditor.ToString(),
                    Title: "SQL A",
                    IsDirty: false,
                    PersistenceSchemaVersion: "1.0")
            ],
            ActiveDocumentId: ddlId);

        shell.RestoreWorkspaceDocuments(workspace);

        Assert.Equal(3, shell.OpenWorkspaceDocuments.Count);
        Assert.Equal(queryId, shell.OpenWorkspaceDocuments[0].Descriptor.DocumentId);
        Assert.Equal(ddlId, shell.OpenWorkspaceDocuments[1].Descriptor.DocumentId);
        Assert.Equal(sqlId, shell.OpenWorkspaceDocuments[2].Descriptor.DocumentId);
        Assert.Equal(ddlId, shell.ActiveWorkspaceDocumentId);
        Assert.Equal(WorkspaceDocumentType.DdlCanvas, shell.ActiveWorkspaceDocumentType);
    }

    private static SavedCanvas EmptyCanvasPayload()
    {
        return new SavedCanvas(
            Version: 3,
            DatabaseProvider: null,
            ConnectionName: null,
            Zoom: 1,
            PanX: 0,
            PanY: 0,
            Nodes: [],
            Connections: [],
            SelectBindings: [],
            WhereBindings: [],
            AppVersion: "test",
            CreatedAt: DateTime.UtcNow.ToString("o"));
    }

    private static DbMetadata CreateMetadata()
    {
        TableMetadata customers = new(
            Schema: "public",
            Name: "customers",
            Kind: TableKind.Table,
            EstimatedRowCount: 10,
            Columns:
            [
                new ColumnMetadata("id", "int", "int", false, true, false, true, true, 1),
                new ColumnMetadata("name", "text", "text", false, false, false, false, false, 2),
            ],
            Indexes: [],
            OutboundForeignKeys: [],
            InboundForeignKeys: []);

        ForeignKeyRelation ordersToCustomers = new(
            ConstraintName: "fk_orders_customers",
            ChildSchema: "public",
            ChildTable: "orders",
            ChildColumn: "customer_id",
            ParentSchema: "public",
            ParentTable: "customers",
            ParentColumn: "id",
            OnDelete: ReferentialAction.NoAction,
            OnUpdate: ReferentialAction.NoAction);

        TableMetadata orders = new(
            Schema: "public",
            Name: "orders",
            Kind: TableKind.Table,
            EstimatedRowCount: 20,
            Columns:
            [
                new ColumnMetadata("id", "int", "int", false, true, false, true, true, 1),
                new ColumnMetadata("customer_id", "int", "int", false, false, true, false, true, 2),
                new ColumnMetadata("order_number", "text", "text", false, false, false, false, false, 3),
            ],
            Indexes: [],
            OutboundForeignKeys: [ordersToCustomers],
            InboundForeignKeys: []);

        return new DbMetadata(
            DatabaseName: "akkorn",
            Provider: DatabaseProvider.Postgres,
            ServerVersion: "16",
            CapturedAt: DateTimeOffset.UtcNow,
            Schemas:
            [
                new SchemaMetadata("public", [customers, orders]),
            ],
            AllForeignKeys: [ordersToCustomers]);
    }

    private static DbMetadata CreateCompositeMetadata()
    {
        ForeignKeyRelation ordersToCustomersTenant = new(
            ConstraintName: "fk_orders_customers_composite",
            ChildSchema: "public",
            ChildTable: "orders",
            ChildColumn: "tenant_id",
            ParentSchema: "public",
            ParentTable: "customers",
            ParentColumn: "tenant_id",
            OnDelete: ReferentialAction.NoAction,
            OnUpdate: ReferentialAction.NoAction,
            OrdinalPosition: 1);

        ForeignKeyRelation ordersToCustomersCustomer = new(
            ConstraintName: "fk_orders_customers_composite",
            ChildSchema: "public",
            ChildTable: "orders",
            ChildColumn: "customer_id",
            ParentSchema: "public",
            ParentTable: "customers",
            ParentColumn: "id",
            OnDelete: ReferentialAction.NoAction,
            OnUpdate: ReferentialAction.NoAction,
            OrdinalPosition: 2);

        TableMetadata customers = new(
            Schema: "public",
            Name: "customers",
            Kind: TableKind.Table,
            EstimatedRowCount: 10,
            Columns:
            [
                new ColumnMetadata("tenant_id", "int", "int", false, true, false, true, true, 1),
                new ColumnMetadata("id", "int", "int", false, true, false, true, true, 2),
            ],
            Indexes: [],
            OutboundForeignKeys: [],
            InboundForeignKeys: [ordersToCustomersTenant, ordersToCustomersCustomer]);

        TableMetadata orders = new(
            Schema: "public",
            Name: "orders",
            Kind: TableKind.Table,
            EstimatedRowCount: 20,
            Columns:
            [
                new ColumnMetadata("tenant_id", "int", "int", false, false, true, false, true, 1),
                new ColumnMetadata("customer_id", "int", "int", false, false, true, false, true, 2),
            ],
            Indexes: [],
            OutboundForeignKeys: [ordersToCustomersTenant, ordersToCustomersCustomer],
            InboundForeignKeys: []);

        return new DbMetadata(
            DatabaseName: "akkorn",
            Provider: DatabaseProvider.Postgres,
            ServerVersion: "16",
            CapturedAt: DateTimeOffset.UtcNow,
            Schemas:
            [
                new SchemaMetadata("public", [customers, orders]),
            ],
            AllForeignKeys: [ordersToCustomersTenant, ordersToCustomersCustomer]);
    }

    private static DbMetadata CreateTechnicalProjectionMetadata()
    {
        TableMetadata customers = new(
            Schema: "public",
            Name: "customers",
            Kind: TableKind.Table,
            EstimatedRowCount: 10,
            Columns:
            [
                new ColumnMetadata("id", "int", "int", false, true, false, true, true, 1),
                new ColumnMetadata("created_at", "timestamp", "timestamp", false, false, false, false, false, 2),
                new ColumnMetadata("updated_at", "timestamp", "timestamp", false, false, false, false, false, 3),
                new ColumnMetadata("display_name", "text", "text", false, false, false, false, false, 4),
            ],
            Indexes: [],
            OutboundForeignKeys: [],
            InboundForeignKeys: []);

        ForeignKeyRelation ordersToCustomers = new(
            ConstraintName: "fk_orders_customers",
            ChildSchema: "public",
            ChildTable: "orders",
            ChildColumn: "customer_id",
            ParentSchema: "public",
            ParentTable: "customers",
            ParentColumn: "id",
            OnDelete: ReferentialAction.NoAction,
            OnUpdate: ReferentialAction.NoAction);

        TableMetadata orders = new(
            Schema: "public",
            Name: "orders",
            Kind: TableKind.Table,
            EstimatedRowCount: 20,
            Columns:
            [
                new ColumnMetadata("id", "int", "int", false, true, false, true, true, 1),
                new ColumnMetadata("customer_id", "int", "int", false, false, true, false, true, 2),
            ],
            Indexes: [],
            OutboundForeignKeys: [ordersToCustomers],
            InboundForeignKeys: []);

        return new DbMetadata(
            DatabaseName: "akkorn",
            Provider: DatabaseProvider.Postgres,
            ServerVersion: "16",
            CapturedAt: DateTimeOffset.UtcNow,
            Schemas:
            [
                new SchemaMetadata("public", [customers, orders]),
            ],
            AllForeignKeys: [ordersToCustomers]);
    }

    private static NodeViewModel AssertTable(CanvasViewModel canvas, string tableName, Avalonia.Point position)
    {
        Assert.True(canvas.TryInsertSchemaTableNode(tableName, position));
        return Assert.Single(canvas.Nodes.Where(node =>
            node.IsTableSource
            && string.Equals(node.Subtitle, tableName, StringComparison.OrdinalIgnoreCase)));
    }

    private static NodeViewModel BuildBasicCteEditableGraph(CanvasViewModel canvas)
    {
        canvas.Nodes.Clear();
        canvas.Connections.Clear();
        canvas.UndoRedo.Clear();

        NodeViewModel table = new("public.orders", [("id", PinDataType.Number)], new Avalonia.Point(0, 0));
        NodeViewModel columns = new(NodeDefinitionRegistry.Get(NodeType.ColumnList), new Avalonia.Point(130, 0));
        NodeViewModel result = new(NodeDefinitionRegistry.Get(NodeType.ResultOutput), new Avalonia.Point(260, 0));
        NodeViewModel cte = new(NodeDefinitionRegistry.Get(NodeType.CteDefinition), new Avalonia.Point(400, 0));
        cte.Parameters["name"] = "orders_cte";

        canvas.Nodes.Add(table);
        canvas.Nodes.Add(columns);
        canvas.Nodes.Add(result);
        canvas.Nodes.Add(cte);

        Connect(canvas, table, "id", columns, "columns");
        Connect(canvas, columns, "result", result, "columns");
        Connect(canvas, result, "result", cte, "query");
        return cte;
    }

    private static void Connect(
        CanvasViewModel canvas,
        NodeViewModel fromNode,
        string fromPin,
        NodeViewModel toNode,
        string toPin)
    {
        PinViewModel from = fromNode.OutputPins.First(pin => pin.Name == fromPin);
        PinViewModel to = toNode.InputPins.First(pin => pin.Name == toPin);
        canvas.Connections.Add(new ConnectionViewModel(from, from.AbsolutePosition, to.AbsolutePosition)
        {
            ToPin = to,
        });
    }
}

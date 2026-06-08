using System.Collections.ObjectModel;
using System.Collections.Specialized;
using AkkornStudio.Metadata;
using AkkornStudio.UI.Controls;
using AkkornStudio.UI.ViewModels;
using Avalonia;
using AkkornStudio.CanvasKit;

namespace AkkornStudio.UI.ViewModels.ErDiagram;

/// <summary>
/// Represents the full ER canvas state for schema visualization and editing.
/// </summary>
public sealed class ErCanvasViewModel : ViewModelBase, ICanvasViewportState, ICanvasViewportSelectionState
{
    private const double EntityWidth = 420d;

    private Action<ErRelationEdgeViewModel>? _openSelectionInQuery;
    private Action<ErEntityNodeViewModel>? _openEntityDefinitionInSqlEditor;
    private ErEntityNodeViewModel? _selectedEntity;
    private ErRelationEdgeViewModel? _selectedEdge;
    private ErRelationEdgeViewModel? _hoveredEdge;
    private ErEntityNodeViewModel? _hoveredEntity;
    private double _viewportX;
    private double _viewportY;
    private double _zoom = 1.0;
    private double _viewportWidth;
    private double _viewportHeight;
    private double _focusTargetX;
    private double _focusTargetY;
    private int _focusRequestVersion;
    private bool _includeViews;
    private bool _isRebuilding;
    private bool _pendingAutoFit;
    private bool _isDirty;
    private bool _refreshNeedsConfirmation;
    private ErNodeDensity _selectedDensity = ErNodeDensity.Normal;
    private string _searchTerm = string.Empty;
    private bool _isMinimapVisible;
    private bool _isFocusModeEnabled = true;
    private DbMetadata? _sourceMetadata;
    private Func<ErCanvasSyncRequest, bool>? _syncToDdl;
    private readonly ErEditHistory _history = new();

    public ErCanvasViewModel()
    {
        Entities = [];
        Edges = [];
        SelectedEntities = [];
        TechnicalWarnings = [];
        RefreshCommand = new RelayCommand(RequestRefreshFromSourceMetadata);
        OpenSelectionInQueryCommand = new RelayCommand(OpenSelectionInQuery, CanOpenSelectionInQuery);
        SyncToDdlCommand = new RelayCommand(SyncToDdl, CanSyncToDdl);
        UndoCommand = new RelayCommand(UndoFromCommand, () => CanUndo);
        RedoCommand = new RelayCommand(RedoFromCommand, () => CanRedo);
        DeleteSelectionCommand = new RelayCommand(DeleteSelectionFromCommand, () => HasSelectionDetails);
        FindEntityCommand = new RelayCommand(FindAndSelectEntity, CanFindEntity);
        AutoLayoutCommand = new RelayCommand(ApplyAutoLayout, CanAutoLayout);
        FitToScreenCommand = new RelayCommand(FitToContents, () => EntityCount > 0);
        EditSelectedDefinitionInSqlEditorCommand = new RelayCommand(EditSelectedDefinitionInSqlEditor, CanEditSelectedDefinitionInSqlEditor);
        HideUnrelatedEntitiesCommand = new RelayCommand(HideUnrelatedEntities, CanHideUnrelatedEntities);
        ShowAllEntitiesCommand = new RelayCommand(ShowAllEntities, CanShowAllEntities);

        Entities.CollectionChanged += OnEntitiesChanged;
        Edges.CollectionChanged += OnEdgesChanged;
        SelectedEntities.CollectionChanged += OnSelectedEntitiesChanged;
        TechnicalWarnings.CollectionChanged += OnTechnicalWarningsChanged;
    }

    public ObservableCollection<ErEntityNodeViewModel> Entities { get; }

    public ObservableCollection<ErRelationEdgeViewModel> Edges { get; }

    public ObservableCollection<ErEntityNodeViewModel> SelectedEntities { get; }

    public ObservableCollection<string> TechnicalWarnings { get; }

    public ErEntityNodeViewModel? SelectedEntity
    {
        get => _selectedEntity;
        set
        {
            if (value is null)
            {
                if (_selectedEntity is null && SelectedEntities.Count == 0)
                    return;

                ClearEntitySelection();
                return;
            }

            SelectEntity(value, additiveSelection: false);
        }
    }

    public ErRelationEdgeViewModel? SelectedEdge
    {
        get => _selectedEdge;
        set
        {
            if (ReferenceEquals(_selectedEdge, value))
                return;

            if (_selectedEdge is not null)
                _selectedEdge.IsSelected = false;

            if (!Set(ref _selectedEdge, value))
                return;

            if (_selectedEdge is not null)
                _selectedEdge.IsSelected = true;

            if (value is not null)
            {
                ClearEntitySelectionCore();
            }

            SetHoveredEdge(null);

            ApplySelectionHighlights();
            RaiseSelectionDetailPropertiesChanged();
        }
    }

    public double ViewportX
    {
        get => _viewportX;
        set => Set(ref _viewportX, value);
    }

    public double ViewportY
    {
        get => _viewportY;
        set => Set(ref _viewportY, value);
    }

    public double Zoom
    {
        get => _zoom;
        set
        {
            if (!Set(ref _zoom, value))
                return;

            RaisePropertyChanged(nameof(ZoomDisplay));
        }
    }

    public Point PanOffset
    {
        get => new(ViewportX, ViewportY);
        set
        {
            ViewportX = value.X;
            ViewportY = value.Y;
        }
    }

    public double ViewportWidth
    {
        get => _viewportWidth;
        private set => Set(ref _viewportWidth, value);
    }

    public double ViewportHeight
    {
        get => _viewportHeight;
        private set => Set(ref _viewportHeight, value);
    }

    public bool IncludeViews
    {
        get => _includeViews;
        set
        {
            if (!Set(ref _includeViews, value))
                return;

            if (!_isRebuilding)
                RefreshFromSourceMetadata();
        }
    }

    public ErNodeDensity SelectedDensity
    {
        get => _selectedDensity;
        set
        {
            if (!Set(ref _selectedDensity, value))
                return;

            foreach (ErEntityNodeViewModel entity in Entities)
                entity.Density = value;

            RecomputeEdgeGeometry();
        }
    }

    public IReadOnlyList<ErNodeDensity> DensityOptions { get; } =
    [
        ErNodeDensity.Compact,
        ErNodeDensity.Normal,
        ErNodeDensity.Detailed
    ];

    public string SearchTerm
    {
        get => _searchTerm;
        set
        {
            if (!Set(ref _searchTerm, value))
                return;

            FindEntityCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsMinimapVisible
    {
        get => _isMinimapVisible;
        set => Set(ref _isMinimapVisible, value);
    }

    public bool IsFocusModeEnabled
    {
        get => _isFocusModeEnabled;
        set
        {
            if (!Set(ref _isFocusModeEnabled, value))
                return;

            ApplySelectionHighlights();
        }
    }

    public int EntityCount => Entities.Count;

    public int EdgeCount => Edges.Count;

    public int SelectedEntityCount => SelectedEntities.Count;

    public string ZoomDisplay => $"{Zoom * 100:0}%";

    public bool HasTechnicalWarnings => TechnicalWarnings.Count > 0;

    public bool HasSelectionDetails => SelectedEntityCount > 0 || SelectedEdge is not null;

    public bool HasSingleEntitySelection => SelectedEntityCount == 1;

    public bool HasMultipleEntitySelection => SelectedEntityCount > 1;

    public bool CanEditSelectedViewDefinition =>
        SelectedEntityCount == 1 && SelectedEntity?.IsView == true;

    public bool HasSelectedEntityMetadata => SelectedEntityCount == 1 && SelectedEntity is not null;

    public string SelectedEntityQualifiedName =>
        SelectedEntity is null ? string.Empty : $"{SelectedEntity.DisplaySchema}.{SelectedEntity.DisplayName}";

    public string SelectedEntityObjectType => SelectedEntity?.ObjectTypeLabel ?? string.Empty;

    public string SelectedEntityStats =>
        SelectedEntity is null
            ? string.Empty
            : $"{SelectedEntity.ColumnCount} columns | {SelectedEntity.PrimaryKeyCount} PK | {SelectedEntity.ForeignKeyCount} FK | {SelectedEntity.Columns.Count(static c => c.IsUnique)} IX";

    public string SelectedEntityNullabilityStats =>
        SelectedEntity is null
            ? string.Empty
            : $"{SelectedEntity.Columns.Count(static c => c.IsNullable)} nullable | {SelectedEntity.Columns.Count(static c => !c.IsNullable)} not null";

    public string SelectedEntityRelationshipStats =>
        SelectedEntity is null
            ? string.Empty
            : $"{GetOutboundRelationCount(SelectedEntity)} outbound | {GetInboundRelationCount(SelectedEntity)} inbound";

    public string SelectedEntityPrimaryKeyColumns =>
        SelectedEntity is null
            ? string.Empty
            : JoinColumnsOrDash(SelectedEntity.Columns.Where(static c => c.IsPrimaryKey).Select(static c => c.ColumnName));

    public string SelectedEntityForeignKeyColumns =>
        SelectedEntity is null
            ? string.Empty
            : JoinColumnsOrDash(SelectedEntity.Columns.Where(static c => c.IsForeignKey).Select(static c => c.ColumnName));

    public string SelectedEntityIndexedColumns =>
        SelectedEntity is null
            ? string.Empty
            : JoinColumnsOrDash(SelectedEntity.Columns.Where(static c => c.IsUnique).Select(static c => c.ColumnName));

    public int SelectedEntityColumnCount => SelectedEntity?.ColumnCount ?? 0;

    public int SelectedEntityPrimaryKeyCount => SelectedEntity?.PrimaryKeyCount ?? 0;

    public int SelectedEntityForeignKeyCount => SelectedEntity?.ForeignKeyCount ?? 0;

    public bool HasSelectedEntityPrimaryKeys => SelectedEntity?.PrimaryKeyCount > 0;

    public bool HasSelectedEntityForeignKeys => SelectedEntity?.ForeignKeyCount > 0;

    public bool HasSelectedEntityIndexes =>
        SelectedEntity is not null && SelectedEntity.Columns.Any(static c => c.IsUnique);

    public bool IsDirty
    {
        get => _isDirty;
        private set => Set(ref _isDirty, value);
    }

    public bool RefreshNeedsConfirmation
    {
        get => _refreshNeedsConfirmation;
        private set => Set(ref _refreshNeedsConfirmation, value);
    }

    public bool CanUndo => _history.CanUndo;

    public bool CanRedo => _history.CanRedo;

    public string StatusMessage =>
        _sourceMetadata is null
            ? "Conecte-se a um banco para gerar o diagrama ER."
            : $"Fonte sincronizada com {EntityCount} entidade(s) e {EdgeCount} relacao(oes).{(IsDirty ? " Alteracoes locais pendentes." : string.Empty)}";

    public string SelectionTitle =>
        SelectedEdge is not null
            ? SelectedEdge.ConstraintLabel
            : HasMultipleEntitySelection
                ? $"{SelectedEntityCount} entidades selecionadas"
                : SelectedEntity is not null
                ? SelectedEntity.DisplayName
                : "Nada selecionado";

    public string SelectionSubtitle =>
        SelectedEdge is not null
            ? $"{SelectedEdge.ChildEntityId} -> {SelectedEdge.ParentEntityId}"
            : HasMultipleEntitySelection
                ? "Multiselecao ativa. Shift/Ctrl + clique para ajustar a selecao."
                : SelectedEntity is not null
                ? (SelectedEntity.IsView ? "View" : "Tabela")
                : "Selecione uma entidade ou relacionamento para ver os detalhes.";

    public string SelectionBody =>
        SelectedEdge is not null
            ? $"Mapeamento: {SelectedEdge.MappingSummary}"
            : HasMultipleEntitySelection
                ? BuildMultiSelectionSummary()
            : SelectedEntity is not null
                ? SelectedEntity.SelectionSummary
                : string.Empty;

    public string SelectionJoinPredicate => SelectedEdge?.JoinPredicateSql ?? string.Empty;

    public bool HasSelectionJoinPredicate => SelectedEdge is not null;

    public double FocusTargetX
    {
        get => _focusTargetX;
        private set => Set(ref _focusTargetX, value);
    }

    public double FocusTargetY
    {
        get => _focusTargetY;
        private set => Set(ref _focusTargetY, value);
    }

    public int FocusRequestVersion
    {
        get => _focusRequestVersion;
        private set => Set(ref _focusRequestVersion, value);
    }

    public RelayCommand RefreshCommand { get; }

    public RelayCommand OpenSelectionInQueryCommand { get; }

    public RelayCommand SyncToDdlCommand { get; }

    public RelayCommand UndoCommand { get; }

    public RelayCommand RedoCommand { get; }

    public RelayCommand DeleteSelectionCommand { get; }

    public RelayCommand FindEntityCommand { get; }

    public RelayCommand AutoLayoutCommand { get; }

    public RelayCommand FitToScreenCommand { get; }

    public RelayCommand EditSelectedDefinitionInSqlEditorCommand { get; }

    public RelayCommand HideUnrelatedEntitiesCommand { get; }

    public RelayCommand ShowAllEntitiesCommand { get; }

    public void BindSourceMetadata(DbMetadata? metadata, bool rebuild = true)
    {
        _sourceMetadata = metadata;
        RaisePropertyChanged(nameof(StatusMessage));

        if (rebuild)
            RefreshFromSourceMetadata();
    }

    public void BindQueryNavigation(Action<ErRelationEdgeViewModel>? openSelectionInQuery)
    {
        _openSelectionInQuery = openSelectionInQuery;
        OpenSelectionInQueryCommand.NotifyCanExecuteChanged();
    }

    public void BindEntityDefinitionNavigation(Action<ErEntityNodeViewModel>? openEntityDefinitionInSqlEditor)
    {
        _openEntityDefinitionInSqlEditor = openEntityDefinitionInSqlEditor;
        EditSelectedDefinitionInSqlEditorCommand.NotifyCanExecuteChanged();
    }

    public void BindSyncToDdl(Func<ErCanvasSyncRequest, bool>? syncToDdl)
    {
        _syncToDdl = syncToDdl;
        SyncToDdlCommand.NotifyCanExecuteChanged();
    }

    public void MarkDirty()
    {
        IsDirty = true;
        RefreshNeedsConfirmation = false;
        RaisePropertyChanged(nameof(StatusMessage));
        SyncToDdlCommand.NotifyCanExecuteChanged();
    }

    public void SelectEntity(ErEntityNodeViewModel entity, bool additiveSelection)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (!additiveSelection)
        {
            SetSingleEntitySelection(entity);
            return;
        }

        if (SelectedEdge is not null)
            SelectedEdge = null;

        if (SelectedEntities.Contains(entity))
        {
            if (SelectedEntities.Count == 1)
                return;

            SelectedEntities.Remove(entity);
            if (ReferenceEquals(SelectedEntity, entity))
                SetPrimarySelectedEntity(SelectedEntities.LastOrDefault());
        }
        else
        {
            SelectedEntities.Add(entity);
            SetPrimarySelectedEntity(entity);
        }

        SetHoveredEdge(null);
        ApplySelectionHighlights();
        RaiseSelectionDetailPropertiesChanged();
    }

    public void RecordEntityMove(ErEntityNodeViewModel entity, Point from, Point to)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (Math.Abs(from.X - to.X) < 0.001 && Math.Abs(from.Y - to.Y) < 0.001)
            return;

        ExecuteMutation(new ErCanvasMutation(
            Description: "ER: mover tabela",
            Execute: () =>
            {
                entity.X = to.X;
                entity.Y = to.Y;
                RecomputeEdgeGeometry();
            },
            Undo: () =>
            {
                entity.X = from.X;
                entity.Y = from.Y;
                RecomputeEdgeGeometry();
            }));
    }

    public void RecordEntityMoves(IReadOnlyDictionary<ErEntityNodeViewModel, Point> fromPositions)
    {
        ArgumentNullException.ThrowIfNull(fromPositions);
        if (fromPositions.Count == 0)
            return;

        var changed = fromPositions
            .Select(pair => new
            {
                Entity = pair.Key,
                From = pair.Value,
                To = new Point(pair.Key.X, pair.Key.Y),
            })
            .Where(item =>
                Math.Abs(item.From.X - item.To.X) >= 0.001 || Math.Abs(item.From.Y - item.To.Y) >= 0.001)
            .ToList();

        if (changed.Count == 0)
            return;

        ExecuteMutation(new ErCanvasMutation(
            Description: changed.Count == 1 ? "ER: mover tabela" : "ER: mover tabelas",
            Execute: () =>
            {
                foreach (var item in changed)
                {
                    item.Entity.X = item.To.X;
                    item.Entity.Y = item.To.Y;
                }
                RecomputeEdgeGeometry();
            },
            Undo: () =>
            {
                foreach (var item in changed)
                {
                    item.Entity.X = item.From.X;
                    item.Entity.Y = item.From.Y;
                }
                RecomputeEdgeGeometry();
            }));
    }

    public bool DeleteSelection()
    {
        if (SelectedEdge is not null)
        {
            ErRelationEdgeViewModel edge = SelectedEdge;
            ExecuteMutation(new ErCanvasMutation(
                Description: "ER: remover relacionamento",
                Execute: () =>
                {
                    Edges.Remove(edge);
                    if (ReferenceEquals(SelectedEdge, edge))
                        SelectedEdge = null;
                    RecomputeEdgeGeometry();
                },
                Undo: () =>
                {
                    if (!Edges.Contains(edge))
                        Edges.Add(edge);
                    SelectedEdge = edge;
                    RecomputeEdgeGeometry();
                }));
            return true;
        }

        if (SelectedEntities.Count == 0)
            return false;

        List<ErEntityNodeViewModel> entities = [.. SelectedEntities];
        var selectedIds = new HashSet<string>(
            entities.Select(entity => entity.Id),
            StringComparer.OrdinalIgnoreCase);
        List<ErRelationEdgeViewModel> attachedEdges = [.. Edges.Where(edge =>
            selectedIds.Contains(edge.ChildEntityId) || selectedIds.Contains(edge.ParentEntityId))];
        var originalIndexes = entities.ToDictionary(
            static entity => entity,
            entity => Entities.IndexOf(entity));

        ExecuteMutation(new ErCanvasMutation(
            Description: entities.Count == 1 ? "ER: remover tabela" : "ER: remover tabelas",
            Execute: () =>
            {
                foreach (ErRelationEdgeViewModel edge in attachedEdges)
                    Edges.Remove(edge);
                foreach (ErEntityNodeViewModel entity in entities)
                    Entities.Remove(entity);
                ClearSelection();
                RecomputeEdgeGeometry();
            },
            Undo: () =>
            {
                foreach (ErEntityNodeViewModel entity in entities.OrderBy(entity => originalIndexes[entity]))
                {
                    int originalIndex = originalIndexes[entity];
                    int restoreIndex = originalIndex < 0 ? Entities.Count : Math.Min(originalIndex, Entities.Count);
                    if (!Entities.Contains(entity))
                        Entities.Insert(restoreIndex, entity);
                }
                foreach (ErRelationEdgeViewModel edge in attachedEdges)
                {
                    if (!Edges.Contains(edge))
                        Edges.Add(edge);
                }
                SetEntitiesSelection(entities);
                RecomputeEdgeGeometry();
            }));
        return true;
    }

    private void DeleteSelectionFromCommand() => DeleteSelection();

    public bool Undo()
    {
        bool undone = _history.Undo();
        if (!undone)
            return false;

        OnHistoryChanged();
        return true;
    }

    private void UndoFromCommand() => Undo();

    private void RedoFromCommand() => Redo();

    public bool Redo()
    {
        bool redone = _history.Redo();
        if (!redone)
            return false;

        OnHistoryChanged();
        return true;
    }

    public void ClearSelection()
    {
        ClearEntitySelectionCore();
        SetHoveredEntity(null);
        SetHoveredEdge(null);
        SelectedEdge = null;
        ApplySelectionHighlights();
        RaiseSelectionDetailPropertiesChanged();
    }

    public void SetHoveredEdge(ErRelationEdgeViewModel? edge)
    {
        if (ReferenceEquals(_hoveredEdge, edge))
            return;

        if (_hoveredEdge is not null)
            _hoveredEdge.IsHovered = false;

        _hoveredEdge = edge;

        if (_hoveredEdge is not null)
            _hoveredEdge.IsHovered = true;

        ApplySelectionHighlights();
    }

    public void SetHoveredEntity(ErEntityNodeViewModel? entity)
    {
        if (_hoveredEntity is not null && !ReferenceEquals(_hoveredEntity, entity))
            _hoveredEntity.IsHoverFocusPending = false;

        if (ReferenceEquals(_hoveredEntity, entity))
            return;

        _hoveredEntity = entity;
        ApplySelectionHighlights();
    }

    public void SetHoveredRelationByColumn(string entityId, string columnName)
    {
        if (string.IsNullOrWhiteSpace(entityId) || string.IsNullOrWhiteSpace(columnName))
        {
            SetHoveredEdge(null);
            return;
        }

        ErRelationEdgeViewModel? edge = Edges.FirstOrDefault(candidate =>
            (string.Equals(candidate.ChildEntityId, entityId, StringComparison.OrdinalIgnoreCase)
             && candidate.ChildColumns.Any(column => string.Equals(column, columnName, StringComparison.OrdinalIgnoreCase)))
            || (string.Equals(candidate.ParentEntityId, entityId, StringComparison.OrdinalIgnoreCase)
                && candidate.ParentColumns.Any(column => string.Equals(column, columnName, StringComparison.OrdinalIgnoreCase))));
        SetHoveredEdge(edge);
    }

    public void ReplaceContents(ErCanvasViewModel source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _isRebuilding = true;
        IncludeViews = source.IncludeViews;
        _isRebuilding = false;

        Entities.Clear();
        Edges.Clear();
        TechnicalWarnings.Clear();

        foreach (ErEntityNodeViewModel entity in source.Entities)
        {
            entity.Density = SelectedDensity;
            Entities.Add(entity);
        }

        foreach (ErRelationEdgeViewModel edge in source.Edges)
            Edges.Add(edge);

        foreach (string warning in source.TechnicalWarnings)
            TechnicalWarnings.Add(warning);

        ClearSelection();
        RaisePropertyChanged(nameof(EntityCount));
        RaisePropertyChanged(nameof(EdgeCount));
        RaisePropertyChanged(nameof(HasTechnicalWarnings));
        RaisePropertyChanged(nameof(StatusMessage));
        RaiseSelectionDetailPropertiesChanged();
    }

    public void AddTechnicalWarning(string warningCode)
    {
        if (string.IsNullOrWhiteSpace(warningCode))
            return;

        TechnicalWarnings.Add(warningCode.Trim());
    }

    public ErEntityNodeViewModel? FindEntity(string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId))
            return null;

        return Entities.FirstOrDefault(entity =>
            string.Equals(entity.Id, entityId, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<ErRelationEdgeViewModel> GetEdgesForEntity(string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId))
            return [];

        return Edges.Where(edge =>
                !edge.IsHidden
                && (string.Equals(edge.ChildEntityId, entityId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(edge.ParentEntityId, entityId, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    public bool TryFocusRelation(
        string childEntityId,
        string parentEntityId,
        IReadOnlyList<string>? childColumns = null,
        IReadOnlyList<string>? parentColumns = null)
    {
        if (string.IsNullOrWhiteSpace(childEntityId) || string.IsNullOrWhiteSpace(parentEntityId))
            return false;

        ErRelationEdgeViewModel? edge = Edges.FirstOrDefault(candidate =>
            string.Equals(candidate.ChildEntityId, childEntityId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.ParentEntityId, parentEntityId, StringComparison.OrdinalIgnoreCase)
            && MatchesColumns(candidate.ChildColumns, childColumns)
            && MatchesColumns(candidate.ParentColumns, parentColumns));

        if (edge is null)
            return false;

        SelectedEdge = edge;
        RequestViewportFocusForCurrentSelection();
        return true;
    }

    public void RequestViewportFocusForCurrentSelection()
    {
        if (SelectedEdge is not null)
        {
            FocusTargetX = (SelectedEdge.StartX + SelectedEdge.EndX) / 2d;
            FocusTargetY = (SelectedEdge.StartY + SelectedEdge.EndY) / 2d;
            FocusRequestVersion++;
            return;
        }

        if (SelectedEntities.Count > 0)
        {
            double minX = SelectedEntities.Min(entity => entity.X);
            double minY = SelectedEntities.Min(entity => entity.Y);
            double maxX = SelectedEntities.Max(entity => entity.X + EntityWidth);
            double maxY = SelectedEntities.Max(entity => entity.Y + entity.NodeHeight);
            FocusTargetX = (minX + maxX) / 2d;
            FocusTargetY = (minY + maxY) / 2d;
            FocusRequestVersion++;
        }
    }

    public bool TryGetSelectionFrame(double padding, out Rect frame)
    {
        if (SelectedEntities.Count > 0)
        {
            double minX = SelectedEntities.Min(entity => entity.X) - padding;
            double minY = SelectedEntities.Min(entity => entity.Y) - padding;
            double maxX = SelectedEntities.Max(entity => entity.X + EntityWidth) + padding;
            double maxY = SelectedEntities.Max(entity => entity.Y + entity.NodeHeight) + padding;
            frame = new Rect(minX, minY, Math.Max(1d, maxX - minX), Math.Max(1d, maxY - minY));
            return true;
        }

        if (SelectedEdge is not null)
        {
            IReadOnlyList<Point> route = SelectedEdge.RoutePoints;
            if (route.Count == 0)
            {
                frame = default;
                return false;
            }

            double minX = route.Min(point => point.X) - padding;
            double minY = route.Min(point => point.Y) - padding;
            double maxX = route.Max(point => point.X) + padding;
            double maxY = route.Max(point => point.Y) + padding;
            frame = new Rect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
            return true;
        }

        frame = default;
        return false;
    }

    public bool TrySelectEntityInRegion(Rect region)
    {
        ErEntityNodeViewModel? selected = Entities
            .Where(entity => region.Intersects(new Rect(entity.X, entity.Y, EntityWidth, entity.NodeHeight)))
            .OrderBy(entity =>
            {
                double centerX = entity.X + (EntityWidth / 2d);
                double centerY = entity.Y + (entity.NodeHeight / 2d);
                double dx = centerX - region.Center.X;
                double dy = centerY - region.Center.Y;
                return (dx * dx) + (dy * dy);
            })
            .FirstOrDefault();

        if (selected is null)
            return false;

        SelectedEntity = selected;
        return true;
    }

    public bool TrySelectInRegion(Rect region) => TrySelectEntityInRegion(region);

    public void SetViewportSize(double width, double height)
    {
        if (width > 0)
            ViewportWidth = width;

        if (height > 0)
            ViewportHeight = height;

        if (_pendingAutoFit)
        {
            TryConsumePendingAutoFit();
            return;
        }

        if (EntityCount > 0 && width > 0 && height > 0 && ViewportX == 0 && ViewportY == 0 && Zoom == 1.0)
            FitToContents();
    }

    internal void SetIncludeViewsSilently(bool includeViews)
    {
        _isRebuilding = true;
        IncludeViews = includeViews;
        _isRebuilding = false;
    }

    public void ZoomToward(Point screen, double factor)
    {
        double old = Zoom;
        if (old <= 0)
            return;

        Zoom = Math.Clamp(old * factor, 0.15, 4.0);
        PanOffset = new Point(
            screen.X - (screen.X - PanOffset.X) * (Zoom / old),
            screen.Y - (screen.Y - PanOffset.Y) * (Zoom / old));
    }

    public void PanBy(Vector delta)
    {
        PanOffset = PanOffset + delta;
    }

    public void CenterViewportOnCanvasPoint(double canvasX, double canvasY)
    {
        if (ViewportWidth <= 0 || ViewportHeight <= 0)
            return;

        PanOffset = new Point(
            (ViewportWidth / 2d) - (canvasX * Zoom),
            (ViewportHeight / 2d) - (canvasY * Zoom));
    }

    public void FitToContents()
    {
        if (ViewportWidth <= 0 || ViewportHeight <= 0 || Entities.Count == 0)
            return;

        CanvasViewportNodeFrame[] frames = Entities
            .Select(entity => new CanvasViewportNodeFrame(
                entity.X,
                entity.Y,
                EntityWidth,
                entity.NodeHeight))
            .ToArray();
        if (!CanvasViewportMath.TryGetSelectionBounds(frames, out CanvasSelectionBounds bounds))
            return;

        (double zoom, CanvasViewportPoint pan) = CanvasViewportMath.ComputeFit(
            bounds,
            new CanvasViewportSize(ViewportWidth, ViewportHeight),
            padding: 48,
            minZoom: 0.15,
            maxZoom: 2.0);
        Zoom = zoom;
        PanOffset = new Point(pan.X, pan.Y);
    }

    private void OnEntitiesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems is not null)
        {
            foreach (ErEntityNodeViewModel removed in e.OldItems.OfType<ErEntityNodeViewModel>())
            {
                SelectedEntities.Remove(removed);
                if (ReferenceEquals(_hoveredEntity, removed))
                    _hoveredEntity = null;
            }
        }

        RaisePropertyChanged(nameof(EntityCount));
        RaisePropertyChanged(nameof(StatusMessage));
        DeleteSelectionCommand.NotifyCanExecuteChanged();
        SyncToDdlCommand.NotifyCanExecuteChanged();
        AutoLayoutCommand.NotifyCanExecuteChanged();
        FitToScreenCommand.NotifyCanExecuteChanged();
        FindEntityCommand.NotifyCanExecuteChanged();
        EditSelectedDefinitionInSqlEditorCommand.NotifyCanExecuteChanged();
        HideUnrelatedEntitiesCommand.NotifyCanExecuteChanged();
        ShowAllEntitiesCommand.NotifyCanExecuteChanged();
    }

    private void OnEdgesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateRelationPins();
        SyncHiddenEdgesFromEntityVisibility();
        RaisePropertyChanged(nameof(EdgeCount));
        RaisePropertyChanged(nameof(StatusMessage));
        DeleteSelectionCommand.NotifyCanExecuteChanged();
        SyncToDdlCommand.NotifyCanExecuteChanged();
        HideUnrelatedEntitiesCommand.NotifyCanExecuteChanged();
        ShowAllEntitiesCommand.NotifyCanExecuteChanged();
    }

    private void OnTechnicalWarningsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RaisePropertyChanged(nameof(HasTechnicalWarnings));

    private void OnSelectedEntitiesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_selectedEntity is not null && !SelectedEntities.Contains(_selectedEntity))
            SetPrimarySelectedEntity(SelectedEntities.LastOrDefault());

        RaisePropertyChanged(nameof(SelectedEntityCount));
        RaisePropertyChanged(nameof(HasMultipleEntitySelection));
        RaiseSelectionDetailPropertiesChanged();
        EditSelectedDefinitionInSqlEditorCommand.NotifyCanExecuteChanged();
        HideUnrelatedEntitiesCommand.NotifyCanExecuteChanged();
    }

    public void RecomputeEdgeGeometry()
    {
        ErCanvasBuilder.RecomputeEdgeGeometry(this);
    }

    private void ExecuteMutation(ErCanvasMutation mutation)
    {
        _history.Execute(mutation);
        IsDirty = true;
        RefreshNeedsConfirmation = false;
        OnHistoryChanged();
        RecomputeEdgeGeometry();
    }

    private void OnHistoryChanged()
    {
        RaisePropertyChanged(nameof(CanUndo));
        RaisePropertyChanged(nameof(CanRedo));
        RaisePropertyChanged(nameof(StatusMessage));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        SyncToDdlCommand.NotifyCanExecuteChanged();
    }

    private void RequestRefreshFromSourceMetadata()
    {
        if (IsDirty && !RefreshNeedsConfirmation)
        {
            RefreshNeedsConfirmation = true;
            AddTechnicalWarning("W-ER-REFRESH-CONFIRMATION-REQUIRED");
            return;
        }

        RefreshFromSourceMetadata();
    }

    private void RefreshFromSourceMetadata()
    {
        if (_sourceMetadata is null)
        {
            Entities.Clear();
            Edges.Clear();
            TechnicalWarnings.Clear();
            AddTechnicalWarning("W-ER-NO-METADATA");
            _pendingAutoFit = false;
            Zoom = 1.0;
            PanOffset = new Point(0, 0);
            IsDirty = false;
            RefreshNeedsConfirmation = false;
            _history.Clear();
            OnHistoryChanged();
            RaisePropertyChanged(nameof(StatusMessage));
            return;
        }

        ErCanvasViewModel rebuilt = ErCanvasBuilder.Build(_sourceMetadata, IncludeViews);
        rebuilt.BindSourceMetadata(_sourceMetadata, rebuild: false);
        ReplaceContents(rebuilt);
        _pendingAutoFit = true;
        IsDirty = false;
        RefreshNeedsConfirmation = false;
        _history.Clear();
        OnHistoryChanged();
        TryConsumePendingAutoFit();
    }

    private void TryConsumePendingAutoFit()
    {
        if (!_pendingAutoFit || ViewportWidth <= 0 || ViewportHeight <= 0 || EntityCount == 0)
            return;

        _pendingAutoFit = false;
        Zoom = 1.0;
        PanOffset = new Point(0, 0);
        FitToContents();
    }

    private void ApplySelectionHighlights()
    {
        Dictionary<string, HashSet<string>> highlights = new(StringComparer.OrdinalIgnoreCase);
        ErRelationEdgeViewModel? activeEdge = SelectedEdge ?? _hoveredEdge;
        HashSet<ErRelationEdgeViewModel> focusedEdges = [];
        HashSet<string> focusedEntities = new(StringComparer.OrdinalIgnoreCase);

        if (activeEdge is not null)
        {
            foreach (ErRelationEdgeViewModel edge in Edges)
            {
                if (edge.IsHidden)
                    continue;
                edge.IsSelected = SelectedEdge is not null && ReferenceEquals(edge, SelectedEdge);
                edge.VisualState = ReferenceEquals(edge, activeEdge)
                    ? ErVisualState.ConnectedHighlight
                    : ErVisualState.Normal;
            }

            AddColumnHighlights(highlights, activeEdge.ChildEntityId, activeEdge.ChildColumns);
            AddColumnHighlights(highlights, activeEdge.ParentEntityId, activeEdge.ParentColumns);
            focusedEdges.Add(activeEdge);
            focusedEntities.Add(activeEdge.ChildEntityId);
            focusedEntities.Add(activeEdge.ParentEntityId);
        }
        else if (SelectedEntities.Count > 0)
        {
            var selectedIds = new HashSet<string>(
                SelectedEntities.Select(entity => entity.Id),
                StringComparer.OrdinalIgnoreCase);
            HashSet<ErRelationEdgeViewModel> selectedEdges = [.. Edges.Where(edge =>
                selectedIds.Contains(edge.ChildEntityId) || selectedIds.Contains(edge.ParentEntityId))];
            foreach (ErEntityNodeViewModel selected in SelectedEntities)
                focusedEntities.Add(selected.Id);

            foreach (ErRelationEdgeViewModel edge in Edges)
            {
                if (edge.IsHidden)
                    continue;
                bool isSelected = selectedEdges.Contains(edge);
                edge.IsSelected = isSelected;
                edge.VisualState = isSelected ? ErVisualState.ConnectedHighlight : ErVisualState.Normal;
                if (!isSelected)
                    continue;

                AddColumnHighlights(highlights, edge.ChildEntityId, edge.ChildColumns);
                AddColumnHighlights(highlights, edge.ParentEntityId, edge.ParentColumns);
                focusedEdges.Add(edge);
                focusedEntities.Add(edge.ChildEntityId);
                focusedEntities.Add(edge.ParentEntityId);
            }
        }
        else if (_hoveredEntity is not null)
        {
            HashSet<ErRelationEdgeViewModel> hoveredEdges = [.. GetEdgesForEntity(_hoveredEntity.Id)];
            focusedEntities.Add(_hoveredEntity.Id);

            foreach (ErRelationEdgeViewModel edge in Edges)
            {
                if (edge.IsHidden)
                    continue;
                bool isConnected = hoveredEdges.Contains(edge);
                edge.IsSelected = false;
                edge.VisualState = isConnected ? ErVisualState.ConnectedHighlight : ErVisualState.Normal;
                if (!isConnected)
                    continue;

                AddColumnHighlights(highlights, edge.ChildEntityId, edge.ChildColumns);
                AddColumnHighlights(highlights, edge.ParentEntityId, edge.ParentColumns);
                focusedEdges.Add(edge);
                focusedEntities.Add(edge.ChildEntityId);
                focusedEntities.Add(edge.ParentEntityId);
            }

        }
        else
        {
            foreach (ErRelationEdgeViewModel edge in Edges)
            {
                if (edge.IsHidden)
                    continue;
                edge.IsSelected = false;
                edge.VisualState = ErVisualState.Normal;
            }
        }

        foreach (ErEntityNodeViewModel entity in Entities)
        {
            if (!highlights.TryGetValue(entity.Id, out HashSet<string>? columns))
                columns = [];

            entity.HighlightColumns(columns);
            entity.VisualState = columns.Count > 0 ? ErVisualState.ConnectedHighlight : ErVisualState.Normal;
            entity.IsSelected = SelectedEntities.Contains(entity);
            if (entity.IsHidden)
                entity.IsDimmed = true;
        }

        bool shouldDim = IsFocusModeEnabled && (activeEdge is not null || SelectedEntities.Count > 0 || _hoveredEntity is not null);
        foreach (ErRelationEdgeViewModel edge in Edges)
            edge.IsDimmed = shouldDim && !focusedEdges.Contains(edge);

        foreach (ErEntityNodeViewModel entity in Entities)
            entity.IsDimmed = shouldDim && !focusedEntities.Contains(entity.Id);
    }

    private static void AddColumnHighlight(
        IDictionary<string, HashSet<string>> highlights,
        string entityId,
        string columnName)
    {
        if (!highlights.TryGetValue(entityId, out HashSet<string>? columns))
        {
            columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            highlights[entityId] = columns;
        }

        columns.Add(columnName);
    }

    private static void AddColumnHighlights(
        IDictionary<string, HashSet<string>> highlights,
        string entityId,
        IReadOnlyList<string> columnNames)
    {
        foreach (string columnName in columnNames)
            AddColumnHighlight(highlights, entityId, columnName);
    }

    private void RaiseSelectionDetailPropertiesChanged()
    {
        RaisePropertyChanged(nameof(HasSelectionDetails));
        RaisePropertyChanged(nameof(HasSingleEntitySelection));
        RaisePropertyChanged(nameof(HasMultipleEntitySelection));
        RaisePropertyChanged(nameof(CanEditSelectedViewDefinition));
        RaisePropertyChanged(nameof(SelectedEntityCount));
        RaisePropertyChanged(nameof(HasSelectedEntityMetadata));
        RaisePropertyChanged(nameof(SelectedEntityQualifiedName));
        RaisePropertyChanged(nameof(SelectedEntityObjectType));
        RaisePropertyChanged(nameof(SelectedEntityStats));
        RaisePropertyChanged(nameof(SelectedEntityNullabilityStats));
        RaisePropertyChanged(nameof(SelectedEntityRelationshipStats));
        RaisePropertyChanged(nameof(SelectedEntityPrimaryKeyColumns));
        RaisePropertyChanged(nameof(SelectedEntityForeignKeyColumns));
        RaisePropertyChanged(nameof(SelectedEntityIndexedColumns));
        RaisePropertyChanged(nameof(SelectedEntityColumnCount));
        RaisePropertyChanged(nameof(SelectedEntityPrimaryKeyCount));
        RaisePropertyChanged(nameof(SelectedEntityForeignKeyCount));
        RaisePropertyChanged(nameof(HasSelectedEntityPrimaryKeys));
        RaisePropertyChanged(nameof(HasSelectedEntityForeignKeys));
        RaisePropertyChanged(nameof(HasSelectedEntityIndexes));
        RaisePropertyChanged(nameof(SelectionTitle));
        RaisePropertyChanged(nameof(SelectionSubtitle));
        RaisePropertyChanged(nameof(SelectionBody));
        RaisePropertyChanged(nameof(SelectionJoinPredicate));
        RaisePropertyChanged(nameof(HasSelectionJoinPredicate));
        DeleteSelectionCommand.NotifyCanExecuteChanged();
        OpenSelectionInQueryCommand.NotifyCanExecuteChanged();
        EditSelectedDefinitionInSqlEditorCommand.NotifyCanExecuteChanged();
        HideUnrelatedEntitiesCommand.NotifyCanExecuteChanged();
        ShowAllEntitiesCommand.NotifyCanExecuteChanged();
    }

    private bool CanHideUnrelatedEntities() => SelectedEdge is not null || SelectedEntities.Count > 0;

    private void HideUnrelatedEntities()
    {
        if (!CanHideUnrelatedEntities())
            return;

        HashSet<ErRelationEdgeViewModel> seedEdges = [];
        if (SelectedEdge is not null)
        {
            seedEdges.Add(SelectedEdge);
        }
        else
        {
            var selectedIds = new HashSet<string>(
                SelectedEntities.Select(entity => entity.Id),
                StringComparer.OrdinalIgnoreCase);
            foreach (ErRelationEdgeViewModel edge in Edges)
            {
                if (selectedIds.Contains(edge.ChildEntityId) || selectedIds.Contains(edge.ParentEntityId))
                    seedEdges.Add(edge);
            }
        }

        var keepEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ErEntityNodeViewModel selected in SelectedEntities)
            keepEntities.Add(selected.Id);
        foreach (ErRelationEdgeViewModel edge in seedEdges)
        {
            keepEntities.Add(edge.ChildEntityId);
            keepEntities.Add(edge.ParentEntityId);
        }

        foreach (ErEntityNodeViewModel entity in Entities)
            entity.IsHidden = !keepEntities.Contains(entity.Id);

        SyncHiddenEdgesFromEntityVisibility();
        ApplySelectionHighlights();
        RaiseSelectionDetailPropertiesChanged();
        ShowAllEntitiesCommand.NotifyCanExecuteChanged();
    }

    private bool CanShowAllEntities() =>
        Entities.Any(static entity => entity.IsHidden) || Edges.Any(static edge => edge.IsHidden);

    private void ShowAllEntities()
    {
        foreach (ErEntityNodeViewModel entity in Entities)
            entity.IsHidden = false;

        foreach (ErRelationEdgeViewModel edge in Edges)
            edge.IsHidden = false;

        ApplySelectionHighlights();
        ShowAllEntitiesCommand.NotifyCanExecuteChanged();
    }

    private bool CanSyncToDdl() => _syncToDdl is not null && Entities.Count > 0;

    private void SyncToDdl()
    {
        if (_syncToDdl is null)
            return;

        var request = new ErCanvasSyncRequest(
            Entities: [.. Entities],
            Edges: [.. Edges]);

        if (!_syncToDdl(request))
            return;

        IsDirty = false;
        RefreshNeedsConfirmation = false;
        RaisePropertyChanged(nameof(StatusMessage));
        SyncToDdlCommand.NotifyCanExecuteChanged();
    }

    private bool CanFindEntity() =>
        !string.IsNullOrWhiteSpace(SearchTerm) && Entities.Count > 0;

    private void FindAndSelectEntity()
    {
        if (string.IsNullOrWhiteSpace(SearchTerm))
            return;

        ErEntityNodeViewModel? entity = Entities.FirstOrDefault(candidate =>
            candidate.DisplayName.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
            || candidate.DisplaySchema.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
            || candidate.Id.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
            return;

        SelectedEntity = entity;
        RequestViewportFocusForCurrentSelection();
    }

    private bool CanAutoLayout() => Entities.Count > 1;

    private void ApplyAutoLayout()
    {
        var previous = Entities
            .ToDictionary(
                static entity => entity.Id,
                static entity => new Point(entity.X, entity.Y),
                StringComparer.OrdinalIgnoreCase);

        ExecuteMutation(new ErCanvasMutation(
            Description: "ER: auto layout",
            Execute: () =>
            {
                ErCanvasBuilder.AutoLayout(this);
            },
            Undo: () =>
            {
                foreach (ErEntityNodeViewModel entity in Entities)
                {
                    if (previous.TryGetValue(entity.Id, out Point point))
                    {
                        entity.X = point.X;
                        entity.Y = point.Y;
                    }
                }
                RecomputeEdgeGeometry();
            }));
    }

    private void ClearEntitySelection()
    {
        if (SelectedEntities.Count == 0 && _selectedEntity is null)
            return;

        ClearEntitySelectionCore();
        SetHoveredEdge(null);
        ApplySelectionHighlights();
        RaiseSelectionDetailPropertiesChanged();
    }

    private void ClearEntitySelectionCore()
    {
        foreach (ErEntityNodeViewModel selected in SelectedEntities)
            selected.IsSelected = false;

        SelectedEntities.Clear();
        SetPrimarySelectedEntity(null);
    }

    private void SetSingleEntitySelection(ErEntityNodeViewModel entity)
    {
        if (SelectedEdge is not null)
            SelectedEdge = null;

        if (SelectedEntities.Count == 1 && ReferenceEquals(SelectedEntities[0], entity))
            return;

        SetEntitiesSelection([entity]);
    }

    private void SetEntitiesSelection(IReadOnlyList<ErEntityNodeViewModel> entities)
    {
        foreach (ErEntityNodeViewModel current in SelectedEntities)
            current.IsSelected = false;

        SelectedEntities.Clear();
        foreach (ErEntityNodeViewModel entity in entities.Distinct())
            SelectedEntities.Add(entity);

        SetPrimarySelectedEntity(SelectedEntities.LastOrDefault());
        SetHoveredEdge(null);
        ApplySelectionHighlights();
        RaiseSelectionDetailPropertiesChanged();
    }

    private void SetPrimarySelectedEntity(ErEntityNodeViewModel? entity)
    {
        if (ReferenceEquals(_selectedEntity, entity))
            return;

        _selectedEntity = entity;
        RaisePropertyChanged(nameof(SelectedEntity));
    }

    private void UpdateRelationPins()
    {
        foreach (ErEntityNodeViewModel entity in Entities)
        {
            foreach (ErColumnRowViewModel column in entity.Columns)
            {
                column.HasInboundRelation = false;
                column.HasOutboundRelation = false;
            }
        }

        Dictionary<string, ErEntityNodeViewModel> entityById = Entities.ToDictionary(entity => entity.Id, StringComparer.OrdinalIgnoreCase);
        foreach (ErRelationEdgeViewModel edge in Edges)
        {
            if (entityById.TryGetValue(edge.ChildEntityId, out ErEntityNodeViewModel? child))
            {
                foreach (string columnName in edge.ChildColumns)
                {
                    ErColumnRowViewModel? column = child.Columns.FirstOrDefault(candidate =>
                        string.Equals(candidate.ColumnName, columnName, StringComparison.OrdinalIgnoreCase));
                    if (column is not null)
                        column.HasOutboundRelation = true;
                }
            }

            if (entityById.TryGetValue(edge.ParentEntityId, out ErEntityNodeViewModel? parent))
            {
                foreach (string columnName in edge.ParentColumns)
                {
                    ErColumnRowViewModel? column = parent.Columns.FirstOrDefault(candidate =>
                        string.Equals(candidate.ColumnName, columnName, StringComparison.OrdinalIgnoreCase));
                    if (column is not null)
                        column.HasInboundRelation = true;
                }
            }
        }
    }

    private void SyncHiddenEdgesFromEntityVisibility()
    {
        Dictionary<string, ErEntityNodeViewModel> entityById = Entities.ToDictionary(entity => entity.Id, StringComparer.OrdinalIgnoreCase);
        foreach (ErRelationEdgeViewModel edge in Edges)
        {
            bool childHidden = entityById.TryGetValue(edge.ChildEntityId, out ErEntityNodeViewModel? child) && child.IsHidden;
            bool parentHidden = entityById.TryGetValue(edge.ParentEntityId, out ErEntityNodeViewModel? parent) && parent.IsHidden;
            edge.IsHidden = childHidden || parentHidden;
        }
    }

    private int GetOutboundRelationCount(ErEntityNodeViewModel entity) =>
        Edges.Count(edge => string.Equals(edge.ChildEntityId, entity.Id, StringComparison.OrdinalIgnoreCase));

    private int GetInboundRelationCount(ErEntityNodeViewModel entity) =>
        Edges.Count(edge => string.Equals(edge.ParentEntityId, entity.Id, StringComparison.OrdinalIgnoreCase));

    private static string JoinColumnsOrDash(IEnumerable<string> columns)
    {
        string[] values = columns
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return values.Length == 0 ? "-" : string.Join(", ", values);
    }

    private string BuildMultiSelectionSummary()
    {
        if (SelectedEntities.Count == 0)
            return string.Empty;

        int totalColumns = SelectedEntities.Sum(entity => entity.ColumnCount);
        int totalRelationships = Edges.Count(edge =>
            SelectedEntities.Any(entity =>
                string.Equals(entity.Id, edge.ChildEntityId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entity.Id, edge.ParentEntityId, StringComparison.OrdinalIgnoreCase)));
        return $"{SelectedEntities.Count} entidades | {totalColumns} colunas | {totalRelationships} relacoes visiveis";
    }

    private bool CanEditSelectedDefinitionInSqlEditor() =>
        SelectedEntityCount == 1
        && SelectedEntity?.IsView == true
        && _openEntityDefinitionInSqlEditor is not null;

    private void EditSelectedDefinitionInSqlEditor()
    {
        if (!CanEditSelectedDefinitionInSqlEditor() || SelectedEntity is null)
            return;

        _openEntityDefinitionInSqlEditor?.Invoke(SelectedEntity);
    }

    private static bool MatchesColumns(IReadOnlyList<string> candidate, IReadOnlyList<string>? expected)
    {
        if (expected is null || expected.Count == 0)
            return true;

        if (candidate.Count != expected.Count)
            return false;

        for (int i = 0; i < candidate.Count; i++)
        {
            if (!string.Equals(candidate[i], expected[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private bool CanOpenSelectionInQuery() => SelectedEdge is not null && _openSelectionInQuery is not null;

    private void OpenSelectionInQuery()
    {
        if (SelectedEdge is null || _openSelectionInQuery is null)
            return;

        _openSelectionInQuery(SelectedEdge);
    }
}

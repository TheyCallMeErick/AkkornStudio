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
    private ErEntityNodeViewModel? _selectedEntity;
    private ErRelationEdgeViewModel? _selectedEdge;
    private ErRelationEdgeViewModel? _hoveredEdge;
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

        Entities.CollectionChanged += OnEntitiesChanged;
        Edges.CollectionChanged += OnEdgesChanged;
        TechnicalWarnings.CollectionChanged += OnTechnicalWarningsChanged;
    }

    public ObservableCollection<ErEntityNodeViewModel> Entities { get; }

    public ObservableCollection<ErRelationEdgeViewModel> Edges { get; }

    public ObservableCollection<string> TechnicalWarnings { get; }

    public ErEntityNodeViewModel? SelectedEntity
    {
        get => _selectedEntity;
        set
        {
            if (ReferenceEquals(_selectedEntity, value))
                return;

            if (_selectedEntity is not null)
                _selectedEntity.IsSelected = false;

            if (!Set(ref _selectedEntity, value))
                return;

            if (_selectedEntity is not null)
                _selectedEntity.IsSelected = true;

            if (value is not null)
            {
                if (_selectedEdge is not null)
                    _selectedEdge.IsSelected = false;

                _selectedEdge = null;
                RaisePropertyChanged(nameof(SelectedEdge));
            }

            SetHoveredEdge(null);

            ApplySelectionHighlights();
            RaiseSelectionDetailPropertiesChanged();
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
                if (_selectedEntity is not null)
                    _selectedEntity.IsSelected = false;

                _selectedEntity = null;
                RaisePropertyChanged(nameof(SelectedEntity));
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

    public string ZoomDisplay => $"{Zoom * 100:0}%";

    public bool HasTechnicalWarnings => TechnicalWarnings.Count > 0;

    public bool HasSelectionDetails => SelectedEntity is not null || SelectedEdge is not null;

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
            : SelectedEntity is not null
                ? SelectedEntity.DisplayName
                : "Nada selecionado";

    public string SelectionSubtitle =>
        SelectedEdge is not null
            ? $"{SelectedEdge.ChildEntityId} -> {SelectedEdge.ParentEntityId}"
            : SelectedEntity is not null
                ? (SelectedEntity.IsView ? "View" : "Tabela")
                : "Selecione uma entidade ou relacionamento para ver os detalhes.";

    public string SelectionBody =>
        SelectedEdge is not null
            ? $"Mapeamento: {SelectedEdge.MappingSummary}"
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

        if (SelectedEntity is null)
            return false;

        ErEntityNodeViewModel entity = SelectedEntity;
        List<ErRelationEdgeViewModel> attachedEdges = [.. GetEdgesForEntity(entity.Id)];
        int originalIndex = Entities.IndexOf(entity);
        ExecuteMutation(new ErCanvasMutation(
            Description: "ER: remover tabela",
            Execute: () =>
            {
                foreach (ErRelationEdgeViewModel edge in attachedEdges)
                    Edges.Remove(edge);
                Entities.Remove(entity);
                ClearSelection();
                RecomputeEdgeGeometry();
            },
            Undo: () =>
            {
                int restoreIndex = originalIndex < 0 ? Entities.Count : Math.Min(originalIndex, Entities.Count);
                if (!Entities.Contains(entity))
                    Entities.Insert(restoreIndex, entity);
                foreach (ErRelationEdgeViewModel edge in attachedEdges)
                {
                    if (!Edges.Contains(edge))
                        Edges.Add(edge);
                }
                SelectedEntity = entity;
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
        if (SelectedEntity is not null)
            SelectedEntity.IsSelected = false;

        SetHoveredEdge(null);
        SelectedEntity = null;
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
                string.Equals(edge.ChildEntityId, entityId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(edge.ParentEntityId, entityId, StringComparison.OrdinalIgnoreCase))
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

        if (SelectedEntity is not null)
        {
            FocusTargetX = SelectedEntity.X + (EntityWidth / 2d);
            FocusTargetY = SelectedEntity.Y + SelectedEntity.NodeHeight / 2d;
            FocusRequestVersion++;
        }
    }

    public bool TryGetSelectionFrame(double padding, out Rect frame)
    {
        if (SelectedEntity is not null)
        {
            frame = new Rect(
                SelectedEntity.X - padding,
                SelectedEntity.Y - padding,
                EntityWidth + (padding * 2d),
                SelectedEntity.NodeHeight + (padding * 2d));
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
        RaisePropertyChanged(nameof(EntityCount));
        RaisePropertyChanged(nameof(StatusMessage));
        DeleteSelectionCommand.NotifyCanExecuteChanged();
        SyncToDdlCommand.NotifyCanExecuteChanged();
        AutoLayoutCommand.NotifyCanExecuteChanged();
        FitToScreenCommand.NotifyCanExecuteChanged();
        FindEntityCommand.NotifyCanExecuteChanged();
    }

    private void OnEdgesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(EdgeCount));
        RaisePropertyChanged(nameof(StatusMessage));
        DeleteSelectionCommand.NotifyCanExecuteChanged();
        SyncToDdlCommand.NotifyCanExecuteChanged();
    }

    private void OnTechnicalWarningsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RaisePropertyChanged(nameof(HasTechnicalWarnings));

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
        else if (SelectedEntity is not null)
        {
            HashSet<ErRelationEdgeViewModel> selectedEdges = [.. GetEdgesForEntity(SelectedEntity.Id)];
            focusedEntities.Add(SelectedEntity.Id);
            foreach (ErRelationEdgeViewModel edge in Edges)
            {
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
        else
        {
            foreach (ErRelationEdgeViewModel edge in Edges)
            {
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
            if (ReferenceEquals(entity, SelectedEntity))
                entity.IsSelected = true;
        }

        bool shouldDim = IsFocusModeEnabled && (activeEdge is not null || SelectedEntity is not null);
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
        RaisePropertyChanged(nameof(SelectionTitle));
        RaisePropertyChanged(nameof(SelectionSubtitle));
        RaisePropertyChanged(nameof(SelectionBody));
        RaisePropertyChanged(nameof(SelectionJoinPredicate));
        RaisePropertyChanged(nameof(HasSelectionJoinPredicate));
        DeleteSelectionCommand.NotifyCanExecuteChanged();
        OpenSelectionInQueryCommand.NotifyCanExecuteChanged();
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

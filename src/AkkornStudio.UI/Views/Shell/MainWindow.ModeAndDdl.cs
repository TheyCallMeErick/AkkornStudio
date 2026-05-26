using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Material.Icons;
using Microsoft.Extensions.DependencyInjection;
using AkkornStudio.Core;
using AkkornStudio.UI.Controls;
using AkkornStudio.UI.Controls.Ddl;
using AkkornStudio.Metadata;
using AkkornStudio.UI.Controls.Shell;
using AkkornStudio.UI.Services.Ddl;
using AkkornStudio.UI.Services.Workspace.Models;
using AkkornStudio.UI.Services.Workspace.Preview;
using AkkornStudio.UI.ViewModels;
using AkkornStudio.UI.ViewModels.Canvas.Strategies;

namespace AkkornStudio.UI;

public partial class MainWindow
{
    private static readonly Thickness FloatingSidebarMargin = new(8);
    private static readonly CornerRadius FloatingSidebarCornerRadius = new(12);

    private void WireModeToggle()
    {
        _shellPropertyChangedHandler = (_, e) =>
        {
            if (e.PropertyName is nameof(ShellViewModel.ActiveWorkspaceDocumentType)
                or nameof(ShellViewModel.ActivePageContract)
                or nameof(ShellViewModel.IsQueryDocumentPageActive)
                or nameof(ShellViewModel.IsDdlDocumentPageActive)
                or nameof(ShellViewModel.IsSqlEditorDocumentPageActive)
                or nameof(ShellViewModel.IsErDiagramDocumentPageActive)
                or nameof(ShellViewModel.ActiveCanvasContext))
            {
                SyncModeToggleState();
                SyncCanvasContext();
                UpdatePreviewDockLayout();
            }
        };

        CurrentShell.PropertyChanged += _shellPropertyChangedHandler;
        SyncModeToggleState();
    }

    private void SyncModeToggleState()
    {
        Button? queryModeBtn = this.FindControl<Button>("QueryModeBtn");
        Button? ddlModeBtn = this.FindControl<Button>("DdlModeBtn");
        Button? sqlEditorModeBtn = this.FindControl<Button>("SqlEditorModeBtn");
        Button? erDiagramModeBtn = this.FindControl<Button>("ErDiagramModeBtn");

        if (queryModeBtn is not null)
        {
            queryModeBtn.IsEnabled = true;
            queryModeBtn.Classes.Set("active", CurrentShell.IsQueryDocumentPageActive);
        }

        if (ddlModeBtn is not null)
        {
            ddlModeBtn.IsEnabled = true;
            ddlModeBtn.Classes.Set("active", CurrentShell.IsDdlDocumentPageActive);
        }

        if (sqlEditorModeBtn is not null)
        {
            sqlEditorModeBtn.IsEnabled = true;
            sqlEditorModeBtn.Classes.Set("active", CurrentShell.IsSqlEditorDocumentPageActive);
        }

        if (erDiagramModeBtn is not null)
        {
            erDiagramModeBtn.IsEnabled = true;
            erDiagramModeBtn.Classes.Set("active", CurrentShell.IsErDiagramDocumentPageActive);
        }

        SyncSidebarChromeForActivePage();
    }

    private void SyncSidebarChromeForActivePage()
    {
        if (CurrentShell.ActivePageContract.CanCollapseSidebars)
        {
            SyncDiagramPageSidebars();
            return;
        }

        SyncFixedPageSidebars();
    }

    private void SyncDiagramPageSidebars()
    {
        Border? leftHost = this.FindControl<Border>("LeftSidebarHost");
        Border? rightHost = this.FindControl<Border>("RightSidebarHost");

        if (leftHost is not null)
        {
            leftHost.Margin = FloatingSidebarMargin;
            leftHost.CornerRadius = FloatingSidebarCornerRadius;
        }

        if (rightHost is not null)
        {
            rightHost.Margin = FloatingSidebarMargin;
            rightHost.CornerRadius = FloatingSidebarCornerRadius;
        }

        bool leftCollapsed = CurrentShell.IsDdlDocumentPageActive
            ? _ddlModeLeftSidebarCollapsed
            : _queryModeLeftSidebarCollapsed;

        bool rightCollapsed = CurrentShell.IsDdlDocumentPageActive
            ? _ddlModeRightSidebarCollapsed
            : _queryModeRightSidebarCollapsed;

        if (CurrentShell.ActivePageContract.ShowsDiagramSidebar && CurrentShell.LeftSidebar.IsVisible)
            SetLeftSidebarCollapsed(leftCollapsed);
        else
            HideLeftSidebarForMode();

        if (CurrentShell.ActivePageContract.ShowsDiagramSidebar && CurrentShell.RightSidebar.IsVisible)
            SetRightSidebarCollapsed(rightCollapsed);
        else
            HideRightSidebarForMode();
    }

    private void SyncFixedPageSidebars()
    {
        Border? leftHost = this.FindControl<Border>("LeftSidebarHost");
        Border? rightHost = this.FindControl<Border>("RightSidebarHost");
        Button? leftHideBtn = this.FindControl<Button>("LeftSidebarHideBtn");
        Button? rightHideBtn = this.FindControl<Button>("RightSidebarHideBtn");

        if (!CurrentShell.ActivePageContract.ShowsDiagramSidebar
            && !CurrentShell.ActivePageContract.ShowsSqlEditorSidebar)
        {
            HideLeftSidebarForMode();
            HideRightSidebarForMode();
            return;
        }

        SetLeftSidebarCollapsed(false);
        SetRightSidebarCollapsed(false);

        if (leftHost is not null)
        {
            leftHost.Margin = FloatingSidebarMargin;
            leftHost.CornerRadius = FloatingSidebarCornerRadius;
        }

        if (rightHost is not null)
        {
            rightHost.Margin = FloatingSidebarMargin;
            rightHost.CornerRadius = FloatingSidebarCornerRadius;
        }

        if (leftHideBtn is not null)
            leftHideBtn.IsVisible = false;

        if (rightHideBtn is not null)
            rightHideBtn.IsVisible = false;
    }

    private void HideLeftSidebarForMode()
    {
        Grid? bodyGrid = this.FindControl<Grid>("BodyGrid");
        Border? host = this.FindControl<Border>("LeftSidebarHost");
        GridSplitter? splitter = this.FindControl<GridSplitter>("LeftSplitter");
        Button? hideBtn = this.FindControl<Button>("LeftSidebarHideBtn");

        if (bodyGrid is null || bodyGrid.ColumnDefinitions.Count < 2)
            return;

        ColumnDefinition sidebarColumn = bodyGrid.ColumnDefinitions[0];
        ColumnDefinition splitterColumn = bodyGrid.ColumnDefinitions[1];
        sidebarColumn.MinWidth = 0;
        sidebarColumn.MaxWidth = 0;
        sidebarColumn.Width = new GridLength(0);
        splitterColumn.Width = new GridLength(0);

        if (host is not null)
            host.IsVisible = false;

        if (splitter is not null)
            splitter.IsVisible = false;

        if (hideBtn is not null)
            hideBtn.IsVisible = false;
    }

    private void HideRightSidebarForMode()
    {
        Grid? bodyGrid = this.FindControl<Grid>("BodyGrid");
        Border? host = this.FindControl<Border>("RightSidebarHost");
        GridSplitter? splitter = this.FindControl<GridSplitter>("RightSplitter");
        Button? hideBtn = this.FindControl<Button>("RightSidebarHideBtn");

        if (bodyGrid is null || bodyGrid.ColumnDefinitions.Count < 5)
            return;

        ColumnDefinition splitterColumn = bodyGrid.ColumnDefinitions[3];
        ColumnDefinition sidebarColumn = bodyGrid.ColumnDefinitions[4];
        sidebarColumn.MinWidth = 0;
        sidebarColumn.MaxWidth = 0;
        sidebarColumn.Width = new GridLength(0);
        splitterColumn.Width = new GridLength(0);

        if (host is not null)
            host.IsVisible = false;

        if (splitter is not null)
            splitter.IsVisible = false;

        if (hideBtn is not null)
            hideBtn.IsVisible = false;
    }

    private void QueryModeBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        CurrentShell.ActivateDocument(WorkspaceDocumentType.QueryCanvas);
        SyncModeToggleState();
        TrackCriticalFlow("CF-02-navigate-shell", "switch_to_query_mode", "ok");
        e.Handled = true;
    }

    private void DdlModeBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        CurrentShell.ActivateDocument(WorkspaceDocumentType.DdlCanvas);
        SyncModeToggleState();
        TrackCriticalFlow("CF-02-navigate-shell", "switch_to_ddl_mode", "ok");
        e.Handled = true;
    }

    private void SqlEditorModeBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        CurrentShell.ActivateDocument(WorkspaceDocumentType.SqlEditor);
        SyncModeToggleState();
        TrackCriticalFlow("CF-02-navigate-shell", "switch_to_sql_editor_mode", "ok");
        e.Handled = true;
    }

    private void ErDiagramModeBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        CurrentShell.ActivateDocument(WorkspaceDocumentType.ErDiagram);
        SyncModeToggleState();
        TrackCriticalFlow("CF-02-navigate-shell", "switch_to_er_diagram_mode", "ok");
        e.Handled = true;
    }

    private void WireHeaderMenus()
    {
        AppHeaderBar? canvasHeader = this.FindControl<AppHeaderBar>("CanvasHeader");
        if (canvasHeader is not null)
            canvasHeader.TitleMenuRequested += (_, _) => OpenTitleMenu(canvasHeader);
    }

    private void OpenTitleMenu(Control anchor)
    {
        _titleMenu ??= BuildTitleMenu();
        _titleMenu.Open(anchor);
    }

    private ContextMenu BuildTitleMenu()
    {
        MenuItem NewItem(string header, MaterialIconKind icon, Action onClick)
        {
            var item = new MenuItem
            {
                Header = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new Material.Icons.Avalonia.MaterialIcon
                        {
                            Kind = icon,
                            Width = 14,
                            Height = 14,
                        },
                        new TextBlock
                        {
                            Text = header,
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                    },
                },
            };
            item.Classes.Add("app-title-menu-item");
            item.Click += (_, _) => onClick();
            return item;
        }

        MenuItem NewGroupHeader(string header)
        {
            var item = new MenuItem
            {
                Header = new TextBlock
                {
                    Text = header,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                IsEnabled = false,
            };
            item.Classes.Add("app-title-menu-group");
            return item;
        }

        MenuItem NewDisabledItem(string header, MaterialIconKind icon, string disabledReason)
        {
            var item = NewItem(header, icon, () => { });
            item.IsEnabled = false;
            ToolTip.SetTip(item, disabledReason);
            return item;
        }

        Separator NewSeparator()
        {
            var separator = new Separator();
            separator.Classes.Add("app-title-menu-sep");
            return separator;
        }

        bool isDdlModeActive = CurrentShell.IsDdlDocumentPageActive;
        string ddlModeOnlyReason = L("menu.reason.ddlOnly", "Disponivel apenas no modo DDL.");
        var items = new List<object>
        {
            NewGroupHeader(L("menu.group.project", "Projeto")),
            NewItem(L("menu.newDiagram", "Novo diagrama"), MaterialIconKind.FileOutline, () =>
            {
                EnterCanvasMode();
                ResetCurrentCanvas();
            }),
            NewItem(L("menu.openFile", "Abrir arquivo"), MaterialIconKind.FolderOpenOutline, () =>
            {
                EnterCanvasMode();
                _ = _fileOps?.OpenAsync();
            }),
            NewItem(L("menu.save", "Salvar"), MaterialIconKind.ContentSave, () =>
            {
                EnterCanvasMode();
                _ = _fileOps?.SaveAsync();
            }),
            NewItem(L("menu.fileHistory", "Histórico de arquivos"), MaterialIconKind.History, () =>
            {
                EnterCanvasMode();
                ResolveActiveDiagramCanvasStrict().FileHistory.Open();
            }),
            NewSeparator(),
            NewGroupHeader(L("menu.group.currentMode", "Modo Atual")),
            NewItem(L("menu.shortcuts", "Atalhos de teclado"), MaterialIconKind.Keyboard, () => new KeyboardShortcutsWindow().Show(this)),
            NewItem(L("menu.settings", "Configurações"), MaterialIconKind.CogOutline, () =>
            {
                PopulateSettingsThemeJsonEditor();
                OpenSettings(keepStartVisible: false);
            }),
            NewItem(L("menu.importSqlQuery", "Importar SQL para Query"), MaterialIconKind.CodeBrackets, () =>
            {
                _ = ImportSqlToQuerySafeAsync();
            }),
            NewItem("Diagnóstico de estrutura", MaterialIconKind.Radar, () =>
            {
                EnterCanvasMode();
                CurrentShell.OpenNewDocument(WorkspaceDocumentType.DdlSchemaAnalysis);
                CurrentShell.ActivateDocument(WorkspaceDocumentType.DdlSchemaAnalysis);
                SyncModeToggleState();
            }),
            NewSeparator(),
            NewGroupHeader(L("menu.group.tools", "Ferramentas")),
            NewItem("Exportar baseline de observabilidade (7 dias)", MaterialIconKind.FileOutline, () =>
            {
                ExportObservabilityBaselineSafe(7);
            }),
            NewItem("Exportar baseline de observabilidade (14 dias)", MaterialIconKind.FileOutline, () =>
            {
                ExportObservabilityBaselineSafe(14);
            }),
            NewItem("Exportar baseline de observabilidade (30 dias)", MaterialIconKind.FileOutline, () =>
            {
                ExportObservabilityBaselineSafe(30);
            }),
        };

        if (isDdlModeActive)
        {
            items.Add(NewItem(L("menu.importDdlSchema", "Importar Schema DDL"), MaterialIconKind.DatabaseImportOutline, () =>
            {
                _ = ImportDdlSchemaSafeAsync();
            }));
            items.Add(NewItem(L("menu.viewDdlSql", "Ver SQL DDL"), MaterialIconKind.CodeBraces, () =>
            {
                _ = ViewDdlSqlSafeAsync();
            }));
            items.Add(NewItem(L("menu.executeDdl", "Executar DDL"), MaterialIconKind.PlayCircleOutline, () =>
            {
                _ = ExecuteDdlSafeAsync();
            }));
        }
        else
        {
            items.Add(NewDisabledItem(L("menu.importDdlSchema", "Importar Schema DDL"), MaterialIconKind.DatabaseImportOutline, ddlModeOnlyReason));
            items.Add(NewDisabledItem(L("menu.viewDdlSql", "Ver SQL DDL"), MaterialIconKind.CodeBraces, ddlModeOnlyReason));
            items.Add(NewDisabledItem(L("menu.executeDdl", "Executar DDL"), MaterialIconKind.PlayCircleOutline, ddlModeOnlyReason));
        }

        items.Add(NewSeparator());
        items.Add(NewItem(L("menu.backToStart", "Voltar para início"), MaterialIconKind.Home, () =>
        {
            if (!_canvasInitialized)
                return;

            CurrentShell.StartMenu.RefreshData(
                ResolveDiagramCanvasForConnectionSidebar().ConnectionManager.Profiles,
                ResolveDiagramCanvasForConnectionSidebar().ConnectionManager.ActiveProfileId
            );
            CurrentShell.ReturnToStart();
            Title = AppConstants.AppDisplayName;
        }));

        return new ContextMenu
        {
            Classes = { "app-title-menu" },
            ItemsSource = items,
        };
    }

    private async Task ExecuteDdlSafeAsync()
    {
        try
        {
            await ExecuteDdlAsync();
        }
        catch (Exception ex)
        {
            CurrentShell.Toasts.ShowError(L("toast.ddlExecuteFailed", "Falha ao executar DDL."), ex.Message);
        }
    }

    private async Task ViewDdlSqlSafeAsync()
    {
        try
        {
            await ViewDdlSqlAsync();
        }
        catch (Exception ex)
        {
            CurrentShell.Toasts.ShowError(L("toast.ddlOpenFailed", "Falha ao abrir SQL DDL."), ex.Message);
        }
    }

    private async Task ImportDdlSchemaSafeAsync()
    {
        try
        {
            await ImportDdlSchemaAsync();
        }
        catch (Exception ex)
        {
            CurrentShell.Toasts.ShowError(L("toast.ddlImportFailed", "Falha ao importar schema para DDL."), ex.Message);
        }
    }

    private async Task ImportSqlToQuerySafeAsync()
    {
        try
        {
            await ImportSqlToQueryAsync();
        }
        catch (Exception ex)
        {
            CurrentShell.Toasts.ShowError(L("toast.queryImportSqlFailed", "Falha ao abrir importação SQL no modo Query."), ex.Message);
        }
    }

    private Task ImportSqlToQueryAsync()
    {
        if (!CurrentShell.IsQueryDocumentPageActive)
        {
            CurrentShell.Toasts.ShowWarning(L("toast.switchToQueryForSqlImport", "Alterne para o modo Query para importar SQL em grafo."));
            return Task.CompletedTask;
        }

        CanvasViewModel queryCanvas = CurrentShell.ActiveQueryCanvasDocument
            ?? CurrentShell.EnsureCanvas();

        queryCanvas.SqlImporter.Open();
        return Task.CompletedTask;
    }

    private Task ImportDdlSchemaAsync()
    {
        if (!CurrentShell.IsDdlDocumentPageActive)
        {
            CurrentShell.Toasts.ShowWarning(L("toast.switchToDdlForSchemaImport", "Alterne para o modo DDL para importar schema."));
            return Task.CompletedTask;
        }

        DbMetadata? metadata = ResolveMetadataForActiveWorkspace();
        if (metadata is null)
        {
            CurrentShell.Toasts.ShowWarning(L("toast.ddlConnectToImportSchema", "Conecte-se a um banco para importar schema no canvas DDL."));
            return Task.CompletedTask;
        }

        return ImportDdlSchemaAsyncCore(metadata);
    }

    private async Task ImportDdlSchemaAsyncCore(DbMetadata metadata)
    {
        CanvasViewModel ddlCanvas = CurrentShell.EnsureDdlCanvas();
        DatabaseProvider activeProvider = ResolveActiveConnectionConfigForWorkspace()?.Provider ?? ddlCanvas.Provider;

        // Build import graph off the visible canvas to keep UI responsive on large schemas.
        DdlSchemaImportPayload payload = await Task.Run(() =>
        {
            var scratch = new CanvasViewModel(null, null, null, null, new DdlDomainStrategy())
            {
                Provider = activeProvider,
            };

            var importer = new DdlSchemaImporter();
            DdlImportResult result = importer.Import(metadata, scratch);
            return new DdlSchemaImportPayload(
                result,
                scratch.Nodes.ToList(),
                scratch.Connections.ToList(),
                metadata.Provider);
        });

        ddlCanvas.Provider = payload.Provider;
        ddlCanvas.ReplaceGraph(payload.Nodes, payload.Connections);

        CurrentShell.ActivateDocument(WorkspaceDocumentType.DdlCanvas);
        SyncModeToggleState();

        if (payload.Result.TableCount == 0)
        {
            CurrentShell.Toasts.ShowWarning(L("toast.ddlNoTablesFound", "Nenhuma tabela encontrada para importar no modo DDL."));
            return;
        }

        CurrentShell.Toasts.ShowSuccess(
            L("toast.ddlSchemaImported", "Schema importado para o canvas DDL."),
            BuildDdlImportSummary(payload.Result)
        );
    }

    private sealed record DdlSchemaImportPayload(
        DdlImportResult Result,
        IReadOnlyList<NodeViewModel> Nodes,
        IReadOnlyList<ConnectionViewModel> Connections,
        DatabaseProvider Provider
    );

    private static string BuildDdlImportSummary(DdlImportResult result)
    {
        string summary = LF(
            "toast.ddlImportSummary",
            "{0} tabela(s), {1} coluna(s), {2} FK(s), {3} indice(s) unicos.",
            result.TableCount,
            result.ColumnCount,
            result.ForeignKeyCount,
            result.IndexCount);

        if (result.Warnings is null || result.Warnings.Count == 0)
            return summary;

        return summary + "\n" + string.Join("\n", result.Warnings);
    }

    private void ImportSingleTableToDdl(TableMetadata table, Point position)
    {
        DbMetadata? metadata = ResolveMetadataForActiveWorkspace();
        if (metadata is null)
        {
            CurrentShell.Toasts.ShowWarning(L("toast.ddlConnectToImportTable", "Conecte-se a um banco para importar tabelas no canvas DDL."));
            return;
        }

        CanvasViewModel ddlCanvas = CurrentShell.EnsureDdlCanvas();
        var importer = new DdlSchemaImporter();
        DdlPartialImportResult result = importer.ImportTable(metadata, table.FullName, ddlCanvas, position);

        CurrentShell.ActivateDocument(WorkspaceDocumentType.DdlCanvas);
        SyncModeToggleState();

        if (!result.TableAdded)
        {
            CurrentShell.Toasts.ShowWarning(LF("toast.ddlTableAlreadyExists", "A tabela '{0}' ja existe no canvas DDL.", table.FullName));
            return;
        }

        CurrentShell.Toasts.ShowSuccess(
            L("toast.ddlTableImported", "Tabela importada para o canvas DDL."),
            LF("toast.ddlTableImportSummary", "Nos: +{0}, conexoes: +{1}, FKs: +{2}.", result.AddedNodeCount, result.AddedConnectionCount, result.AddedForeignKeys)
        );
    }

    private async Task ExecuteDdlAsync()
    {
        ConnectionConfig? config = ResolveActiveConnectionConfigForWorkspace();
        if (config is null)
        {
            CurrentShell.Toasts.ShowWarning(L("toast.ddlNoActiveConnection", "Nenhuma conexão ativa para executar DDL."));
            return;
        }

        if (!TryBuildDdlSql(out string sql))
            return;

        DdlExecutionResult? executionResult = null;
        var dialogVm = new DdlExecuteDialogViewModel(sql);
        var dialog = new DdlExecuteDialogWindow(
            dialogVm,
            async (stopOnError, ct) =>
            {
                IDbOrchestratorFactory orchestratorFactory =
                    _services.GetRequiredService<IDbOrchestratorFactory>();
                await using IDbOrchestrator orchestrator = orchestratorFactory.Create(config);
                executionResult = await orchestrator.ExecuteDdlAsync(sql, stopOnError, ct);
                return executionResult;
            }
        );

        await dialog.ShowDialog(this);

        if (!dialogVm.HasResult)
            return;

        if (dialogVm.IsSuccess)
            CurrentShell.Toasts.ShowSuccess(L("toast.ddlExecutedSuccess", "DDL executado com sucesso."), dialogVm.ResultSummary);
        else
            CurrentShell.Toasts.ShowWarning(L("toast.ddlExecutedWithIssues", "DDL executado com falhas."), dialogVm.ResultDetails);

        if (dialogVm.IsSuccess || HasSuccessfulDdlStatements(executionResult))
            TryReloadActiveMetadataAfterDdl();
    }

    private void TryReloadActiveMetadataAfterDdl()
    {
        ConnectionManagerViewModel? manager = CurrentShell.ActiveConnectionManager;
        manager ??= ResolveConnectionManagerForActiveSubscreen();

        if (manager.ReloadMetadataCommand.CanExecute(null))
            manager.ReloadMetadataCommand.Execute(null);
    }

    private static bool HasSuccessfulDdlStatements(DdlExecutionResult? executionResult)
    {
        if (executionResult?.Statements is null)
            return false;

        foreach (DdlStatementExecutionResult statement in executionResult.Statements)
        {
            if (statement.Success)
                return true;
        }

        return false;
    }

    private async Task ViewDdlSqlAsync()
    {
        CanvasViewModel ddlCanvas = PrepareDdlPreviewCanvas();
        LiveDdlBarViewModel liveDdl = ddlCanvas.LiveDdl
            ?? throw new InvalidOperationException(
                L("error.mainWindow.ddlPreviewUnavailable", "Preview DDL indisponivel para o canvas atual.")
            );
        CurrentShell.OutputPreview.OpenForDdl(ddlCanvas, liveDdl, ddlCanvas.Provider.ToString());
        await Task.CompletedTask;
    }

    private CanvasViewModel PrepareDdlPreviewCanvas()
    {
        CanvasViewModel ddlCanvas = CurrentShell.EnsureDdlCanvas();
        DatabaseProvider provider = ResolveActiveConnectionConfigForWorkspace()?.Provider ?? ddlCanvas.Provider;
        ddlCanvas.Provider = provider;
        LiveDdlBarViewModel liveDdl = ddlCanvas.LiveDdl
            ?? throw new InvalidOperationException(
                L("error.mainWindow.ddlPreviewUnavailable", "Preview DDL indisponivel para o canvas atual.")
            );
        liveDdl.Recompile();
        return ddlCanvas;
    }

    private bool TryBuildDdlSql(out string sql)
    {
        sql = string.Empty;
        CanvasViewModel ddlCanvas = CurrentShell.EnsureDdlCanvas();

        if (!CurrentShell.IsDdlDocumentPageActive)
        {
            CurrentShell.Toasts.ShowWarning(L("toast.switchToDdl", "Alterne para o modo DDL para gerar SQL."));
            return false;
        }

        DatabaseProvider provider = ResolveActiveConnectionConfigForWorkspace()?.Provider ?? ddlCanvas.Provider;
        ddlCanvas.Provider = provider;
        LiveDdlBarViewModel liveDdl = ddlCanvas.LiveDdl
            ?? throw new InvalidOperationException(
                L("error.mainWindow.ddlPreviewUnavailable", "Preview DDL indisponivel para o canvas atual.")
            );
        liveDdl.Recompile();

        if (!liveDdl.IsValid)
        {
            string details = liveDdl.CompileError ?? string.Join("\n", liveDdl.ErrorHints);
            CurrentShell.Toasts.ShowError(L("toast.ddlInvalid", "DDL inválido. Corrija os erros antes de continuar."), details);
            return false;
        }

        sql = liveDdl.RawSql;
        if (string.IsNullOrWhiteSpace(sql))
        {
            CurrentShell.Toasts.ShowWarning(L("toast.ddlNoStatements", "Nenhum statement DDL foi gerado no canvas."));
            return false;
        }

        return true;
    }

    private void WireToolbarMenuButtons()
    {
        void B(string name, Action a)
        {
            Button? btn = this.FindControl<Button>(name);
            if (btn is not null)
                btn.Click += (_, _) => a();
        }

        B(
            "NewBtn",
            () =>
            {
                EnterCanvasMode();
                ResetCurrentCanvas();
            }
        );
        B("OpenSearchBtn", () =>
        {
            EnterCanvasMode();
            OpenSearch();
        });
        B("SaveBtn", () =>
        {
            EnterCanvasMode();
            _ = _fileOps?.SaveAsync();
        });
        B("FileHistoryBtn", () =>
        {
            EnterCanvasMode();
            ResolveActiveDiagramCanvasStrict().FileHistory.Open();
        });
        B("OpenBtn", () =>
        {
            EnterCanvasMode();
            _ = _fileOps?.OpenAsync();
        });
        B("HomeBtn", () =>
        {
            if (!_canvasInitialized)
                return;
            CanvasViewModel canvas = ResolveDiagramCanvasForConnectionSidebar();
            canvas.ConnectionManager.IsVisible = false;

            CurrentShell.StartMenu.RefreshData(
                canvas.ConnectionManager.Profiles,
                canvas.ConnectionManager.ActiveProfileId
            );
            CurrentShell.ReturnToStart();
            Title = AppConstants.AppDisplayName;
        });
        B("ShortcutsBtn", OpenKeyboardShortcutsWindow);
        B("ZoomInBtn", () =>
        {
            EnterCanvasMode();
            ResolveActiveDiagramCanvasStrict().ZoomInCommand.Execute(null);
        });
        B("ZoomOutBtn", () =>
        {
            EnterCanvasMode();
            ResolveActiveDiagramCanvasStrict().ZoomOutCommand.Execute(null);
        });
        B("FitBtn", () =>
        {
            EnterCanvasMode();
            ResolveActiveDiagramCanvasStrict().FitToScreenCommand.Execute(null);
        });
    }

    private async Task OpenModeAwareOutputPreviewSafeAsync()
    {
        try
        {
            await OpenModeAwareOutputPreviewAsync();
        }
        catch (Exception ex)
        {
            CurrentShell.Toasts.ShowError(L("toast.previewOpenFailed", "Falha ao abrir preview."), ex.Message);
        }
    }

    private async Task OpenModeAwareQuickDataPreviewSafeAsync()
    {
        try
        {
            bool opened = await CurrentShell.OpenQuickDataPreviewForActiveModeAsync();
            if (!opened)
            {
                CurrentShell.Toasts.ShowWarning(
                    "Nao foi possivel abrir o preview rapido.",
                    "No modo DDL, selecione uma tabela ou view para visualizar os dados.");
            }
        }
        catch (Exception ex)
        {
            CurrentShell.Toasts.ShowError("Falha ao abrir preview rapido.", ex.Message);
        }
    }

    private async Task OpenModeAwareOutputPreviewAsync()
    {
        switch (CurrentShell.ActivePreviewContract.Kind)
        {
            case WorkspaceDocumentPreviewKind.Query:
            {
                CanvasViewModel queryCanvas = CurrentShell.ActiveQueryCanvasDocument
                    ?? CurrentShell.EnsureCanvas();
                queryCanvas.LiveSql.Recompile();
                DatabaseProvider provider = ResolveActiveConnectionConfigForWorkspace()?.Provider ?? queryCanvas.Provider;
                CurrentShell.OutputPreview.OpenForQuery(queryCanvas, queryCanvas.LiveSql, provider.ToString());
                TrackCriticalFlow("CF-07-sql-preview", "open_query_preview", "ok");
                await Task.CompletedTask;
                return;
            }

            case WorkspaceDocumentPreviewKind.Ddl:
            {
                await ViewDdlSqlAsync();
                TrackCriticalFlow("CF-07-sql-preview", "open_ddl_preview", "ok");
                return;
            }

            default:
            {
                var contract = CurrentShell.ActivePreviewContract;
                CurrentShell.OutputPreview.OpenUnavailable(contract.Title, contract.PrimaryTabLabel, contract.UnavailableMessage);
                await Task.CompletedTask;
                return;
            }
        }
    }
}

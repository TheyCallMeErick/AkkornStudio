import { computed, proxyRefs, reactive, watch } from "vue";

export const STORAGE_KEY = "akkorn-report-view-vue-v8";
export const PAGE_SIZES = [10, 25, 50, 100];
export const DEFAULT_EMPTY = "-";
export const MIN_COLUMN_WIDTH = 140;
const VIRTUAL_ROW_HEIGHT = 48;
const VIRTUAL_OVERSCAN = 6;

export function createReportModel() {
  const payload = window.__AKKORN_REPORT__ ?? {};
  const labels = payload.labels ?? {};
  const meta = payload.meta ?? {};
  const txt = (pt, en) => labels[pt] ?? labels[en] ?? pt;

  const datasets = computed(() => buildDatasets(payload, txt));
  const state = reactive(loadState(datasets.value));
  applyStateSnapshot(state, readStateSnapshotFromHash(), datasets.value);
  const ui = reactive({
    activeModal: "",
    modalDatasetId: "",
    longCellValue: "",
    toast: "",
    toastTimer: 0,
    resizing: null,
    contextMenu: null,
    scrollHints: {},
    exportOptions: {
      datasetId: "results",
      scope: "filtered",
      format: "csv",
      visibleOnly: true
    }
  });

  const pageJobs = new Map();

  const datasetViews = computed(() => {
    const entries = {};
    for (const dataset of datasets.value) {
      entries[dataset.id] = buildDatasetView(dataset, state.datasets[dataset.id]);
    }

    return entries;
  });
  const getExportRows = getExportRowsFactory(state, datasetViews);

  const overviewCards = computed(() => buildOverviewCards(meta, payload, txt));
  const themeIcon = computed(() => (state.theme === "dark" ? "moon" : "sun"));
  const footerText = computed(() => `${meta.title ?? "Report"} • Akkorn Studio • v${payload.version ?? "1.0"}`);

  watch(
    () => state,
    () => saveState(state),
    { deep: true }
  );

  watch(
    () => state.theme,
    (theme) => {
      document.documentElement.setAttribute("data-theme", theme);
    },
    { immediate: true }
  );

  function activeDataset() {
    return datasets.value.find((dataset) => dataset.id === ui.modalDatasetId) ?? null;
  }

  function activeDatasetState() {
    return ui.modalDatasetId ? state.datasets[ui.modalDatasetId] : null;
  }

  function openModal(name, datasetId = "") {
    ui.activeModal = name;
    ui.modalDatasetId = datasetId;
  }

  function closeModal() {
    ui.activeModal = "";
    ui.modalDatasetId = "";
  }

  function closeContextMenu() {
    ui.contextMenu = null;
  }

  function toggleTheme() {
    state.theme = state.theme === "dark" ? "light" : "dark";
  }

  function toggleSection(id) {
    state.sections[id] = !state.sections[id];
  }

  function resetDataset(id) {
    const dataset = datasets.value.find((item) => item.id === id);
    if (!dataset) {
      return;
    }

    state.datasets[id] = normalizeDatasetState(dataset, {});
  }

  function clearFilters(id) {
    state.datasets[id].filters = [];
    state.datasets[id].page = 1;
  }

  function removeFilter(id, index) {
    state.datasets[id].filters.splice(index, 1);
    state.datasets[id].page = 1;
  }

  function openFilterModal(id) {
    const dataset = datasets.value.find((item) => item.id === id);
    if (!dataset) {
      return;
    }

    state.datasets[id].pendingFilter = normalizePendingFilter(dataset, state.datasets[id].pendingFilter);
    openModal("filter", id);
  }

  function openSortModal(id) {
    openModal("sort", id);
  }

  function openColumnsModal(id) {
    openModal("columns", id);
  }

  function openGroupModal(id) {
    openModal("group", id);
  }

  function openStatsModal(id) {
    if (!state.datasets[id].statsColumnKey) {
      state.datasets[id].statsColumnKey = datasetViews.value[id]?.visibleColumns[0]?.key ?? "";
    }
    openModal("stats", id);
  }

  function openExportModal(id = "results") {
    ui.exportOptions.datasetId = id;
    openModal("export", id);
  }

  function addFilter() {
    const dataset = activeDataset();
    const datasetState = activeDatasetState();
    if (!dataset || !datasetState || !datasetState.pendingFilter.key) {
      return;
    }

    datasetState.filters.push({ ...datasetState.pendingFilter });
    datasetState.page = 1;
    const currentKey = datasetState.pendingFilter.key;
    datasetState.pendingFilter = normalizePendingFilter(dataset, { key: currentKey });
  }

  function applyGrouping() {
    const datasetState = activeDatasetState();
    if (!datasetState) {
      return;
    }

    datasetState.page = 1;
    closeModal();
  }

  function applySort() {
    const datasetState = activeDatasetState();
    if (!datasetState) {
      return;
    }

    datasetState.page = 1;
    closeModal();
  }

  function clearSort() {
    const datasetState = activeDatasetState();
    if (!datasetState) {
      return;
    }

    datasetState.sorts = [];
    syncPrimarySort(datasetState);
    datasetState.page = 1;
  }

  function setSortFromHeader(id, key) {
    const datasetState = state.datasets[id];
    const current = datasetState.sorts[0];
    if (current?.key === key) {
      datasetState.sorts = [{ key, dir: current.dir === "asc" ? "desc" : "asc" }, ...datasetState.sorts.slice(1)];
    } else {
      datasetState.sorts = [{ key, dir: "asc" }, ...datasetState.sorts.filter((item) => item.key !== key)];
    }
    syncPrimarySort(datasetState);
    datasetState.page = 1;
  }

  function addSortRule() {
    const dataset = activeDataset();
    const datasetState = activeDatasetState();
    if (!dataset || !datasetState) {
      return;
    }

    const key = dataset.columns.find((column) => !datasetState.sorts.some((item) => item.key === column.key))?.key ?? dataset.columns[0]?.key ?? "";
    if (!key) {
      return;
    }

    datasetState.sorts.push({ key, dir: "asc" });
    syncPrimarySort(datasetState);
  }

  function removeSortRule(index) {
    const datasetState = activeDatasetState();
    if (!datasetState) {
      return;
    }

    datasetState.sorts.splice(index, 1);
    syncPrimarySort(datasetState);
  }

  function updateSortRuleKey(index, key) {
    const datasetState = activeDatasetState();
    if (!datasetState || !datasetState.sorts[index]) {
      return;
    }

    datasetState.sorts[index].key = key;
    datasetState.sorts = dedupeSortRules(datasetState.sorts);
    syncPrimarySort(datasetState);
  }

  function updateSortRuleDirection(index, dir) {
    const datasetState = activeDatasetState();
    if (!datasetState || !datasetState.sorts[index]) {
      return;
    }

    datasetState.sorts[index].dir = dir === "desc" ? "desc" : "asc";
    syncPrimarySort(datasetState);
  }

  function toggleRow(id, rowId, event = null) {
    const selected = state.datasets[id].selectedRows;
    const datasetState = state.datasets[id];
    const view = datasetViews.value[id];
    if (event?.shiftKey && datasetState.lastRowSelection && view) {
      const rowIds = view.rows.map((entry) => entry.rowId);
      const start = rowIds.indexOf(datasetState.lastRowSelection);
      const end = rowIds.indexOf(rowId);
      if (start >= 0 && end >= 0) {
        const [from, to] = start < end ? [start, end] : [end, start];
        datasetState.selectedRows = [...new Set([...selected, ...rowIds.slice(from, to + 1)])];
        datasetState.lastRowSelection = rowId;
        return;
      }
    }

    const index = selected.indexOf(rowId);
    if (index >= 0) {
      selected.splice(index, 1);
    } else {
      selected.push(rowId);
    }
    datasetState.lastRowSelection = rowId;
  }

  function selectCell(id, rowId, key) {
    state.datasets[id].selectedCell = { rowId, key };
    state.datasets[id].selectedCells = [`${rowId}::${key}`];
    state.datasets[id].anchorCell = { rowId, key };
    closeContextMenu();
  }

  function selectCellWithEvent(id, rowId, key, event = null) {
    const datasetState = state.datasets[id];
    const view = datasetViews.value[id];
    if (event?.shiftKey && datasetState.anchorCell && view) {
      const rowIds = view.rows.map((entry) => entry.rowId);
      const colKeys = view.visibleColumns.map((column) => column.key);
      const startRow = rowIds.indexOf(datasetState.anchorCell.rowId);
      const endRow = rowIds.indexOf(rowId);
      const startCol = colKeys.indexOf(datasetState.anchorCell.key);
      const endCol = colKeys.indexOf(key);
      if (startRow >= 0 && endRow >= 0 && startCol >= 0 && endCol >= 0) {
        const [rowFrom, rowTo] = startRow < endRow ? [startRow, endRow] : [endRow, startRow];
        const [colFrom, colTo] = startCol < endCol ? [startCol, endCol] : [endCol, startCol];
        const selectedCells = [];
        for (let rowIndex = rowFrom; rowIndex <= rowTo; rowIndex += 1) {
          for (let colIndex = colFrom; colIndex <= colTo; colIndex += 1) {
            selectedCells.push(`${rowIds[rowIndex]}::${colKeys[colIndex]}`);
          }
        }
        datasetState.selectedCell = { rowId, key };
        datasetState.selectedCells = selectedCells;
        closeContextMenu();
        return;
      }
    }

    selectCell(id, rowId, key);
  }

  function isCellSelected(id, rowId, key) {
    const datasetState = state.datasets[id];
    if (datasetState.selectedCells?.length > 0) {
      return datasetState.selectedCells.includes(`${rowId}::${key}`);
    }

    const selected = datasetState.selectedCell;
    return !!selected && selected.rowId === rowId && selected.key === key;
  }

  function isRowSelected(id, rowId) {
    return state.datasets[id].selectedRows.includes(rowId);
  }

  function changePage(id, target) {
    const view = datasetViews.value[id];
    if (!view) {
      return;
    }

    const safeTarget = Math.min(Math.max(1, normalizePositiveInt(target, 1)), view.pageCount);
    const datasetState = state.datasets[id];
    if (datasetState.page === safeTarget && !datasetState.pageBusy) {
      return;
    }

    if (pageJobs.has(id)) {
      window.clearTimeout(pageJobs.get(id));
    }

    datasetState.pageBusy = true;
    const handle = window.setTimeout(() => {
      datasetState.page = safeTarget;
      window.requestAnimationFrame(() => {
        datasetState.pageBusy = false;
        pageJobs.delete(id);
      });
    }, 0);
    pageJobs.set(id, handle);
  }

  function pageJump(event, id) {
    changePage(id, event.target.value);
  }

  function openCell(value) {
    ui.longCellValue = displayValue(value);
    closeContextMenu();
    openModal("cell");
  }

  function isLongText(value) {
    return stringifyValue(value).length > 96;
  }

  function copySql() {
    copyText(payload.sql ?? "", txt("SQL copiado", "SQL copied"), ui);
  }

  function copySummary() {
    const lines = [
      meta.title ?? "",
      meta.description ?? "",
      `${txt("Linhas", "Rows")}: ${(payload.rows ?? []).length}`,
      `${txt("Colunas", "Columns")}: ${(payload.schema ?? []).length}`,
      `${txt("Gerado em", "Generated at")}: ${meta.generatedAt ?? ""}`
    ].filter(Boolean);
    copyText(lines.join("\n"), txt("Resumo copiado", "Summary copied"), ui);
  }

  function exportCsv() {
    const view = datasetViews.value.results;
    if (!view) {
      return;
    }

    const headers = view.visibleColumns.map((column) => column.label);
    const rows = view.rows.map((entry) => view.visibleColumns.map((column) => csvEscape(getValue(entry.raw, column.key))));
    const content = [headers.map(csvEscape).join(","), ...rows.map((row) => row.join(","))].join("\r\n");
    const blob = new Blob([content], { type: "text/csv;charset=utf-8;" });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = `${(meta.title ?? "report").replace(/[^\w\-]+/g, "_")}.csv`;
    anchor.click();
    URL.revokeObjectURL(url);
  }

  function exportJson() {
    const fileName = `${slugify(meta.title ?? "report") || "report"}.json`;
    const blob = new Blob([JSON.stringify(payload, null, 2)], { type: "application/json;charset=utf-8;" });
    downloadBlob(blob, fileName);
  }

  function choosePick(id, bucket, key, multi = false) {
    const list = state.datasets[id].pickSelection[bucket];
    if (!multi) {
      list.splice(0, list.length, key);
      return;
    }

    const index = list.indexOf(key);
    if (index >= 0) {
      list.splice(index, 1);
    } else {
      list.push(key);
    }
  }

  function moveColumns(id, direction) {
    const datasetState = state.datasets[id];
    const view = datasetViews.value[id];
    if (!view) {
      return;
    }

    if (direction === "add") {
      for (const key of datasetState.pickSelection.available) {
        if (!datasetState.visibleKeys.includes(key)) {
          datasetState.visibleKeys.push(key);
        }
      }
      datasetState.pickSelection.available = [];
      return;
    }

    if (direction === "remove") {
      datasetState.visibleKeys = datasetState.visibleKeys.filter((key) => !datasetState.pickSelection.visible.includes(key));
      if (datasetState.visibleKeys.length === 0) {
        datasetState.visibleKeys = view.dataset.columns.slice(0, 1).map((column) => column.key);
      }
      datasetState.pickSelection.visible = [];
    }
  }

  function moveVisibleColumnOrder(id, direction) {
    const datasetState = state.datasets[id];
    const selectedKey = datasetState.pickSelection.visible[0];
    if (!selectedKey) {
      return;
    }

    const currentIndex = datasetState.visibleKeys.indexOf(selectedKey);
    if (currentIndex < 0) {
      return;
    }

    const targetIndex = direction === "up" ? currentIndex - 1 : currentIndex + 1;
    if (targetIndex < 0 || targetIndex >= datasetState.visibleKeys.length) {
      return;
    }

    const next = [...datasetState.visibleKeys];
    [next[currentIndex], next[targetIndex]] = [next[targetIndex], next[currentIndex]];
    datasetState.visibleKeys = next;
  }

  function moveAllColumns(id, mode) {
    const dataset = datasets.value.find((item) => item.id === id);
    if (!dataset) {
      return;
    }

    if (mode === "all") {
      state.datasets[id].visibleKeys = dataset.columns.map((column) => column.key);
      return;
    }

    state.datasets[id].visibleKeys = dataset.columns.slice(0, 1).map((column) => column.key);
  }

  function visibleColumnSummary(id) {
    return datasetViews.value[id]?.shownColumns ?? [];
  }

  function selectedRowsCount(id) {
    return state.datasets[id]?.selectedRows?.length ?? 0;
  }

  function copySelectedCell(id) {
    const datasetState = state.datasets[id];
    const selected = datasetState?.selectedCell;
    const view = datasetViews.value[id];
    if (!selected || !view) {
      return;
    }

    if (datasetState.selectedCells?.length > 1) {
      const rowIds = [...new Set(datasetState.selectedCells.map((item) => item.split("::")[0]))];
      const colKeys = view.visibleColumns
        .map((column) => column.key)
        .filter((key) => datasetState.selectedCells.some((item) => item.endsWith(`::${key}`)));
      const body = rowIds.map((rowId) => {
        const entry = view.rows.find((item) => item.rowId === rowId);
        return colKeys.map((key) => displayValue(getValue(entry?.raw, key))).join("\t");
      });
      copyText(body.join("\n"), txt("Selecao copiada", "Selection copied"), ui);
      return;
    }

    const entry = view.rows.find((item) => item.rowId === selected.rowId);
    if (!entry) {
      return;
    }

    copyText(displayValue(getValue(entry.raw, selected.key)), txt("Celula copiada", "Cell copied"), ui);
  }

  function copySelectedRows(id) {
    const selected = state.datasets[id]?.selectedRows ?? [];
    const view = datasetViews.value[id];
    if (!view || selected.length === 0) {
      return;
    }

    const rows = view.rows.filter((entry) => selected.includes(entry.rowId));
    if (rows.length === 0) {
      return;
    }

    const headers = view.visibleColumns.map((column) => column.label);
    const body = rows.map((entry) => view.visibleColumns.map((column) => displayValue(getValue(entry.raw, column.key))).join("\t"));
    copyText([headers.join("\t"), ...body].join("\n"), txt("Linhas copiadas", "Rows copied"), ui);
  }

  function exportSelectedRows(id = "results") {
    exportDatasetData(id, "selected", "csv", true);
  }

  function exportFilteredRows(id = "results") {
    exportDatasetData(id, "filtered", "csv", true);
  }

  function openCellMenu(event, id, rowId, key) {
    event.preventDefault();
    selectCell(id, rowId, key);
    const view = datasetViews.value[id];
    const entry = view?.rows.find((item) => item.rowId === rowId);
    const value = entry ? getValue(entry.raw, key) : "";
    ui.contextMenu = {
      type: "cell",
      datasetId: id,
      rowId,
      key,
      value,
      x: event.clientX,
      y: event.clientY
    };
  }

  function openHeaderMenu(event, id, key) {
    event.preventDefault();
    closeContextMenu();
    ui.contextMenu = {
      type: "header",
      datasetId: id,
      key,
      x: event.clientX,
      y: event.clientY
    };
  }

  function contextFilterByValue() {
    const menu = ui.contextMenu;
    if (!menu) {
      return;
    }

    state.datasets[menu.datasetId].filters.push({
      key: menu.key,
      operator: "eq",
      value: displayValue(menu.value) === DEFAULT_EMPTY ? "" : stringifyValue(menu.value)
    });
    state.datasets[menu.datasetId].page = 1;
    closeContextMenu();
  }

  function contextHideColumn() {
    const menu = ui.contextMenu;
    if (!menu) {
      return;
    }

    const datasetState = state.datasets[menu.datasetId];
    datasetState.visibleKeys = datasetState.visibleKeys.filter((item) => item !== menu.key);
    if (datasetState.visibleKeys.length === 0) {
      datasetState.visibleKeys = [menu.key];
    }
    closeContextMenu();
  }

  function contextShowOnlyColumn() {
    const menu = ui.contextMenu;
    if (!menu) {
      return;
    }

    state.datasets[menu.datasetId].visibleKeys = [menu.key];
    closeContextMenu();
  }

  function copyContextValue() {
    const menu = ui.contextMenu;
    if (!menu) {
      return;
    }

    copyText(displayValue(menu.value), txt("Celula copiada", "Cell copied"), ui);
    closeContextMenu();
  }

  function togglePinnedColumn() {
    const menu = ui.contextMenu;
    if (!menu) {
      return;
    }

    const datasetState = state.datasets[menu.datasetId];
    const current = [...datasetState.pinnedColumnKeys];
    datasetState.pinnedColumnKeys = current.includes(menu.key)
      ? current.filter((item) => item !== menu.key)
      : [...current, menu.key];
    closeContextMenu();
  }

  function isPinnedColumn(id, key) {
    return state.datasets[id]?.pinnedColumnKeys?.includes(key);
  }

  function contextSort(direction, mode = "primary") {
    const menu = ui.contextMenu;
    if (!menu) {
      return;
    }

    const datasetState = state.datasets[menu.datasetId];
    const dir = direction === "desc" ? "desc" : "asc";
    const nextRule = { key: menu.key, dir };
    datasetState.sorts = mode === "secondary"
      ? dedupeSortRules([...datasetState.sorts.filter((item) => item.key !== menu.key), nextRule])
      : dedupeSortRules([nextRule, ...datasetState.sorts.filter((item) => item.key !== menu.key)]);
    syncPrimarySort(datasetState);
    datasetState.page = 1;
    closeContextMenu();
  }

  function getColumnWidth(id, key) {
    const width = state.datasets[id]?.columnWidths?.[key];
    return clampColumnWidth(width);
  }

  function columnStyle(id, key) {
    const width = getColumnWidth(id, key);
    return {
      width: `${width}px`,
      minWidth: `${MIN_COLUMN_WIDTH}px`
    };
  }

  function columnStickyClass(id, key) {
    return { "sticky-main-col": isPinnedColumn(id, key) };
  }

  function columnStickyStyle(id, key) {
    if (!isPinnedColumn(id, key)) {
      return {};
    }

    const datasetState = state.datasets[id];
    const pinnedKeys = (datasetState?.pinnedColumnKeys ?? []).filter((item) => datasetState.visibleKeys.includes(item));
    const targetIndex = pinnedKeys.indexOf(key);
    if (targetIndex < 0) {
      return {};
    }

    let left = 28;
    for (let index = 0; index < targetIndex; index += 1) {
      left += getColumnWidth(id, pinnedKeys[index]);
    }

    return { left: `${left}px` };
  }

  function startResize(event, id, key) {
    if (event.button !== 0) {
      return;
    }

    const startWidth = getColumnWidth(id, key);
    ui.resizing = {
      id,
      key,
      startX: event.clientX,
      startWidth
    };

    const onMove = (moveEvent) => {
      if (!ui.resizing || ui.resizing.id !== id || ui.resizing.key !== key) {
        return;
      }

      const nextWidth = ui.resizing.startWidth + (moveEvent.clientX - ui.resizing.startX);
      state.datasets[id].columnWidths[key] = clampColumnWidth(nextWidth);
    };

    const onUp = () => {
      window.removeEventListener("mousemove", onMove);
      window.removeEventListener("mouseup", onUp);
      ui.resizing = null;
    };

    window.addEventListener("mousemove", onMove);
    window.addEventListener("mouseup", onUp);
    event.preventDefault();
    event.stopPropagation();
  }

  function autoFitColumn(id, key) {
    const view = datasetViews.value[id];
    if (!view) {
      return;
    }

    const column = view.visibleColumns.find((item) => item.key === key);
    if (!column) {
      return;
    }

    const sample = [column.label, ...view.rows.slice(0, 120).map((entry) => displayValue(getValue(entry.raw, key)))];
    const contentWidth = Math.max(...sample.map((value) => estimateColumnWidth(value, column.kind)));
    state.datasets[id].columnWidths[key] = clampColumnWidth(contentWidth, defaultColumnWidth(column.kind));
  }

  function saveCustomPreset(id) {
    const datasetState = state.datasets[id];
    const name = (window.prompt(txt("Nome do preset", "Preset name"), datasetState.activeCustomPreset || txt("Meu preset", "My preset")) ?? "").trim();
    if (!name) {
      return;
    }

    const preset = {
      name,
      visibleKeys: [...datasetState.visibleKeys],
      sort: { ...datasetState.sort },
      sorts: datasetState.sorts.map((item) => ({ ...item })),
      size: datasetState.size,
      columnWidths: { ...datasetState.columnWidths },
      pinnedColumnKeys: [...datasetState.pinnedColumnKeys],
      filters: [...datasetState.filters],
      groupBy: datasetState.groupBy || ""
    };

    datasetState.customPresets = [...datasetState.customPresets.filter((item) => item.name !== name), preset];
    datasetState.activeCustomPreset = name;
  }

  function applyCustomPreset(id, name) {
    const datasetState = state.datasets[id];
    const dataset = datasets.value.find((item) => item.id === id);
    const preset = datasetState.customPresets.find((item) => item.name === name);
    if (!preset || !dataset) {
      return;
    }

    const normalized = normalizePreset(dataset, preset);
    datasetState.visibleKeys = [...normalized.visibleKeys];
    datasetState.sorts = [...normalized.sorts];
    syncPrimarySort(datasetState);
    datasetState.size = normalized.size;
    datasetState.columnWidths = { ...datasetState.columnWidths, ...normalized.columnWidths };
    datasetState.pinnedColumnKeys = [...normalized.pinnedColumnKeys];
    datasetState.filters = [...normalized.filters];
    datasetState.groupBy = normalized.groupBy || "";
    datasetState.page = 1;
    datasetState.activeCustomPreset = normalized.name;
  }

  function deleteCustomPreset(id, name) {
    const datasetState = state.datasets[id];
    datasetState.customPresets = datasetState.customPresets.filter((item) => item.name !== name);
    if (datasetState.activeCustomPreset === name) {
      datasetState.activeCustomPreset = "";
    }
  }

  function exportCustomPresets(id) {
    const datasetState = state.datasets[id];
    if (!datasetState || datasetState.customPresets.length === 0) {
      return;
    }

    const blob = new Blob([JSON.stringify(datasetState.customPresets, null, 2)], { type: "application/json;charset=utf-8;" });
    downloadBlob(blob, `${slugify(`${meta.title ?? "report"}-${id}-presets`) || "report-presets"}.json`);
  }

  function importCustomPresets(id) {
    const input = document.createElement("input");
    input.type = "file";
    input.accept = "application/json,.json";
    input.onchange = async () => {
      const file = input.files?.[0];
      if (!file) {
        return;
      }

      try {
        const text = await file.text();
        const imported = JSON.parse(text);
        if (!Array.isArray(imported)) {
          throw new Error("invalid");
        }

        const dataset = datasets.value.find((item) => item.id === id);
        if (!dataset) {
          return;
        }

        const normalized = imported
          .filter((item) => item && typeof item.name === "string")
          .map((item) => normalizePreset(dataset, item));
        const datasetState = state.datasets[id];
        datasetState.customPresets = dedupePresets([...datasetState.customPresets, ...normalized]);
        showToast(txt("Presets importados", "Presets imported"), ui);
      } catch {
        showToast(txt("Falha ao importar presets", "Could not import presets"), ui);
      }
    };
    input.click();
  }

  function quickFilterValues(id) {
    const view = datasetViews.value[id];
    const datasetState = state.datasets[id];
    if (!view || view.rows.length === 0) {
      return [];
    }

    const activeKey = datasetState.selectedCell?.key || datasetState.pinnedColumnKeys?.[0] || view.visibleColumns[0]?.key;
    const column = view.visibleColumns.find((item) => item.key === activeKey);
    if (!column) {
      return [];
    }

    const counts = new Map();
    for (const entry of view.rows.slice(0, 200)) {
      const value = stringifyValue(getValue(entry.raw, activeKey)).trim();
      if (!value) {
        continue;
      }
      counts.set(value, (counts.get(value) ?? 0) + 1);
    }

    return [...counts.entries()]
      .sort((left, right) => right[1] - left[1])
      .slice(0, 5)
      .map(([value, count]) => ({ key: activeKey, value, count, label: column.label }));
  }

  function addQuickFilter(id, key, value) {
    state.datasets[id].filters.push({ key, operator: "eq", value });
    state.datasets[id].page = 1;
  }

  function applyPreset(id, preset) {
    const dataset = datasets.value.find((item) => item.id === id);
    const datasetState = state.datasets[id];
    if (!dataset || !datasetState) {
      return;
    }

    const textColumns = dataset.columns.filter((column) => column.kind === "text");
    const compactColumns = dataset.columns.filter((column) => column.kind !== "text");
    if (preset === "summary") {
      datasetState.visibleKeys = [...compactColumns.slice(0, 4), ...textColumns.slice(0, 3)].slice(0, 7).map((column) => column.key);
      datasetState.size = 10;
    } else if (preset === "audit") {
      datasetState.visibleKeys = dataset.columns.map((column) => column.key);
      datasetState.size = 25;
    } else if (preset === "detailed") {
      datasetState.visibleKeys = dataset.columns.map((column) => column.key);
      datasetState.size = 10;
      for (const column of textColumns) {
        datasetState.columnWidths[column.key] = Math.max(datasetState.columnWidths[column.key] ?? 0, 320);
      }
    }
    datasetState.page = 1;
    datasetState.activePreset = preset;
  }

  function updateScrollHint(id, event) {
    const element = event?.currentTarget ?? event?.target;
    if (!element) {
      return;
    }

    const maxScrollLeft = Math.max(0, element.scrollWidth - element.clientWidth);
    ui.scrollHints[id] = {
      left: element.scrollLeft > 8,
      right: element.scrollLeft < maxScrollLeft - 8
    };
  }

  function scrollClass(id) {
    const hint = ui.scrollHints[id] ?? { left: false, right: true };
    return {
      "has-left-shadow": !!hint.left,
      "has-right-shadow": !!hint.right
    };
  }

  function activeSortSummary(id) {
    const view = datasetViews.value[id];
    const datasetState = state.datasets[id];
    if (!view || !datasetState) {
      return [];
    }

    return datasetState.sorts
      .map((sortRule, index) => {
        const column = view.dataset.columns.find((item) => item.key === sortRule.key);
        return column
          ? {
              index,
              key: sortRule.key,
              dir: sortRule.dir,
              label: column.label
            }
          : null;
      })
      .filter(Boolean);
  }

  function isPageBusy(id) {
    return !!state.datasets[id]?.pageBusy;
  }

  function groupedByLabel(id) {
    const datasetState = state.datasets[id];
    const dataset = datasets.value.find((item) => item.id === id);
    const column = dataset?.columns.find((item) => item.key === datasetState?.groupBy);
    return column?.label ?? column?.key ?? "";
  }

  function currentColumnStats(id) {
    const datasetState = state.datasets[id];
    const key = datasetState?.statsColumnKey || datasetViews.value[id]?.visibleColumns[0]?.key;
    if (!key) {
      return null;
    }

    return computeColumnStats(datasetViews.value[id], key);
  }

  function copyViewLink() {
    try {
      const snapshot = buildStateSnapshot(state);
      const encoded = encodeURIComponent(JSON.stringify(snapshot));
      const url = `${window.location.href.split("#")[0]}#view=${encoded}`;
      copyText(url, txt("Link da visao copiado", "View link copied"), ui);
    } catch {
      showToast(txt("Nao foi possivel gerar o link", "Could not generate the link"), ui);
    }
  }

  function exportDatasetFromModal() {
    exportDatasetData(
      ui.exportOptions.datasetId,
      ui.exportOptions.scope,
      ui.exportOptions.format,
      ui.exportOptions.visibleOnly
    );
    closeModal();
  }

  function exportDatasetData(id, scope, format, visibleOnly) {
    const view = datasetViews.value[id];
    if (!view) {
      return;
    }

    const rows = getExportRows(id, scope);
    const columns = visibleOnly ? view.visibleColumns : view.dataset.columns;
    const fileBase = slugify(`${meta.title ?? "report"}-${id}-${scope}`) || `report-${id}`;
    if (format === "json") {
      const payload = rows.map((entry) => projectRow(entry.raw ?? entry, columns));
      downloadBlob(new Blob([JSON.stringify(payload, null, 2)], { type: "application/json;charset=utf-8;" }), `${fileBase}.json`);
      return;
    }

    const headers = columns.map((column) => csvEscape(column.label));
    const body = rows.map((entry) => columns.map((column) => csvEscape(getValue(entry.raw ?? entry, column.key))).join(","));
    downloadBlob(new Blob([[headers.join(","), ...body].join("\r\n")], { type: "text/csv;charset=utf-8;" }), `${fileBase}.csv`);
  }

  function handleTableScroll(id, event) {
    updateScrollHint(id, event);
    const target = event?.currentTarget ?? event?.target;
    if (!target) {
      return;
    }

    state.datasets[id].scrollTop = target.scrollTop;
    state.datasets[id].viewportHeight = target.clientHeight;
  }

  function activePendingColumn() {
    const dataset = activeDataset();
    const datasetState = activeDatasetState();
    if (!dataset || !datasetState) {
      return null;
    }

    return dataset.columns.find((item) => item.key === datasetState.pendingFilter.key) ?? null;
  }

  function filterUsesRange(kind, operator) {
    return (kind === "number" || kind === "date") && operator === "between";
  }

  function filterInputType(kind) {
    if (kind === "number") {
      return "number";
    }

    if (kind === "date") {
      return "date";
    }

    return "text";
  }

  return proxyRefs({
    payload,
    meta,
    txt,
    state,
    ui,
    datasets,
    datasetViews,
    overviewCards,
    themeIcon,
    footerText,
    activeDataset,
    activeDatasetState,
    toggleTheme,
    toggleSection,
    resetDataset,
    clearFilters,
    removeFilter,
    openFilterModal,
    openSortModal,
    openColumnsModal,
    addFilter,
    applySort,
    clearSort,
    setSortFromHeader,
    toggleRow,
    selectCell,
    selectCellWithEvent,
    isCellSelected,
    isRowSelected,
    changePage,
    pageJump,
    openCell,
    isLongText,
    copySql,
    copySummary,
    exportCsv,
    exportJson,
    choosePick,
    moveColumns,
    moveAllColumns,
    moveVisibleColumnOrder,
    visibleColumnSummary,
    selectedRowsCount,
    copySelectedCell,
    copySelectedRows,
    openCellMenu,
    openHeaderMenu,
    contextFilterByValue,
    contextHideColumn,
    contextShowOnlyColumn,
    copyContextValue,
    contextSort,
    togglePinnedColumn,
    isPinnedColumn,
    getColumnWidth,
    columnStyle,
    columnStickyClass,
    columnStickyStyle,
    startResize,
    autoFitColumn,
    applyPreset,
    saveCustomPreset,
    applyCustomPreset,
    deleteCustomPreset,
    exportCustomPresets,
    importCustomPresets,
    quickFilterValues,
    addQuickFilter,
    updateScrollHint,
    scrollClass,
    activeSortSummary,
    groupedByLabel,
    currentColumnStats,
    activePendingColumn,
    filterUsesRange,
    filterInputType,
    isPageBusy,
    copyViewLink,
    openGroupModal,
    openStatsModal,
    openExportModal,
    applyGrouping,
    exportSelectedRows,
    exportFilteredRows,
    exportDatasetFromModal,
    handleTableScroll,
    addSortRule,
    removeSortRule,
    updateSortRuleKey,
    updateSortRuleDirection,
    openModal,
    closeModal,
    closeContextMenu,
    summaryChipText: formatSummaryChip,
    columnTone,
    allowedOperators,
    operatorLabel,
    getValue,
    displayValue,
    stringifyValue,
    PAGE_SIZES,
    DEFAULT_EMPTY
  });
}

function buildDatasets(payload, txt) {
  const sections = [];
  const resultColumns = buildResultColumns(payload.rows ?? [], payload.schema ?? []);
  sections.push({
    id: "results",
    title: txt("Resultados", "Results"),
    searchPlaceholder: txt("Buscar resultados", "Search results"),
    columns: resultColumns,
    rows: payload.rows ?? [],
    preparedRows: prepareDatasetRows(payload.rows ?? [], resultColumns),
    collapsed: false
  });

  if ((payload.schema ?? []).length > 0) {
    sections.push({
      id: "schema",
      title: txt("Colunas e schema", "Columns and schema"),
      searchPlaceholder: txt("Buscar schema", "Search schema"),
      columns: [
        { key: "name", label: txt("Nome", "Name"), kind: "text" },
        { key: "kind", label: txt("Tipo", "Type"), kind: "text" },
        { key: "nullCount", label: txt("Nulos", "Nulls"), kind: "number" },
        { key: "distinctCount", label: txt("Distintos", "Distinct"), kind: "number" },
        { key: "example", label: txt("Exemplo", "Example"), kind: "text" },
        { key: "minValue", label: txt("Minimo", "Minimum"), kind: "text" },
        { key: "maxValue", label: txt("Maximo", "Maximum"), kind: "text" }
      ],
      rows: (payload.schema ?? []).map((item) => ({
        name: item.name ?? item.Name ?? "",
        kind: item.kind ?? item.Kind ?? "",
        nullCount: item.nullCount ?? item.NullCount ?? 0,
        distinctCount: item.distinctCount ?? item.DistinctCount ?? 0,
        example: item.example ?? item.Example ?? "",
        minValue: item.minValue ?? item.MinValue ?? "",
        maxValue: item.maxValue ?? item.MaxValue ?? ""
      })),
      preparedRows: [],
      collapsed: true
    });
  }

  for (const entry of [
    ["metadata", txt("Metadados", "Metadata"), txt("Buscar metadados", "Search metadata"), payload.metadata ?? []],
    ["lineageNodes", txt("Nos de linhagem", "Lineage nodes"), txt("Buscar nos", "Search nodes"), payload.lineageNodes ?? []],
    ["lineageConnections", txt("Conexoes de linhagem", "Lineage connections"), txt("Buscar conexoes", "Search connections"), payload.lineageConnections ?? []]
  ]) {
    if (entry[3].length > 0) {
      sections.push({
        id: entry[0],
        title: entry[1],
        searchPlaceholder: entry[2],
        columns: buildColumnsFromRows(entry[3]),
        rows: entry[3],
        preparedRows: [],
        collapsed: true
      });
    }
  }

  return sections;
}

function loadState(datasetDefs) {
  const stored = readStoredState();
  const base = {
    theme: stored.theme === "light" ? "light" : "dark",
    sections: {
      overview: false,
      sql: true
    },
    datasets: {}
  };

  for (const dataset of datasetDefs) {
    const previous = stored.datasets?.[dataset.id] ?? {};
    base.sections[dataset.id] = typeof previous.collapsed === "boolean" ? previous.collapsed : !!dataset.collapsed;
    base.datasets[dataset.id] = normalizeDatasetState(dataset, previous);
  }

  return base;
}

function readStoredState() {
  try {
    return JSON.parse(window.localStorage.getItem(STORAGE_KEY) ?? "{}");
  } catch {
    return {};
  }
}

function readStateSnapshotFromHash() {
  try {
    const hash = window.location.hash ?? "";
    if (!hash.startsWith("#view=")) {
      return null;
    }

    return JSON.parse(decodeURIComponent(hash.slice(6)));
  } catch {
    return null;
  }
}

function saveState(appState) {
  const serializable = {
    theme: appState.theme,
    sections: JSON.parse(JSON.stringify(appState.sections)),
    datasets: JSON.parse(JSON.stringify(appState.datasets))
  };

  window.localStorage.setItem(STORAGE_KEY, JSON.stringify(serializable));
}

function normalizeDatasetState(dataset, input) {
  const safeColumns = sanitizeColumns(dataset.columns);
  dataset.columns = safeColumns;
  const availableKeys = safeColumns.map((column) => column.key);
  const visibleKeys = Array.isArray(input.visibleKeys) && input.visibleKeys.length > 0
    ? input.visibleKeys.filter((key) => availableKeys.includes(key))
    : [...availableKeys];

  const normalized = {
    search: typeof input.search === "string" ? input.search : "",
    filters: Array.isArray(input.filters) ? input.filters.filter((item) => availableKeys.includes(item.key)) : [],
    sort: normalizeSort(input.sort, availableKeys),
    sorts: normalizeSorts(input.sorts, availableKeys, input.sort),
    visibleKeys: visibleKeys.length > 0 ? visibleKeys : [...availableKeys],
    page: normalizePositiveInt(input.page, 1),
    pageBusy: false,
    scrollTop: normalizePositiveInt(input.scrollTop, 0),
    viewportHeight: normalizePositiveInt(input.viewportHeight, 560),
    size: normalizePageSize(input.size),
    inlineFiltersOpen: !!input.inlineFiltersOpen,
    inlineColumnsOpen: !!input.inlineColumnsOpen,
    selectedRows: Array.isArray(input.selectedRows) ? input.selectedRows.map(String) : [],
    selectedCell: input.selectedCell && typeof input.selectedCell.key === "string"
      ? { rowId: String(input.selectedCell.rowId ?? ""), key: input.selectedCell.key }
      : null,
    selectedCells: Array.isArray(input.selectedCells) ? input.selectedCells.map(String) : [],
    anchorCell: input.anchorCell && typeof input.anchorCell.key === "string"
      ? { rowId: String(input.anchorCell.rowId ?? ""), key: input.anchorCell.key }
      : null,
    columnWidths: normalizeColumnWidths(input.columnWidths, safeColumns),
    pendingFilter: normalizePendingFilter(dataset, input.pendingFilter),
    columnSearch: typeof input.columnSearch === "string" ? input.columnSearch : "",
    activePreset: typeof input.activePreset === "string" ? input.activePreset : "",
    activeCustomPreset: typeof input.activeCustomPreset === "string" ? input.activeCustomPreset : "",
    lastRowSelection: typeof input.lastRowSelection === "string" ? input.lastRowSelection : "",
    pinnedColumnKeys: normalizePinnedColumns(input, availableKeys),
    groupBy: typeof input.groupBy === "string" && availableKeys.includes(input.groupBy) ? input.groupBy : "",
    statsColumnKey: typeof input.statsColumnKey === "string" && availableKeys.includes(input.statsColumnKey) ? input.statsColumnKey : (availableKeys[0] ?? ""),
    customPresets: Array.isArray(input.customPresets) ? input.customPresets.filter((item) => item && typeof item.name === "string").map((item) => normalizePreset(dataset, item)) : [],
    pickSelection: {
      available: Array.isArray(input.pickSelection?.available) ? input.pickSelection.available.filter((key) => availableKeys.includes(key)) : [],
      visible: Array.isArray(input.pickSelection?.visible) ? input.pickSelection.visible.filter((key) => availableKeys.includes(key)) : []
    }
  };

  syncPrimarySort(normalized);
  return normalized;
}

function normalizePendingFilter(dataset, pendingFilter) {
  const firstColumn = dataset.columns[0];
  const key = dataset.columns.some((column) => column.key === pendingFilter?.key) ? pendingFilter.key : firstColumn?.key ?? "";
  const column = dataset.columns.find((item) => item.key === key) ?? firstColumn ?? { kind: "text" };
  const operator = allowedOperators(column.kind).includes(pendingFilter?.operator) ? pendingFilter.operator : defaultOperator(column.kind);
  return {
    key,
    operator,
    value: pendingFilter?.value ?? "",
    valueTo: pendingFilter?.valueTo ?? ""
  };
}

function normalizeColumnWidths(input, columns) {
  const widths = {};
  const source = input && typeof input === "object" ? input : {};
  for (const column of columns) {
    widths[column.key] = clampColumnWidth(source[column.key], defaultColumnWidth(column.kind));
  }

  return widths;
}

function normalizeSort(sort, availableKeys) {
  if (!sort || !availableKeys.includes(sort.key)) {
    return { key: "", dir: "asc" };
  }

  return {
    key: sort.key,
    dir: sort.dir === "desc" ? "desc" : "asc"
  };
}

function normalizeSorts(sorts, availableKeys, fallbackSort = null) {
  const source = Array.isArray(sorts) && sorts.length > 0 ? sorts : (fallbackSort ? [fallbackSort] : []);
  return dedupeSortRules(
    source
      .filter((item) => item && availableKeys.includes(item.key))
      .map((item) => ({
        key: item.key,
        dir: item.dir === "desc" ? "desc" : "asc"
      }))
  );
}

function dedupeSortRules(sorts) {
  const seen = new Set();
  const output = [];
  for (const item of sorts) {
    if (!item?.key || seen.has(item.key)) {
      continue;
    }
    seen.add(item.key);
    output.push({ key: item.key, dir: item.dir === "desc" ? "desc" : "asc" });
  }
  return output;
}

function syncPrimarySort(datasetState) {
  datasetState.sort = datasetState.sorts[0] ? { ...datasetState.sorts[0] } : { key: "", dir: "asc" };
}

function normalizePositiveInt(value, fallback) {
  const parsed = Number.parseInt(value, 10);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
}

function normalizePageSize(value) {
  const size = normalizePositiveInt(value, 10);
  return PAGE_SIZES.includes(size) ? size : 10;
}

function normalizePinnedColumns(input, availableKeys) {
  if (Array.isArray(input.pinnedColumnKeys)) {
    return input.pinnedColumnKeys.filter((key) => availableKeys.includes(key));
  }

  if (typeof input.pinnedColumnKey === "string" && availableKeys.includes(input.pinnedColumnKey)) {
    return [input.pinnedColumnKey];
  }

  return [];
}

function normalizePreset(dataset, input) {
  const availableKeys = sanitizeColumns(dataset.columns).map((column) => column.key);
  return {
    name: String(input.name ?? "").trim() || "Preset",
    visibleKeys: Array.isArray(input.visibleKeys) ? input.visibleKeys.filter((key) => availableKeys.includes(key)) : [...availableKeys],
    sort: normalizeSort(input.sort, availableKeys),
    sorts: normalizeSorts(input.sorts, availableKeys, input.sort),
    size: normalizePageSize(input.size),
    columnWidths: normalizeColumnWidths(input.columnWidths, dataset.columns),
    pinnedColumnKeys: normalizePinnedColumns(input, availableKeys),
    groupBy: typeof input.groupBy === "string" && availableKeys.includes(input.groupBy) ? input.groupBy : "",
    filters: Array.isArray(input.filters) ? input.filters.filter((item) => availableKeys.includes(item.key)).map((item) => ({ ...item, valueTo: item.valueTo ?? "" })) : []
  };
}

function dedupePresets(presets) {
  const map = new Map();
  for (const item of presets) {
    if (item?.name) {
      map.set(item.name, item);
    }
  }
  return [...map.values()];
}

function buildResultColumns(rows, schema) {
  const schemaDetails = schema
    .filter((item) => item && typeof item === "object")
    .map((item) => ({
    key: item.name ?? item.Name ?? "",
    label: item.name ?? item.Name ?? "",
    kind: normalizeKind(item.kind ?? item.Kind ?? inferKindFromRows(rows, item.name ?? item.Name ?? ""))
    }));

  if (schemaDetails.length > 0) {
    return sanitizeColumns(schemaDetails);
  }

  return sanitizeColumns(buildColumnsFromRows(rows));
}

function buildColumnsFromRows(rows) {
  if (!rows.length) {
    return [];
  }

  return Object.keys(rows[0] ?? {}).map((key) => ({
    key,
    label: key,
    kind: inferKindFromRows(rows, key)
  }));
}

function sanitizeColumns(columns) {
  return (Array.isArray(columns) ? columns : [])
    .filter((column) => column && typeof column === "object")
    .map((column) => {
      const key = String(column.key ?? column.name ?? column.Name ?? "").trim();
      const label = String(column.label ?? key).trim();
      const kind = normalizeKind(column.kind ?? "text");
      return key ? { key, label: label || key, kind } : null;
    })
    .filter(Boolean);
}

function inferKindFromRows(rows, key) {
  for (const row of rows) {
    const value = getValue(row, key);
    if (value === null || value === undefined || value === "") {
      continue;
    }

    if (typeof value === "number") {
      return "number";
    }

    if (typeof value === "boolean") {
      return "boolean";
    }

    const text = String(value).trim();
    if (/^-?\d+([.,]\d+)?$/.test(text)) {
      return "number";
    }

    if (/\d{4}-\d{2}-\d{2}|\d{2}\/\d{2}\/\d{4}/.test(text)) {
      return "date";
    }

    if (/^(true|false|sim|nao|não)$/i.test(text)) {
      return "boolean";
    }

    return "text";
  }

  return "text";
}

function normalizeKind(kind) {
  const text = String(kind ?? "").toLowerCase();
  if (text.includes("date") || text.includes("time")) {
    return "date";
  }

  if (text.includes("bool")) {
    return "boolean";
  }

  if (text.includes("int") || text.includes("number") || text.includes("decimal") || text.includes("float")) {
    return "number";
  }

  return "text";
}

function buildDatasetView(dataset, datasetState) {
  const safeColumns = sanitizeColumns(dataset.columns);
  dataset.columns = safeColumns;
  const visibleColumns = safeColumns.filter((column) => datasetState.visibleKeys.includes(column.key));
  const effectiveVisibleColumns = visibleColumns.length > 0 ? visibleColumns : safeColumns.slice(0, 1);
  const allRows = Array.isArray(dataset.preparedRows) && dataset.preparedRows.length === dataset.rows.length
    ? dataset.preparedRows
    : prepareDatasetRows(dataset.rows, safeColumns);

  const filteredRows = allRows.filter((entry) => matchesDataset(entry, dataset, datasetState));
  const sortedRows = sortRows(filteredRows, dataset, datasetState.sorts);
  const displayRows = buildDisplayRows(sortedRows, dataset, datasetState.groupBy);
  const size = normalizePageSize(datasetState.size);
  const pageCount = Math.max(1, Math.ceil(displayRows.length / size));
  const page = Math.min(Math.max(1, normalizePositiveInt(datasetState.page, 1)), pageCount);
  datasetState.page = page;
  datasetState.size = size;
  const start = (page - 1) * size;
  const pageRows = displayRows.slice(start, start + size);
  const sortColumn = safeColumns.find((column) => column.key === datasetState.sorts[0]?.key) ?? null;
  if (effectiveVisibleColumns.length > 0 && !datasetState.visibleKeys.some((key) => effectiveVisibleColumns.some((column) => column.key === key))) {
    datasetState.visibleKeys = effectiveVisibleColumns.map((column) => column.key);
  }

  const virtual = computeVirtualRows(pageRows, datasetState);

  return {
    dataset,
    visibleColumns: effectiveVisibleColumns,
    rows: sortedRows,
    displayRows,
    pageRows,
    virtualRows: virtual.rows,
    virtualTopSpacer: virtual.topSpacer,
    virtualBottomSpacer: virtual.bottomSpacer,
    page,
    pageCount,
    totalRows: allRows.length,
    filteredCount: sortedRows.length,
    size,
    sortColumn,
    sortRules: datasetState.sorts,
    groupBy: datasetState.groupBy,
    availableColumns: safeColumns.filter((column) => !datasetState.visibleKeys.includes(column.key) && (column.label || column.key).toLowerCase().includes(datasetState.columnSearch.toLowerCase())),
    shownColumns: safeColumns.filter((column) => datasetState.visibleKeys.includes(column.key)),
    hasRows: sortedRows.length > 0
  };
}

function clampColumnWidth(value, fallback = MIN_COLUMN_WIDTH) {
  const normalized = normalizePositiveInt(value, fallback);
  return Math.max(MIN_COLUMN_WIDTH, normalized);
}

function defaultColumnWidth(kind) {
  switch (kind) {
    case "number":
      return 160;
    case "date":
      return 180;
    case "boolean":
      return 150;
    default:
      return 240;
  }
}

function estimateColumnWidth(value, kind) {
  const text = stringifyValue(value);
  const maxChars = Math.min(Math.max(text.length, 8), kind === "text" ? 48 : 22);
  const charWidth = kind === "number" ? 9 : 8.2;
  return Math.ceil((maxChars * charWidth) + 48);
}

function buildRowId(row, index) {
  for (const key of ["id", "ID", "Id", "codigo", "Codigo", "key", "Key"]) {
    const value = getValue(row, key);
    if (value !== undefined && value !== null && value !== "") {
      return `${key}:${String(value)}`;
    }
  }

  return `row:${index}`;
}

function prepareDatasetRows(rows, columns) {
  const safeColumns = sanitizeColumns(columns);
  return (Array.isArray(rows) ? rows : []).map((row, index) => ({
    raw: row,
    rowId: buildRowId(row, index),
    searchText: safeColumns.map((column) => stringifyValue(getValue(row, column.key))).join(" ").toLowerCase()
  }));
}

function buildDisplayRows(rows, dataset, groupBy) {
  if (!groupBy) {
    return rows;
  }

  const column = dataset.columns.find((item) => item.key === groupBy);
  if (!column) {
    return rows;
  }

  const groups = new Map();
  for (const entry of rows) {
    const rawValue = getValue(entry.raw, groupBy);
    const value = displayValue(rawValue);
    if (!groups.has(value)) {
      groups.set(value, []);
    }
    groups.get(value).push(entry);
  }

  const display = [];
  let index = 0;
  for (const [value, items] of groups.entries()) {
    const stats = computeGroupSummary(items, dataset.columns);
    display.push({
      kind: "group",
      rowId: `group:${groupBy}:${index}`,
      label: value,
      columnLabel: column.label,
      count: items.length
    });
    display.push(...items);
    display.push({
      kind: "subtotal",
      rowId: `subtotal:${groupBy}:${index}`,
      label: value,
      summary: stats
    });
    index += 1;
  }

  return display;
}

function computeGroupSummary(items, columns) {
  const numericColumns = columns.filter((column) => column.kind === "number");
  return numericColumns.slice(0, 3).map((column) => {
    const values = items
      .map((entry) => parseNumeric(getValue(entry.raw, column.key)))
      .filter((value) => Number.isFinite(value));
    const total = values.reduce((sum, value) => sum + value, 0);
    return {
      key: column.key,
      label: column.label,
      total
    };
  });
}

function computeVirtualRows(rows, datasetState) {
  const viewportHeight = Math.max(240, normalizePositiveInt(datasetState.viewportHeight, 560));
  const scrollTop = Math.max(0, normalizePositiveInt(datasetState.scrollTop, 0));
  const total = rows.length;
  if (total <= 24) {
    return { rows, topSpacer: 0, bottomSpacer: 0 };
  }

  const visibleCount = Math.ceil(viewportHeight / VIRTUAL_ROW_HEIGHT) + VIRTUAL_OVERSCAN;
  const start = Math.max(0, Math.floor(scrollTop / VIRTUAL_ROW_HEIGHT) - Math.floor(VIRTUAL_OVERSCAN / 2));
  const end = Math.min(total, start + visibleCount);
  return {
    rows: rows.slice(start, end),
    topSpacer: start * VIRTUAL_ROW_HEIGHT,
    bottomSpacer: Math.max(0, (total - end) * VIRTUAL_ROW_HEIGHT)
  };
}

function matchesDataset(entry, dataset, datasetState) {
  const search = datasetState.search.trim().toLowerCase();
  if (search && !entry.searchText.includes(search)) {
    return false;
  }

  for (const filter of datasetState.filters) {
    const column = dataset.columns.find((item) => item.key === filter.key);
    if (!column) {
      continue;
    }

    if (!matchesFilter(getValue(entry.raw, column.key), column.kind, filter)) {
      return false;
    }
  }

  return true;
}

function sortRows(rows, dataset, sorts) {
  const rules = Array.isArray(sorts) ? sorts : [];
  if (rules.length === 0) {
    return rows;
  }

  return [...rows].sort((left, right) => {
    for (const sort of rules) {
      const column = dataset.columns.find((item) => item.key === sort.key);
      if (!column) {
        continue;
      }

      const direction = sort.dir === "desc" ? -1 : 1;
      const comparison = compareValues(getValue(left.raw, column.key), getValue(right.raw, column.key), column.kind) * direction;
      if (comparison !== 0) {
        return comparison;
      }
    }

    return 0;
  });
}

function compareValues(left, right, kind) {
  if (left === right) {
    return 0;
  }

  if (left === null || left === undefined || left === "") {
    return 1;
  }

  if (right === null || right === undefined || right === "") {
    return -1;
  }

  if (kind === "number") {
    return parseNumeric(left) - parseNumeric(right);
  }

  if (kind === "date") {
    return parseDate(left) - parseDate(right);
  }

  if (kind === "boolean") {
    return Number(parseBoolean(left)) - Number(parseBoolean(right));
  }

  return stringifyValue(left).localeCompare(stringifyValue(right), undefined, { numeric: true, sensitivity: "base" });
}

function parseNumeric(value) {
  if (typeof value === "number") {
    return value;
  }

  const normalized = String(value).replace(/\./g, "").replace(",", ".");
  const parsed = Number.parseFloat(normalized);
  return Number.isFinite(parsed) ? parsed : 0;
}

function parseDate(value) {
  if (value instanceof Date) {
    return value.getTime();
  }

  const text = String(value).trim();
  if (/^\d{2}\/\d{2}\/\d{4}$/.test(text)) {
    const [day, month, year] = text.split("/");
    return new Date(`${year}-${month}-${day}T00:00:00`).getTime();
  }

  const time = Date.parse(text);
  return Number.isFinite(time) ? time : 0;
}

function parseBoolean(value) {
  if (typeof value === "boolean") {
    return value;
  }

  return /^(true|sim|yes|1)$/i.test(String(value));
}

export function getValue(row, key) {
  if (!row || typeof row !== "object") {
    return undefined;
  }

  if (Object.prototype.hasOwnProperty.call(row, key)) {
    return row[key];
  }

  const match = Object.keys(row).find((candidate) => candidate.toLowerCase() === String(key).toLowerCase());
  return match ? row[match] : undefined;
}

export function stringifyValue(value) {
  if (value === null || value === undefined || value === "") {
    return "";
  }

  return String(value);
}

export function displayValue(value) {
  const text = stringifyValue(value);
  return text || DEFAULT_EMPTY;
}

export function allowedOperators(kind) {
  if (kind === "number" || kind === "date") {
    return ["between", "eq", "neq", "gt", "gte", "lt", "lte"];
  }

  if (kind === "boolean") {
    return ["eq", "neq"];
  }

  return ["contains", "like", "regex", "eq", "neq", "starts", "ends"];
}

function defaultOperator(kind) {
  return kind === "text" ? "contains" : kind === "number" || kind === "date" ? "between" : "eq";
}

export function operatorLabel(operator) {
  return {
    contains: "Contains",
    like: "Like",
    regex: "Regex",
    between: "Between",
    eq: "Equals",
    neq: "Different",
    gt: "Greater than",
    gte: "Greater or equal",
    lt: "Less than",
    lte: "Less or equal",
    starts: "Starts with",
    ends: "Ends with"
  }[operator] ?? operator;
}

function matchesFilter(value, kind, filter) {
  const left = stringifyValue(value);
  const right = stringifyValue(filter.value);
  const rightTo = stringifyValue(filter.valueTo);

  if (kind === "number") {
    if (filter.operator === "between") {
      const leftValue = parseNumeric(left);
      return leftValue >= parseNumeric(right) && leftValue <= parseNumeric(rightTo);
    }
    return compareByOperator(parseNumeric(left), parseNumeric(right), filter.operator);
  }

  if (kind === "date") {
    if (filter.operator === "between") {
      const leftValue = parseDate(left);
      return leftValue >= parseDate(right) && leftValue <= parseDate(rightTo);
    }
    return compareByOperator(parseDate(left), parseDate(right), filter.operator);
  }

  if (kind === "boolean") {
    return compareByOperator(parseBoolean(left), parseBoolean(right), filter.operator);
  }

  const leftLower = left.toLowerCase();
  const rightLower = right.toLowerCase();
  switch (filter.operator) {
    case "contains": return leftLower.includes(rightLower);
    case "starts": return leftLower.startsWith(rightLower);
    case "ends": return leftLower.endsWith(rightLower);
    case "like": return buildLikeRegex(right).test(left);
    case "regex":
      try {
        return new RegExp(right, "i").test(left);
      } catch {
        return false;
      }
    case "eq": return leftLower === rightLower;
    case "neq": return leftLower !== rightLower;
    default: return true;
  }
}

function compareByOperator(left, right, operator) {
  switch (operator) {
    case "eq": return left === right;
    case "neq": return left !== right;
    case "gt": return left > right;
    case "gte": return left >= right;
    case "lt": return left < right;
    case "lte": return left <= right;
    default: return true;
  }
}

function buildLikeRegex(pattern) {
  const escaped = pattern.replace(/[.*+?^${}()|[\]\\]/g, "\\$&").replace(/%/g, ".*").replace(/_/g, ".");
  return new RegExp(`^${escaped}$`, "i");
}

function csvEscape(value) {
  const text = displayValue(value);
  return /[,"\r\n]/.test(text) ? `"${text.replaceAll("\"", "\"\"")}"` : text;
}

function downloadBlob(blob, fileName) {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = fileName;
  anchor.click();
  URL.revokeObjectURL(url);
}

function copyText(text, message, ui = null) {
  navigator.clipboard?.writeText(text).then(() => showToast(message, ui)).catch(() => showToast("Could not copy", ui));
}

function showToast(message, ui) {
  if (!ui) {
    return;
  }

  window.clearTimeout(ui.toastTimer);
  ui.toast = message;
  ui.toastTimer = window.setTimeout(() => {
    ui.toast = "";
  }, 1800);
}

function slugify(value) {
  return String(value ?? "")
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");
}

function buildStateSnapshot(appState) {
  return {
    theme: appState.theme,
    sections: appState.sections,
    datasets: Object.fromEntries(Object.entries(appState.datasets).map(([key, value]) => [key, {
      search: value.search,
      filters: value.filters,
      sorts: value.sorts,
      visibleKeys: value.visibleKeys,
      size: value.size,
      page: value.page,
      groupBy: value.groupBy,
      pinnedColumnKeys: value.pinnedColumnKeys,
      activeCustomPreset: value.activeCustomPreset
    }]))
  };
}

function applyStateSnapshot(state, snapshot, datasets) {
  if (!snapshot || typeof snapshot !== "object") {
    return;
  }

  if (snapshot.theme === "light" || snapshot.theme === "dark") {
    state.theme = snapshot.theme;
  }

  for (const dataset of datasets) {
    const saved = snapshot.datasets?.[dataset.id];
    if (!saved) {
      continue;
    }

    state.datasets[dataset.id] = normalizeDatasetState(dataset, { ...state.datasets[dataset.id], ...saved });
  }
}

function buildOverviewCards(meta, payload, txt) {
  const warnings = meta.warnings ?? [];
  return [
    { label: txt("Linhas", "Rows"), value: new Intl.NumberFormat().format(Number((payload.rows ?? []).length)), icon: "rows" },
    { label: txt("Colunas", "Columns"), value: new Intl.NumberFormat().format(Number((payload.schema ?? []).length || buildColumnsFromRows(payload.rows ?? []).length)), icon: "columns" },
    { label: txt("Tempo", "Duration"), value: meta.duration ?? DEFAULT_EMPTY, icon: "clock" },
    { label: txt("Conexao", "Connection"), value: meta.connectionName ?? DEFAULT_EMPTY, icon: "database" },
    { label: txt("Gerado em", "Generated at"), value: meta.generatedAt ?? DEFAULT_EMPTY, icon: "clock" },
    { label: txt("Descricao", "Description"), value: meta.description || DEFAULT_EMPTY, icon: "clipboard", span: "wide" },
    { label: "Warnings", value: warnings.length > 0 ? warnings.join(" • ") : txt("Nenhum", "None"), icon: "alert", tone: warnings.length > 0 ? "warning" : "" }
  ];
}

export function summaryChipText(filter) {
  return `${filter.key} • ${operatorLabel(filter.operator)} • ${displayValue(filter.value)}`;
}

export function columnTone(kind, value) {
  if (value === null || value === undefined || value === "") {
    return "is-null";
  }

  if (kind === "number") {
    return "is-number";
  }

  if (kind === "date") {
    return "is-date";
  }

  if (kind === "boolean") {
    return "is-boolean";
  }

  return "is-text";
}

function projectRow(row, columns) {
  const output = {};
  for (const column of columns) {
    output[column.key] = getValue(row, column.key);
  }
  return output;
}

function getExportRowsFactory(state, datasetViews) {
  return function getExportRows(id, scope) {
    const view = datasetViews.value[id];
    if (!view) {
      return [];
    }

    if (scope === "selected") {
      const selected = state.datasets[id]?.selectedRows ?? [];
      return view.rows.filter((entry) => selected.includes(entry.rowId));
    }

    if (scope === "page") {
      return view.pageRows.filter((entry) => !entry.kind);
    }

    if (scope === "all") {
      return (view.dataset.preparedRows ?? []).filter((entry) => !entry.kind);
    }

    return view.rows;
  };
}

function computeColumnStats(view, key) {
  if (!view) {
    return null;
  }

  const column = view.dataset.columns.find((item) => item.key === key);
  if (!column) {
    return null;
  }

  const values = view.rows.map((entry) => getValue(entry.raw, key));
  const nonEmpty = values.filter((value) => value !== null && value !== undefined && value !== "");
  const distinct = new Set(nonEmpty.map((value) => stringifyValue(value)));
  const topValues = [...nonEmpty.reduce((map, value) => {
    const text = displayValue(value);
    map.set(text, (map.get(text) ?? 0) + 1);
    return map;
  }, new Map()).entries()].sort((a, b) => b[1] - a[1]).slice(0, 5);

  let min = "";
  let max = "";
  let avg = "";
  if (column.kind === "number") {
    const numeric = nonEmpty.map((value) => parseNumeric(value)).filter((value) => Number.isFinite(value));
    if (numeric.length > 0) {
      min = String(Math.min(...numeric));
      max = String(Math.max(...numeric));
      avg = String((numeric.reduce((sum, value) => sum + value, 0) / numeric.length).toFixed(2));
    }
  } else if (column.kind === "date") {
    const dates = nonEmpty.map((value) => parseDate(value)).filter((value) => Number.isFinite(value) && value > 0);
    if (dates.length > 0) {
      min = new Date(Math.min(...dates)).toISOString();
      max = new Date(Math.max(...dates)).toISOString();
    }
  }

  return {
    key,
    label: column.label,
    kind: column.kind,
    total: values.length,
    nonEmpty: nonEmpty.length,
    nullCount: values.length - nonEmpty.length,
    distinctCount: distinct.size,
    min,
    max,
    avg,
    topValues
  };
}

function formatSummaryChip(filter) {
  if (filter.operator === "between") {
    return `${filter.key} • ${operatorLabel(filter.operator)} • ${displayValue(filter.value)} → ${displayValue(filter.valueTo)}`;
  }

  return `${filter.key} • ${operatorLabel(filter.operator)} • ${displayValue(filter.value)}`;
}

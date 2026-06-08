<script setup>
import "./styles.css";
import { IconGlyph } from "./icons";
import { createReportModel } from "./report-data";

const report = createReportModel();
</script>

<template>
  <div class="report-shell" @click="report.closeContextMenu">
    <header class="page-header">
      <div class="brand-wrap">
        <div class="brand-mark">A</div>
        <div class="brand-copy">
          <div class="brand-name">AkkornStudio</div>
          <div class="brand-sub">{{ report.meta.tabTitle || report.meta.title }}</div>
        </div>
      </div>
      <div class="page-intro">
        <h1>{{ report.meta.title }}</h1>
      </div>
      <div class="toolbar">
        <button class="ghost-btn" type="button" @click="report.toggleTheme"><IconGlyph :name="report.themeIcon" /><span>{{ report.txt("Tema", "Theme") }}</span></button>
        <button class="ghost-btn" type="button" @click="report.copySummary"><IconGlyph name="clipboard" /><span>{{ report.txt("Copiar resumo", "Copy summary") }}</span></button>
        <button class="ghost-btn" type="button" @click="report.copyViewLink"><IconGlyph name="link" /><span>{{ report.txt("Copiar link da visao", "Copy view link") }}</span></button>
        <button class="ghost-btn" type="button" @click="report.copySql" :disabled="!(report.payload.sql ?? '').trim()"><IconGlyph name="code" /><span>{{ report.txt("Copiar SQL", "Copy SQL") }}</span></button>
        <button class="ghost-btn" type="button" @click="report.exportCsv" :disabled="!report.datasetViews.results"><IconGlyph name="download" /><span>{{ report.txt("Exportar visivel CSV", "Export visible CSV") }}</span></button>
        <button class="ghost-btn" type="button" @click="report.exportJson"><IconGlyph name="json" /><span>{{ report.txt("Exportar JSON", "Export JSON") }}</span></button>
        <button class="ghost-btn" type="button" @click="report.openExportModal('results')"><IconGlyph name="downloadPreset" /><span>{{ report.txt("Exportar dados", "Export data") }}</span></button>
      </div>
    </header>

    <main class="main-content">
      <section class="panel">
        <div class="section-head">
          <div>
            <h2>{{ report.txt("Visao geral", "Overview") }}</h2>
            <p class="section-copy">{{ report.txt("Panorama rapido da consulta e do resultado exportado.", "Quick summary of the query and exported result.") }}</p>
          </div>
          <button class="collapse-btn" type="button" @click="report.toggleSection('overview')"><IconGlyph name="chevronDown" /><span>{{ report.state.sections.overview ? report.txt("Expandir", "Expand") : report.txt("Recolher", "Collapse") }}</span></button>
        </div>
        <div v-if="!report.state.sections.overview" class="overview-grid">
          <article v-for="card in (report.overviewCards ?? []).filter(Boolean)" :key="card.label" class="overview-card" :class="[card.span === 'wide' ? 'is-wide' : '', card.tone ? `is-${card.tone}` : '']">
            <div class="overview-icon"><IconGlyph :name="card.icon" /></div>
            <div class="overview-content">
              <div class="overview-label">{{ card.label }}</div>
              <div class="overview-value">{{ card.value }}</div>
            </div>
          </article>
        </div>
      </section>

      <section v-for="dataset in report.datasets" :key="dataset.id" class="panel">
        <div class="section-head">
          <div>
            <h2>{{ dataset.title }}</h2>
            <p class="section-copy">{{ dataset.searchPlaceholder }}</p>
          </div>
          <div class="section-actions">
            <label class="search-field">
              <IconGlyph name="search" />
              <input v-model="report.state.datasets[dataset.id].search" class="field-input compact-search" type="search" :placeholder="dataset.searchPlaceholder" />
            </label>
            <button class="ghost-btn" type="button" @click="report.openFilterModal(dataset.id)"><IconGlyph name="filter" /><span>{{ report.txt("Filtros", "Filters") }}</span></button>
            <button class="ghost-btn" type="button" @click="report.openSortModal(dataset.id)"><IconGlyph name="sort" /><span>{{ report.txt("Ordenacao", "Sorting") }}</span></button>
            <button class="ghost-btn" type="button" @click="report.openGroupModal(dataset.id)"><IconGlyph name="group" /><span>{{ report.txt("Agrupar", "Group") }}</span></button>
            <button class="ghost-btn" type="button" @click="report.openStatsModal(dataset.id)"><IconGlyph name="stats" /><span>{{ report.txt("Estatisticas", "Stats") }}</span></button>
            <button class="ghost-btn" type="button" @click="report.openColumnsModal(dataset.id)"><IconGlyph name="columns" /><span>{{ report.txt("Colunas", "Columns") }}</span></button>
            <button class="ghost-btn" type="button" @click="report.resetDataset(dataset.id)"><IconGlyph name="rotate" /><span>{{ report.txt("Resetar visao", "Reset view") }}</span></button>
            <button class="collapse-btn" type="button" @click="report.toggleSection(dataset.id)"><IconGlyph name="chevronDown" /><span>{{ report.state.sections[dataset.id] ? report.txt("Expandir", "Expand") : report.txt("Recolher", "Collapse") }}</span></button>
          </div>
        </div>

        <div v-if="!report.state.sections[dataset.id]" class="dataset-body">
          <div class="summary-strip">
            <article class="mini-panel">
              <div class="mini-panel-head">
                <div class="mini-panel-title">{{ report.txt("Filtros ativos", "Active filters") }}</div>
                <div class="mini-panel-actions">
                  <button class="icon-only" type="button" @click="report.state.datasets[dataset.id].inlineFiltersOpen = !report.state.datasets[dataset.id].inlineFiltersOpen"><IconGlyph name="chevronDown" /></button>
                  <button class="ghost-btn small" type="button" @click="report.clearFilters(dataset.id)"><IconGlyph name="rotate" /><span>{{ report.txt("Limpar", "Clear") }}</span></button>
                </div>
              </div>
              <div v-if="report.state.datasets[dataset.id].inlineFiltersOpen" class="chips">
                <button v-for="(filter, filterIndex) in report.state.datasets[dataset.id].filters" :key="`${filter.key}-${filterIndex}`" class="chip removable" type="button" @click="report.removeFilter(dataset.id, filterIndex)">
                  <span>{{ report.summaryChipText(filter) }}</span>
                  <IconGlyph name="x" />
                </button>
                <div v-if="report.state.datasets[dataset.id].filters.length === 0" class="chip empty">{{ report.txt("Nenhum filtro aplicado", "No active filters") }}</div>
              </div>
            </article>

            <article class="mini-panel">
              <div class="mini-panel-head">
                <div class="mini-panel-title">{{ report.txt("Colunas visiveis", "Visible columns") }}</div>
                <div class="mini-panel-actions">
                  <button class="icon-only" type="button" @click="report.state.datasets[dataset.id].inlineColumnsOpen = !report.state.datasets[dataset.id].inlineColumnsOpen"><IconGlyph name="chevronDown" /></button>
                  <button class="ghost-btn small" type="button" @click="report.moveAllColumns(dataset.id, 'all')"><IconGlyph name="rotate" /><span>{{ report.txt("Resetar", "Reset") }}</span></button>
                </div>
              </div>
              <div v-if="report.state.datasets[dataset.id].inlineColumnsOpen" class="chips">
                <div v-for="column in (report.visibleColumnSummary(dataset.id) ?? []).filter(Boolean)" :key="column.key" class="chip">{{ column.label ?? column.key ?? report.DEFAULT_EMPTY }}</div>
              </div>
            </article>

            <article class="mini-panel">
              <div class="mini-panel-head">
                <div class="mini-panel-title">{{ report.txt("Ordenacao ativa", "Active sorting") }}</div>
              </div>
              <div class="chips">
                <div v-if="report.groupedByLabel(dataset.id)" class="chip">{{ report.txt("Agrupado por", "Grouped by") }} • {{ report.groupedByLabel(dataset.id) }}</div>
                <div v-for="item in report.activeSortSummary(dataset.id)" :key="`${item.key}-${item.index}`" class="chip">
                  {{ item.index + 1 }} • {{ item.label ?? item.key ?? report.DEFAULT_EMPTY }} • {{ item.dir === 'desc' ? report.txt("Decrescente", "Descending") : report.txt("Crescente", "Ascending") }}
                </div>
                <div v-if="report.activeSortSummary(dataset.id).length === 0" class="chip empty">{{ report.txt("Sem ordenacao ativa", "No active sorting") }}</div>
              </div>
            </article>
          </div>

          <div class="table-shell" :class="{ 'is-page-busy': report.isPageBusy(dataset.id) }">
            <div class="table-meta">
              <div class="table-meta-main">
                <div class="result-count">{{ report.datasetViews[dataset.id].filteredCount }} / {{ report.datasetViews[dataset.id].totalRows }}</div>
                <div v-if="dataset.id === 'results'" class="preset-toolbar">
                  <div class="preset-segmented">
                    <button class="segment-btn" type="button" @click="report.applyPreset(dataset.id, 'summary')">{{ report.txt("Resumo", "Summary") }}</button>
                    <button class="segment-btn" type="button" @click="report.applyPreset(dataset.id, 'audit')">{{ report.txt("Auditoria", "Audit") }}</button>
                    <button class="segment-btn" type="button" @click="report.applyPreset(dataset.id, 'detailed')">{{ report.txt("Detalhado", "Detailed") }}</button>
                  </div>
                  <div class="preset-actions">
                    <button class="icon-chip" type="button" @click="report.saveCustomPreset(dataset.id)" :title="report.txt('Salvar preset', 'Save preset')"><IconGlyph name="save" /></button>
                    <button class="icon-chip" type="button" :disabled="report.state.datasets[dataset.id].customPresets.length === 0" @click="report.exportCustomPresets(dataset.id)" :title="report.txt('Exportar presets', 'Export presets')"><IconGlyph name="downloadPreset" /></button>
                    <button class="icon-chip" type="button" @click="report.importCustomPresets(dataset.id)" :title="report.txt('Importar presets', 'Import presets')"><IconGlyph name="uploadPreset" /></button>
                  </div>
                </div>
                <select v-if="dataset.id === 'results' && report.state.datasets[dataset.id].customPresets.length > 0" :value="report.state.datasets[dataset.id].activeCustomPreset" class="field-input preset-select" @change="report.applyCustomPreset(dataset.id, $event.target.value)">
                  <option value="">{{ report.txt("Presets salvos", "Saved presets") }}</option>
                  <option v-for="preset in report.state.datasets[dataset.id].customPresets" :key="preset.name" :value="preset.name">{{ preset.name }}</option>
                </select>
              </div>
              <div v-if="dataset.id === 'results' && report.state.datasets[dataset.id].customPresets.length > 0" class="preset-secondary">
                <button class="ghost-btn small" type="button" :disabled="!report.state.datasets[dataset.id].activeCustomPreset" @click="report.deleteCustomPreset(dataset.id, report.state.datasets[dataset.id].activeCustomPreset)"><IconGlyph name="trash" /><span>{{ report.txt("Excluir preset", "Delete preset") }}</span></button>
              </div>
              <div class="table-status">
                <span class="status-pill">{{ report.txt("Colunas visiveis", "Visible columns") }}: {{ report.datasetViews[dataset.id].visibleColumns.length }}</span>
                <span class="status-pill">{{ report.txt("Linhas selecionadas", "Selected rows") }}: {{ report.selectedRowsCount(dataset.id) }}</span>
                <span v-if="report.state.datasets[dataset.id].selectedCell" class="status-pill">{{ report.txt("Celula selecionada", "Selected cell") }}</span>
              </div>
              <div v-if="dataset.id === 'results'" class="table-quick-actions">
                <button class="ghost-btn small" type="button" :disabled="!report.state.datasets[dataset.id].selectedCell" @click="report.copySelectedCell(dataset.id)"><IconGlyph name="clipboard" /><span>{{ report.txt("Copiar celula", "Copy cell") }}</span></button>
                <button class="ghost-btn small" type="button" :disabled="report.selectedRowsCount(dataset.id) === 0" @click="report.copySelectedRows(dataset.id)"><IconGlyph name="rows" /><span>{{ report.txt("Copiar linhas", "Copy rows") }}</span></button>
                <button class="ghost-btn small" type="button" :disabled="report.selectedRowsCount(dataset.id) === 0" @click="report.exportSelectedRows(dataset.id)"><IconGlyph name="download" /><span>{{ report.txt("Exportar selecionadas", "Export selected") }}</span></button>
                <button class="ghost-btn small" type="button" :disabled="report.datasetViews[dataset.id].filteredCount === 0" @click="report.exportFilteredRows(dataset.id)"><IconGlyph name="download" /><span>{{ report.txt("Exportar filtradas", "Export filtered") }}</span></button>
              </div>
            </div>

            <div v-if="dataset.id === 'results' && report.quickFilterValues(dataset.id).length > 0" class="quick-filters-wrap">
            <div class="quick-filters">
              <span class="quick-filters-label">{{ report.txt("Filtros rapidos", "Quick filters") }}</span>
              <button v-for="item in report.quickFilterValues(dataset.id)" :key="`${item.key}-${item.value}`" class="chip quick-chip" type="button" @click="report.addQuickFilter(dataset.id, item.key, item.value)">
                <span>{{ item.value }}</span>
                <small>{{ item.count }}</small>
              </button>
            </div>
            </div>

            <div class="table-scroll" :class="report.scrollClass(dataset.id)" @scroll="report.handleTableScroll(dataset.id, $event)" @mouseenter="report.handleTableScroll(dataset.id, $event)">
              <table class="report-table">
                <colgroup>
                  <col class="select-col-width" />
                  <col v-for="column in (report.datasetViews[dataset.id].visibleColumns ?? []).filter(Boolean)" :key="`col-${column.key}`" :style="report.columnStyle(dataset.id, column.key)" />
                </colgroup>
                <thead>
                  <tr>
                    <th class="select-col sticky-select"></th>
                    <th v-for="column in (report.datasetViews[dataset.id].visibleColumns ?? []).filter(Boolean)" :key="column.key" :style="[report.columnStyle(dataset.id, column.key), report.columnStickyStyle(dataset.id, column.key)]" :class="[report.columnStickyClass(dataset.id, column.key)]" @contextmenu="report.openHeaderMenu($event, dataset.id, column.key)">
                      <div class="th-shell">
                        <button class="sort-head" type="button" @click="report.setSortFromHeader(dataset.id, column.key)">
                          <span>{{ column.label ?? column.key ?? report.DEFAULT_EMPTY }}</span>
                          <span v-if="report.state.datasets[dataset.id].sorts[0]?.key === column.key" class="sort-mark">{{ report.state.datasets[dataset.id].sorts[0]?.dir === 'asc' ? '▲' : '▼' }}</span>
                        </button>
                        <button class="resize-handle" type="button" :title="report.txt('Redimensionar coluna', 'Resize column')" @mousedown="report.startResize($event, dataset.id, column.key)" @dblclick.stop="report.autoFitColumn(dataset.id, column.key)">
                          <IconGlyph name="resize" :size="14" />
                        </button>
                      </div>
                    </th>
                  </tr>
                </thead>
                <tbody v-if="report.datasetViews[dataset.id].pageRows.length > 0">
                  <tr v-if="report.datasetViews[dataset.id].virtualTopSpacer > 0" class="virtual-spacer"><td :colspan="Math.max(1, report.datasetViews[dataset.id].visibleColumns.length + 1)" :style="{ height: `${report.datasetViews[dataset.id].virtualTopSpacer}px` }"></td></tr>
                  <template v-for="entry in report.datasetViews[dataset.id].virtualRows" :key="entry.rowId">
                  <tr v-if="entry.kind === 'group'" class="group-row">
                    <td :colspan="Math.max(1, report.datasetViews[dataset.id].visibleColumns.length + 1)">
                      <div class="group-row-content">{{ entry.columnLabel }}: <strong>{{ entry.label }}</strong> <small>({{ entry.count }})</small></div>
                    </td>
                  </tr>
                  <tr v-else-if="entry.kind === 'subtotal'" class="subtotal-row">
                    <td :colspan="Math.max(1, report.datasetViews[dataset.id].visibleColumns.length + 1)">
                      <div class="group-row-content">{{ report.txt("Subtotal", "Subtotal") }} <span v-for="item in entry.summary" :key="item.key" class="subtotal-chip">{{ item.label }}: {{ item.total }}</span></div>
                    </td>
                  </tr>
                  <tr v-else :class="{ 'is-row-selected': report.isRowSelected(dataset.id, entry.rowId) }">
                    <td class="select-col sticky-select"><button class="row-selector" type="button" @click="report.toggleRow(dataset.id, entry.rowId, $event)"><IconGlyph name="rowSelect" :size="14" :stroke-width="2.1" /></button></td>
                    <td v-for="column in (report.datasetViews[dataset.id].visibleColumns ?? []).filter(Boolean)" :key="`${entry.rowId}-${column.key}`" :style="[report.columnStyle(dataset.id, column.key), report.columnStickyStyle(dataset.id, column.key)]" :class="['cell', report.columnTone(column.kind, report.getValue(entry.raw, column.key)), report.columnStickyClass(dataset.id, column.key), { 'is-selected-cell': report.isCellSelected(dataset.id, entry.rowId, column.key) }]" @click="report.selectCellWithEvent(dataset.id, entry.rowId, column.key, $event)" @dblclick="report.isLongText(report.getValue(entry.raw, column.key)) && report.openCell(report.getValue(entry.raw, column.key))" @contextmenu="report.openCellMenu($event, dataset.id, entry.rowId, column.key)">
                      <div class="cell-body">
                        <span class="cell-text">{{ report.displayValue(report.getValue(entry.raw, column.key)) }}</span>
                        <button v-if="report.isLongText(report.getValue(entry.raw, column.key))" class="cell-expand" type="button" @click.stop="report.openCell(report.getValue(entry.raw, column.key))"><IconGlyph name="expand" /></button>
                      </div>
                    </td>
                  </tr>
                  </template>
                  <tr v-if="report.datasetViews[dataset.id].virtualBottomSpacer > 0" class="virtual-spacer"><td :colspan="Math.max(1, report.datasetViews[dataset.id].visibleColumns.length + 1)" :style="{ height: `${report.datasetViews[dataset.id].virtualBottomSpacer}px` }"></td></tr>
                </tbody>
                <tbody v-else>
                  <tr><td :colspan="Math.max(1, report.datasetViews[dataset.id].visibleColumns.length + 1)" class="empty-state">{{ report.txt("Nenhuma linha disponivel", "No rows available") }}</td></tr>
                </tbody>
              </table>
            </div>

            <div v-if="report.isPageBusy(dataset.id)" class="table-loading">
              <div class="table-loading-card">
                <span class="table-loading-spinner"></span>
                <span>{{ report.txt("Atualizando pagina", "Updating page") }}</span>
              </div>
            </div>

            <div class="pager-wrap pager-wrap-bottom">
              <div class="page-size-wrap">
                <label class="field-label">{{ report.txt("Tamanho da pagina", "Page size") }}</label>
                <select v-model.number="report.state.datasets[dataset.id].size" class="field-input compact-select">
                  <option v-for="size in report.PAGE_SIZES" :key="size" :value="size">{{ size }}</option>
                </select>
              </div>
              <div class="pager">
                <button class="icon-only" type="button" :disabled="report.datasetViews[dataset.id].page <= 1" @click="report.changePage(dataset.id, report.datasetViews[dataset.id].page - 1)"><IconGlyph name="left" :title="report.txt('Pagina anterior', 'Previous page')" /></button>
                <div class="pager-readout">{{ report.txt("Pagina", "Page") }} {{ report.datasetViews[dataset.id].page }} {{ report.txt("de", "of") }} {{ report.datasetViews[dataset.id].pageCount }}</div>
                <input class="pager-input" type="number" min="1" :max="report.datasetViews[dataset.id].pageCount" :value="report.datasetViews[dataset.id].page" @change="report.pageJump($event, dataset.id)" />
                <button class="icon-only" type="button" :disabled="report.datasetViews[dataset.id].page >= report.datasetViews[dataset.id].pageCount" @click="report.changePage(dataset.id, report.datasetViews[dataset.id].page + 1)"><IconGlyph name="right" :title="report.txt('Proxima pagina', 'Next page')" /></button>
              </div>
            </div>
          </div>
        </div>
      </section>

      <section v-if="(report.payload.sql ?? '').trim().length > 0" class="panel">
        <div class="section-head">
          <div><h2>SQL</h2><p class="section-copy">{{ report.txt("Consulta completa exportada no relatorio.", "Full query exported in the report.") }}</p></div>
          <button class="collapse-btn" type="button" @click="report.toggleSection('sql')"><IconGlyph name="chevronDown" /><span>{{ report.state.sections.sql ? report.txt("Expandir", "Expand") : report.txt("Recolher", "Collapse") }}</span></button>
        </div>
        <div v-if="!report.state.sections.sql" class="sql-box"><pre>{{ report.payload.sql }}</pre></div>
      </section>
    </main>

    <footer class="page-footer">{{ report.footerText }}</footer>

    <div v-if="report.ui.activeModal === 'filter'" class="modal-shell" @click.self="report.closeModal">
      <div class="modal-card">
        <div class="modal-head">
          <div><div class="modal-title">{{ report.txt("Adicionar filtro", "Add filter") }}</div><div class="section-copy">{{ report.txt("Escolha coluna, condicao e valor.", "Choose column, condition and value.") }}</div></div>
          <button class="icon-only" type="button" @click="report.closeModal"><IconGlyph name="x" /></button>
        </div>
        <div v-if="report.activeDataset() && report.activeDatasetState()" class="modal-body">
          <div class="form-grid">
            <label class="field-stack"><span class="field-label"><IconGlyph name="columns" /> {{ report.txt("Coluna", "Column") }}</span><select v-model="report.activeDatasetState().pendingFilter.key" class="field-input"><option v-for="column in (report.activeDataset().columns ?? []).filter(Boolean)" :key="column.key" :value="column.key">{{ column.label ?? column.key ?? report.DEFAULT_EMPTY }}</option></select></label>
            <label class="field-stack"><span class="field-label"><IconGlyph name="funnel" /> {{ report.txt("Condicao", "Condition") }}</span><select v-model="report.activeDatasetState().pendingFilter.operator" class="field-input"><option v-for="operator in report.allowedOperators((report.activePendingColumn() ?? { kind: 'text' }).kind)" :key="operator" :value="operator">{{ report.operatorLabel(operator) }}</option></select></label>
            <template v-if="(report.activePendingColumn()?.kind ?? 'text') === 'boolean'">
              <label class="field-stack field-span"><span class="field-label"><IconGlyph name="text" /> {{ report.txt("Valor", "Value") }}</span><select v-model="report.activeDatasetState().pendingFilter.value" class="field-input"><option value="true">True</option><option value="false">False</option><option value="sim">Sim</option><option value="nao">Não</option></select></label>
            </template>
            <template v-else-if="report.filterUsesRange((report.activePendingColumn()?.kind ?? 'text'), report.activeDatasetState().pendingFilter.operator)">
              <label class="field-stack"><span class="field-label"><IconGlyph name="text" /> {{ report.txt("De", "From") }}</span><input v-model="report.activeDatasetState().pendingFilter.value" class="field-input" :type="report.filterInputType(report.activePendingColumn()?.kind ?? 'text')" /></label>
              <label class="field-stack"><span class="field-label"><IconGlyph name="text" /> {{ report.txt("Ate", "To") }}</span><input v-model="report.activeDatasetState().pendingFilter.valueTo" class="field-input" :type="report.filterInputType(report.activePendingColumn()?.kind ?? 'text')" /></label>
            </template>
            <label v-else class="field-stack field-span"><span class="field-label"><IconGlyph name="text" /> {{ report.txt("Valor", "Value") }}</span><input v-model="report.activeDatasetState().pendingFilter.value" class="field-input" :type="report.filterInputType(report.activePendingColumn()?.kind ?? 'text')" /></label>
          </div>
        </div>
        <div class="modal-foot"><button class="ghost-btn" type="button" @click="report.addFilter"><IconGlyph name="plus" /><span>{{ report.txt("Adicionar filtro", "Add filter") }}</span></button><button class="ghost-btn" type="button" @click="report.closeModal">{{ report.txt("Fechar", "Close") }}</button></div>
      </div>
    </div>

    <div v-if="report.ui.activeModal === 'sort'" class="modal-shell" @click.self="report.closeModal">
      <div class="modal-card">
        <div class="modal-head">
          <div><div class="modal-title">{{ report.txt("Gerenciar ordenacao", "Manage sorting") }}</div><div class="section-copy">{{ report.txt("Defina prioridade e direcao de cada criterio.", "Set priority and direction for each sort rule.") }}</div></div>
          <button class="icon-only" type="button" @click="report.closeModal"><IconGlyph name="x" /></button>
        </div>
        <div v-if="report.activeDataset() && report.activeDatasetState()" class="modal-body">
          <div class="sort-rules">
            <div v-for="(sortRule, index) in report.activeDatasetState().sorts" :key="`${sortRule.key}-${index}`" class="sort-rule">
              <label class="field-stack"><span class="field-label"><IconGlyph name="sort" /> {{ report.txt("Coluna de ordenacao", "Sort column") }} {{ index + 1 }}</span><select :value="sortRule.key" class="field-input" @change="report.updateSortRuleKey(index, $event.target.value)"><option value="">{{ report.txt("Nenhuma", "None") }}</option><option v-for="column in (report.activeDataset().columns ?? []).filter(Boolean)" :key="column.key" :value="column.key">{{ column.label ?? column.key ?? report.DEFAULT_EMPTY }}</option></select></label>
              <label class="field-stack"><span class="field-label"><IconGlyph name="arrowUpDown" /> {{ report.txt("Direcao", "Direction") }}</span><select :value="sortRule.dir" class="field-input" @change="report.updateSortRuleDirection(index, $event.target.value)"><option value="asc">{{ report.txt("Crescente", "Ascending") }}</option><option value="desc">{{ report.txt("Decrescente", "Descending") }}</option></select></label>
              <button class="ghost-btn small sort-remove" type="button" @click="report.removeSortRule(index)"><IconGlyph name="trash" /><span>{{ report.txt("Remover", "Remove") }}</span></button>
            </div>
            <div v-if="report.activeDatasetState().sorts.length === 0" class="chip empty">{{ report.txt("Sem criterios de ordenacao", "No sort rules yet") }}</div>
          </div>
        </div>
        <div class="modal-foot">
          <button class="ghost-btn" type="button" @click="report.addSortRule"><IconGlyph name="plus" /><span>{{ report.txt("Adicionar nivel", "Add level") }}</span></button>
          <button class="ghost-btn" type="button" @click="report.applySort"><IconGlyph name="check" /><span>{{ report.txt("Aplicar ordenacao", "Apply sorting") }}</span></button>
          <button class="ghost-btn" type="button" @click="report.clearSort"><IconGlyph name="rotate" /><span>{{ report.txt("Resetar", "Reset") }}</span></button>
        </div>
      </div>
    </div>

    <div v-if="report.ui.activeModal === 'group'" class="modal-shell" @click.self="report.closeModal">
      <div class="modal-card">
        <div class="modal-head">
          <div><div class="modal-title">{{ report.txt("Agrupar resultados", "Group results") }}</div><div class="section-copy">{{ report.txt("Escolha uma coluna para agrupar e inserir subtotais.", "Choose a column to group and inject subtotals.") }}</div></div>
          <button class="icon-only" type="button" @click="report.closeModal"><IconGlyph name="x" /></button>
        </div>
        <div v-if="report.activeDatasetState() && report.activeDataset()" class="modal-body">
          <label class="field-stack"><span class="field-label"><IconGlyph name="group" /> {{ report.txt("Agrupar por", "Group by") }}</span><select v-model="report.activeDatasetState().groupBy" class="field-input"><option value="">{{ report.txt("Nenhum", "None") }}</option><option v-for="column in (report.activeDataset().columns ?? []).filter(Boolean)" :key="column.key" :value="column.key">{{ column.label ?? column.key ?? report.DEFAULT_EMPTY }}</option></select></label>
        </div>
        <div class="modal-foot"><button class="ghost-btn" type="button" @click="report.applyGrouping"><IconGlyph name="check" /><span>{{ report.txt("Aplicar agrupamento", "Apply grouping") }}</span></button></div>
      </div>
    </div>

    <div v-if="report.ui.activeModal === 'stats'" class="modal-shell" @click.self="report.closeModal">
      <div class="modal-card">
        <div class="modal-head">
          <div><div class="modal-title">{{ report.txt("Estatisticas da coluna", "Column statistics") }}</div><div class="section-copy">{{ report.txt("Resumo rapido da coluna com base nas linhas filtradas.", "Quick summary for the column based on filtered rows.") }}</div></div>
          <button class="icon-only" type="button" @click="report.closeModal"><IconGlyph name="x" /></button>
        </div>
        <div v-if="report.activeDatasetState() && report.activeDataset()" class="modal-body">
          <label class="field-stack"><span class="field-label"><IconGlyph name="stats" /> {{ report.txt("Coluna", "Column") }}</span><select v-model="report.activeDatasetState().statsColumnKey" class="field-input"><option v-for="column in (report.activeDataset().columns ?? []).filter(Boolean)" :key="column.key" :value="column.key">{{ column.label ?? column.key ?? report.DEFAULT_EMPTY }}</option></select></label>
          <div v-if="report.currentColumnStats(report.ui.modalDatasetId)" class="stats-grid">
            <div class="stat-card"><span>{{ report.txt("Total", "Total") }}</span><strong>{{ report.currentColumnStats(report.ui.modalDatasetId).total }}</strong></div>
            <div class="stat-card"><span>{{ report.txt("Preenchidos", "Filled") }}</span><strong>{{ report.currentColumnStats(report.ui.modalDatasetId).nonEmpty }}</strong></div>
            <div class="stat-card"><span>{{ report.txt("Nulos", "Nulls") }}</span><strong>{{ report.currentColumnStats(report.ui.modalDatasetId).nullCount }}</strong></div>
            <div class="stat-card"><span>{{ report.txt("Distintos", "Distinct") }}</span><strong>{{ report.currentColumnStats(report.ui.modalDatasetId).distinctCount }}</strong></div>
            <div class="stat-card" v-if="report.currentColumnStats(report.ui.modalDatasetId).min"><span>Min</span><strong>{{ report.currentColumnStats(report.ui.modalDatasetId).min }}</strong></div>
            <div class="stat-card" v-if="report.currentColumnStats(report.ui.modalDatasetId).max"><span>Max</span><strong>{{ report.currentColumnStats(report.ui.modalDatasetId).max }}</strong></div>
            <div class="stat-card" v-if="report.currentColumnStats(report.ui.modalDatasetId).avg"><span>Avg</span><strong>{{ report.currentColumnStats(report.ui.modalDatasetId).avg }}</strong></div>
          </div>
          <div v-if="report.currentColumnStats(report.ui.modalDatasetId)?.topValues?.length > 0" class="chips">
            <div v-for="item in report.currentColumnStats(report.ui.modalDatasetId).topValues" :key="item[0]" class="chip">{{ item[0] }} <small>{{ item[1] }}</small></div>
          </div>
        </div>
      </div>
    </div>

    <div v-if="report.ui.activeModal === 'export'" class="modal-shell" @click.self="report.closeModal">
      <div class="modal-card">
        <div class="modal-head">
          <div><div class="modal-title">{{ report.txt("Exportar dados", "Export data") }}</div><div class="section-copy">{{ report.txt("Escolha escopo, formato e colunas.", "Choose scope, format and columns.") }}</div></div>
          <button class="icon-only" type="button" @click="report.closeModal"><IconGlyph name="x" /></button>
        </div>
        <div class="modal-body">
          <div class="form-grid">
            <label class="field-stack"><span class="field-label">{{ report.txt("Escopo", "Scope") }}</span><select v-model="report.ui.exportOptions.scope" class="field-input"><option value="filtered">{{ report.txt("Filtrado", "Filtered") }}</option><option value="page">{{ report.txt("Pagina atual", "Current page") }}</option><option value="selected">{{ report.txt("Selecionadas", "Selected") }}</option><option value="all">{{ report.txt("Todas", "All") }}</option></select></label>
            <label class="field-stack"><span class="field-label">{{ report.txt("Formato", "Format") }}</span><select v-model="report.ui.exportOptions.format" class="field-input"><option value="csv">CSV</option><option value="json">JSON</option></select></label>
            <label class="field-stack field-span"><span class="field-label">{{ report.txt("Colunas", "Columns") }}</span><select v-model="report.ui.exportOptions.visibleOnly" class="field-input"><option :value="true">{{ report.txt("Apenas visiveis", "Visible only") }}</option><option :value="false">{{ report.txt("Todas as colunas", "All columns") }}</option></select></label>
          </div>
        </div>
        <div class="modal-foot"><button class="ghost-btn" type="button" @click="report.exportDatasetFromModal"><IconGlyph name="download" /><span>{{ report.txt("Exportar", "Export") }}</span></button></div>
      </div>
    </div>

    <div v-if="report.ui.activeModal === 'columns'" class="modal-shell" @click.self="report.closeModal">
      <div class="modal-card modal-wide">
        <div class="modal-head">
          <div><div class="modal-title">{{ report.txt("Colunas visiveis", "Visible columns") }}</div><div class="section-copy">{{ report.txt("Gerencie o que permanece na tabela.", "Manage what stays visible in the table.") }}</div></div>
          <button class="icon-only" type="button" @click="report.closeModal"><IconGlyph name="x" /></button>
        </div>
        <div v-if="report.activeDataset() && report.activeDatasetState()" class="modal-body">
          <div class="picklist">
            <div class="pick-pane">
              <label class="field-label">{{ report.txt("Disponiveis", "Available") }}</label>
              <input v-model="report.activeDatasetState().columnSearch" class="field-input" type="search" :placeholder="report.txt('Buscar colunas', 'Search columns')" />
              <div class="pick-list">
                <button v-for="column in ((report.datasetViews[report.ui.modalDatasetId]?.availableColumns) ?? []).filter(Boolean)" :key="column.key" class="pick-item" :class="{ active: report.activeDatasetState().pickSelection.available.includes(column.key) }" type="button" @click="report.choosePick(report.ui.modalDatasetId, 'available', column.key, true)">{{ column.label ?? column.key ?? report.DEFAULT_EMPTY }}</button>
              </div>
            </div>
            <div class="pick-actions">
              <button class="ghost-btn" type="button" @click="report.moveColumns(report.ui.modalDatasetId, 'add')">&gt;</button>
              <button class="ghost-btn" type="button" @click="report.moveColumns(report.ui.modalDatasetId, 'remove')">&lt;</button>
              <button class="ghost-btn" type="button" @click="report.moveAllColumns(report.ui.modalDatasetId, 'all')">&gt;&gt;</button>
              <button class="ghost-btn" type="button" @click="report.moveAllColumns(report.ui.modalDatasetId, 'none')">&lt;&lt;</button>
              <button class="ghost-btn" type="button" @click="report.moveVisibleColumnOrder(report.ui.modalDatasetId, 'up')"><IconGlyph name="up" /></button>
              <button class="ghost-btn" type="button" @click="report.moveVisibleColumnOrder(report.ui.modalDatasetId, 'down')"><IconGlyph name="chevronDown" /></button>
            </div>
            <div class="pick-pane">
              <label class="field-label">{{ report.txt("Em exibicao", "Showing") }}</label>
              <div class="pick-list">
                <button v-for="column in ((report.datasetViews[report.ui.modalDatasetId]?.shownColumns) ?? []).filter(Boolean)" :key="column.key" class="pick-item" :class="{ active: report.activeDatasetState().pickSelection.visible.includes(column.key) }" type="button" @click="report.choosePick(report.ui.modalDatasetId, 'visible', column.key, true)">{{ column.label ?? column.key ?? report.DEFAULT_EMPTY }}</button>
              </div>
            </div>
          </div>
        </div>
        <div class="modal-foot"><button class="ghost-btn" type="button" @click="report.closeModal"><IconGlyph name="check" /><span>{{ report.txt("Concluir", "Done") }}</span></button></div>
      </div>
    </div>

    <div v-if="report.ui.activeModal === 'cell'" class="modal-shell" @click.self="report.closeModal">
      <div class="modal-card modal-cell">
        <div class="modal-head">
          <div><div class="modal-title">{{ report.txt("Conteudo completo", "Full content") }}</div><div class="section-copy">{{ report.txt("Visualizacao ampliada da celula selecionada.", "Expanded view for the selected cell.") }}</div></div>
          <button class="icon-only" type="button" @click="report.closeModal"><IconGlyph name="x" /></button>
        </div>
        <div class="modal-body"><pre class="cell-modal-text">{{ report.ui.longCellValue }}</pre></div>
      </div>
    </div>

    <div v-if="report.ui.contextMenu?.type === 'cell'" class="context-menu" :style="{ left: `${report.ui.contextMenu.x}px`, top: `${report.ui.contextMenu.y}px` }" @click.stop>
      <button class="context-item" type="button" @click="report.copyContextValue()"><IconGlyph name="clipboard" /><span>{{ report.txt("Copiar valor", "Copy value") }}</span></button>
      <button class="context-item" type="button" @click="report.contextFilterByValue()"><IconGlyph name="filter" /><span>{{ report.txt("Filtrar por este valor", "Filter by this value") }}</span></button>
      <button class="context-item" type="button" @click="report.contextHideColumn()"><IconGlyph name="columns" /><span>{{ report.txt("Ocultar coluna", "Hide column") }}</span></button>
      <button class="context-item" type="button" @click="report.openCell(report.ui.contextMenu.value)"><IconGlyph name="expand" /><span>{{ report.txt("Abrir valor completo", "Open full value") }}</span></button>
    </div>

    <div v-if="report.ui.contextMenu?.type === 'header'" class="context-menu" :style="{ left: `${report.ui.contextMenu.x}px`, top: `${report.ui.contextMenu.y}px` }" @click.stop>
      <button class="context-item" type="button" @click="report.contextSort('asc', 'primary')"><IconGlyph name="sortAsc" /><span>{{ report.txt("Ordenar crescente", "Sort ascending") }}</span></button>
      <button class="context-item" type="button" @click="report.contextSort('desc', 'primary')"><IconGlyph name="sortDesc" /><span>{{ report.txt("Ordenar decrescente", "Sort descending") }}</span></button>
      <button class="context-item" type="button" @click="report.contextSort('asc', 'secondary')"><IconGlyph name="sortAppend" /><span>{{ report.txt("Adicionar como criterio", "Add as sort level") }}</span></button>
      <button class="context-item" type="button" @click="report.autoFitColumn(report.ui.contextMenu.datasetId, report.ui.contextMenu.key)"><IconGlyph name="resize" /><span>{{ report.txt("Auto ajustar coluna", "Auto fit column") }}</span></button>
      <button class="context-item" type="button" @click="report.togglePinnedColumn()"><IconGlyph name="pin" /><span>{{ report.isPinnedColumn(report.ui.contextMenu.datasetId, report.ui.contextMenu.key) ? report.txt("Desafixar coluna", "Unpin column") : report.txt("Fixar coluna", "Pin column") }}</span></button>
      <button class="context-item" type="button" @click="report.contextHideColumn()"><IconGlyph name="columns" /><span>{{ report.txt("Ocultar coluna", "Hide column") }}</span></button>
      <button class="context-item" type="button" @click="report.contextShowOnlyColumn()"><IconGlyph name="check" /><span>{{ report.txt("Mostrar apenas esta", "Show only this") }}</span></button>
    </div>

    <div v-if="report.ui.toast" class="toast">{{ report.ui.toast }}</div>
  </div>
</template>

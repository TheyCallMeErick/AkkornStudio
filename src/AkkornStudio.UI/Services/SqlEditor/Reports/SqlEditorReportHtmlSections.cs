using System.Text;

namespace AkkornStudio.UI.Services.SqlEditor.Reports;

internal static class SqlEditorReportHtmlSections
{
    public static string BuildFilterAndOrderPanels(string prefix)
    {
        return BuildFilterAndColumnPanels(prefix);
    }

    public static string BuildResultsSection()
    {
        return """
  <section id="s-results">
    <div class="section-header">
      <h2><span class="section-icon" aria-hidden="true"><svg viewBox="0 0 16 16" focusable="false"><path d="M2 2h12v3H2V2Zm0 4h12v3H2V6Zm0 4h12v3H2v-3Z"/></svg></span><span data-i18n="section_query_results">Query Results</span></h2>
      <div class="controls">
        <input id="results-search" class="table-input" type="search" placeholder="Filter results" data-i18n-placeholder="filter_results_placeholder"/>
        <div class="page-size-group" data-table-page-size="results">
          <button type="button" class="page-size-btn is-active" data-page-size="10">10</button>
          <button type="button" class="page-size-btn" data-page-size="25">25</button>
          <button type="button" class="page-size-btn" data-page-size="50">50</button>
          <button type="button" class="page-size-btn" data-page-size="100">100</button>
        </div>
        <button id="results-reset-view" class="collapse-btn" type="button" data-i18n="button_reset_view">Reset View</button>
        <button class="collapse-btn" data-collapse-target="s-results" type="button">▾ Collapse</button>
      </div>
    </div>
    <div class="section-body">
"""
        + BuildFilterAndColumnPanels("results")
        + """
      <div id="results-body"></div>
    </div>
  </section>
""";
    }

    public static string BuildSchemaSection()
    {
        return """
  <section id="s-schema" class="is-collapsed">
    <div class="section-header">
      <h2><span class="section-icon" aria-hidden="true"><svg viewBox="0 0 16 16" focusable="false"><path d="M2 3h12v2H2V3Zm0 4h12v2H2V7Zm0 4h12v2H2v-2Z"/></svg></span><span data-i18n="section_schema_profile">Columns and Schema</span></h2>
      <div class="controls">
        <input id="schema-search" class="table-input" type="search" placeholder="Filter schema" data-i18n-placeholder="filter_schema_placeholder"/>
        <div class="page-size-group" data-table-page-size="schema">
          <button type="button" class="page-size-btn is-active" data-page-size="10">10</button>
          <button type="button" class="page-size-btn" data-page-size="25">25</button>
          <button type="button" class="page-size-btn" data-page-size="50">50</button>
          <button type="button" class="page-size-btn" data-page-size="100">100</button>
        </div>
        <button id="schema-reset-view" class="collapse-btn" type="button" data-i18n="button_reset_view">Reset View</button>
        <button class="collapse-btn" data-collapse-target="s-schema" type="button">▸ Expand</button>
      </div>
    </div>
    <div class="section-body">
"""
        + BuildFilterAndColumnPanels("schema")
        + """
      <div id="schema-body"></div>
    </div>
  </section>
""";
    }

    public static string BuildFilterAndColumnPanels(string prefix)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"      <div class=\"subpanel\" id=\"{prefix}-filters-panel\">");
        sb.AppendLine("        <div class=\"subpanel-header\">");
        sb.AppendLine("          <div class=\"subpanel-title\" data-i18n=\"subpanel_filters\">Filters</div>");
        sb.AppendLine($"          <button class=\"tool-toggle\" type=\"button\" data-subcollapse-target=\"{prefix}-filters-panel\" aria-expanded=\"true\">▾ Collapse</button>");
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div class=\"subpanel-body\">");
        sb.AppendLine("        <div class=\"filter-toolbar\">");
        sb.AppendLine($"          <select id=\"{prefix}-filter-col\" class=\"table-select\"></select>");
        sb.AppendLine($"          <div id=\"{prefix}-filter-op\" class=\"operator-group\"></div>");
        sb.AppendLine($"          <div class=\"filter-value-wrap\" id=\"{prefix}-filter-value-wrap\">");
        sb.AppendLine($"            <input id=\"{prefix}-filter-val\" class=\"table-input\" type=\"text\" placeholder=\"Filter value\" data-i18n-placeholder=\"filter_value_placeholder\"/>");
        sb.AppendLine("          </div>");
        sb.AppendLine($"          <button id=\"{prefix}-add-filter\" class=\"collapse-btn\" type=\"button\" data-i18n=\"filter_add\">Add Filter</button>");
        sb.AppendLine($"          <button id=\"{prefix}-clear-filters\" class=\"collapse-btn\" type=\"button\" data-i18n=\"filter_clear\">Clear Filters</button>");
        sb.AppendLine("        </div>");
        sb.AppendLine($"        <div class=\"subpanel-caption\"><span data-i18n=\"filters_active\">Active filters</span>: <strong id=\"{prefix}-filter-count\">0</strong></div>");
        sb.AppendLine($"        <div class=\"filter-list\" id=\"{prefix}-filter-list\"></div>");
        sb.AppendLine("        </div>");
        sb.AppendLine("      </div>");

        sb.AppendLine($"      <div class=\"subpanel\" id=\"{prefix}-columns-panel\">");
        sb.AppendLine("        <div class=\"subpanel-header\">");
        sb.AppendLine("          <div class=\"subpanel-title\" data-i18n=\"subpanel_columns\">Columns</div>");
        sb.AppendLine($"          <button class=\"tool-toggle\" type=\"button\" data-subcollapse-target=\"{prefix}-columns-panel\" aria-expanded=\"true\">▾ Collapse</button>");
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div class=\"subpanel-body\">");
        sb.AppendLine($"          <input id=\"{prefix}-columns-search\" class=\"table-input\" type=\"search\" placeholder=\"Filter columns\" data-i18n-placeholder=\"filter_columns_placeholder\"/>");
        sb.AppendLine($"          <div class=\"filter-list\" id=\"{prefix}-column-list\"></div>");
        sb.AppendLine("          <div class=\"filter-toolbar\">");
        sb.AppendLine($"            <button id=\"{prefix}-columns-all\" class=\"collapse-btn\" type=\"button\" data-i18n=\"columns_show_all\">Show All</button>");
        sb.AppendLine($"            <button id=\"{prefix}-columns-none\" class=\"collapse-btn\" type=\"button\" data-i18n=\"columns_hide_all\">Hide All</button>");
        sb.AppendLine($"            <button id=\"{prefix}-columns-reset\" class=\"collapse-btn\" type=\"button\" data-i18n=\"columns_reset\">Reset Columns</button>");
        sb.AppendLine("          </div>");
        sb.AppendLine("        </div>");
        sb.AppendLine("      </div>");

        return sb.ToString();
    }
}

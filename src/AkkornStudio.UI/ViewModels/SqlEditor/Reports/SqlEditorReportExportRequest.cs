namespace AkkornStudio.UI.ViewModels;

public sealed record SqlEditorReportExportRequest(
    SqlEditorReportType ReportType,
    string FilePath,
    string Title,
    string Description,
    SqlEditorReportExportProfile Profile,
    SqlEditorReportMetadataLevel MetadataLevel,
    SqlEditorReportEmptyValueDisplayMode EmptyValueDisplayMode,
    bool IncludeSchema,
    bool IncludeSql,
    bool IncludeLineage
)
{
    public bool IncludeMetadata => MetadataLevel != SqlEditorReportMetadataLevel.None;

    public bool IncludeNodeDetails => IncludeLineage;

    public bool UseDashForEmptyFields => EmptyValueDisplayMode == SqlEditorReportEmptyValueDisplayMode.Dash;
}

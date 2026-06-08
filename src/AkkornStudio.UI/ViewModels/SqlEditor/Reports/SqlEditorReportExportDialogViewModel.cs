using System.Collections.ObjectModel;
using System.IO;
using AkkornStudio.UI.Extensions;
using AkkornStudio.UI.Services.Localization;

namespace AkkornStudio.UI.ViewModels;

public sealed class SqlEditorReportExportDialogViewModel : ViewModelBase
{
    private SqlEditorReportTypeOption? _selectedType;
    private string _fileName;
    private string _title;
    private string _description;
    private SqlEditorReportTypeOption? _lastSelectedType;
    private bool _includeSchema;
    private bool _includeSql;
    private bool _includeLineage;
    private SqlEditorReportExportProfile _profile;
    private SqlEditorReportMetadataLevel _metadataLevel;
    private SqlEditorReportEmptyValueDisplayMode _emptyValueDisplayMode;

    public SqlEditorReportExportDialogViewModel(string defaultTitle)
    {
        string sanitizedTitle = string.IsNullOrWhiteSpace(defaultTitle)
            ? L("sqlEditor.export.defaultFileBase", "relatorio")
            : defaultTitle.Trim();

        ReportTypes =
        [
            new SqlEditorReportTypeOption(
                SqlEditorReportType.HtmlFullFeature,
                L("sqlEditor.export.type.html.title", "Relatorio HTML completo"),
                L("sqlEditor.export.type.html.description", "Artefato HTML autonomo e orientado a SQL para auditoria offline."),
                "html"),
            new SqlEditorReportTypeOption(
                SqlEditorReportType.JsonContract,
                L("sqlEditor.export.type.json.title", "Contrato de execucao JSON"),
                L("sqlEditor.export.type.json.description", "Payload legivel por maquina com SQL, metadados e resultado da execucao."),
                "json"),
            new SqlEditorReportTypeOption(
                SqlEditorReportType.CsvData,
                L("sqlEditor.export.type.csv.title", "Exportacao de dados CSV"),
                L("sqlEditor.export.type.csv.description", "Apenas dados tabulares do resultado, ideal para planilhas."),
                "csv"),
            new SqlEditorReportTypeOption(
                SqlEditorReportType.ExcelWorkbook,
                L("sqlEditor.export.type.xlsx.title", "Exportacao de pasta Excel"),
                L("sqlEditor.export.type.xlsx.description", "Pasta de trabalho com os dados tabulares do resultado."),
                "xlsx"),
        ];

        _selectedType = ReportTypes[0];
        _lastSelectedType = _selectedType;
        _fileName = BuildSuggestedFileName(sanitizedTitle, _selectedType.DefaultExtension);
        _title = sanitizedTitle;
        _description = string.Empty;
        _includeSchema = true;
        _includeSql = true;
        _includeLineage = false;
        _profile = SqlEditorReportExportProfile.Technical;
        _metadataLevel = SqlEditorReportMetadataLevel.Essential;
        _emptyValueDisplayMode = SqlEditorReportEmptyValueDisplayMode.Blank;
    }

    public ObservableCollection<SqlEditorReportTypeOption> ReportTypes { get; }

    public SqlEditorReportTypeOption? SelectedType
    {
        get => _selectedType;
        set
        {
            if (!Set(ref _selectedType, value) || value is null)
                return;

            string previousExtension = _lastSelectedType?.DefaultExtension ?? value.DefaultExtension;
            FileName = FileName.EnsureExtension(previousExtension, value.DefaultExtension);
            _lastSelectedType = value;

            ApplyTypeDefaults(value.Type);
            RaisePropertyChanged(nameof(ShowOptions));
            RaisePropertyChanged(nameof(ShowIncludeSchema));
            RaisePropertyChanged(nameof(ShowProfileOptions));
            RaisePropertyChanged(nameof(ShowMetadataOptions));
            RaisePropertyChanged(nameof(ShowSqlOptions));
            RaisePropertyChanged(nameof(ShowLineageOptions));
            RaisePropertyChanged(nameof(CanConfirm));
        }
    }

    public string FileName
    {
        get => _fileName;
        set
        {
            if (!Set(ref _fileName, value))
                return;

            RaisePropertyChanged(nameof(CanConfirm));
        }
    }

    public string Title
    {
        get => _title;
        set
        {
            if (!Set(ref _title, value))
                return;

            RaisePropertyChanged(nameof(CanConfirm));
        }
    }

    public string Description
    {
        get => _description;
        set => Set(ref _description, value);
    }

    public bool IncludeSchema
    {
        get => _includeSchema;
        set => Set(ref _includeSchema, value);
    }

    public bool IncludeSql
    {
        get => _includeSql;
        set => Set(ref _includeSql, value);
    }

    public bool IncludeLineage
    {
        get => _includeLineage;
        set => Set(ref _includeLineage, value);
    }

    public SqlEditorReportExportProfile Profile
    {
        get => _profile;
        set => Set(ref _profile, value);
    }

    public SqlEditorReportMetadataLevel MetadataLevel
    {
        get => _metadataLevel;
        set => Set(ref _metadataLevel, value);
    }

    public SqlEditorReportEmptyValueDisplayMode EmptyValueDisplayMode
    {
        get => _emptyValueDisplayMode;
        set => Set(ref _emptyValueDisplayMode, value);
    }

    public bool ShowOptions => SelectedType?.Type is SqlEditorReportType.HtmlFullFeature or SqlEditorReportType.JsonContract;

    public bool ShowIncludeSchema => SelectedType?.Type is SqlEditorReportType.HtmlFullFeature or SqlEditorReportType.JsonContract;

    public bool ShowProfileOptions => SelectedType?.Type is SqlEditorReportType.HtmlFullFeature or SqlEditorReportType.JsonContract;

    public bool ShowMetadataOptions => SelectedType?.Type is SqlEditorReportType.HtmlFullFeature or SqlEditorReportType.JsonContract;

    public bool ShowSqlOptions => SelectedType?.Type is SqlEditorReportType.HtmlFullFeature or SqlEditorReportType.JsonContract;

    public bool ShowLineageOptions => SelectedType?.Type == SqlEditorReportType.JsonContract;

    public bool CanConfirm =>
        SelectedType is not null
        && !string.IsNullOrWhiteSpace(FileName)
        && !string.IsNullOrWhiteSpace(Title);

    public SqlEditorReportExportRequest BuildRequest(string filePath)
    {
        if (SelectedType is null)
            throw new InvalidOperationException(L("sqlEditor.export.error.typeRequired", "Um tipo de relatorio deve ser selecionado antes da exportacao."));

        return new SqlEditorReportExportRequest(
            ReportType: SelectedType.Type,
            FilePath: filePath,
            Title: string.IsNullOrWhiteSpace(Title) ? L("sqlEditor.export.defaultTitle", "Relatorio SQL") : Title.Trim(),
            Description: string.IsNullOrWhiteSpace(Description) ? string.Empty : Description.Trim(),
            Profile: Profile,
            MetadataLevel: MetadataLevel,
            EmptyValueDisplayMode: EmptyValueDisplayMode,
            IncludeSchema: IncludeSchema,
            IncludeSql: IncludeSql,
            IncludeLineage: IncludeLineage);
    }

    public string SuggestedExtension => SelectedType?.DefaultExtension ?? "html";

    public void SlugifyTitleIntoFileName()
    {
        string extension = SuggestedExtension;
        string slug = Title.ToSlugCase();
        FileName = (string.IsNullOrWhiteSpace(slug) ? "report" : slug).EnsureExtension(extension);
    }

    public void ApplyTitleTransform(string mode)
    {
        Title = Title.TransformText(mode);
    }

    public void ApplyFileNameTransform(string mode)
    {
        string extension = Path.GetExtension(FileName);
        string baseName = Path.GetFileNameWithoutExtension(FileName);
        string transformed = baseName.TransformText(mode);
        string nextBase = string.IsNullOrWhiteSpace(transformed) ? "report" : transformed;
        FileName = string.IsNullOrWhiteSpace(extension)
            ? nextBase
            : nextBase + extension.ToLowerInvariant();
    }

    private static string BuildSuggestedFileName(string title, string extension)
    {
        return title.ToSafeFileBase().EnsureExtension(extension);
    }

    private void ApplyTypeDefaults(SqlEditorReportType reportType)
    {
        switch (reportType)
        {
            case SqlEditorReportType.HtmlFullFeature:
                IncludeSchema = true;
                IncludeSql = true;
                IncludeLineage = false;
                if (MetadataLevel == SqlEditorReportMetadataLevel.None)
                    MetadataLevel = SqlEditorReportMetadataLevel.Essential;
                break;
            case SqlEditorReportType.JsonContract:
                IncludeSchema = true;
                IncludeSql = true;
                IncludeLineage = false;
                break;
            case SqlEditorReportType.CsvData:
            case SqlEditorReportType.ExcelWorkbook:
                IncludeSchema = false;
                IncludeSql = false;
                IncludeLineage = false;
                MetadataLevel = SqlEditorReportMetadataLevel.None;
                break;
        }
    }

    private static string L(string key, string fallback)
    {
        string value = LocalizationService.Instance[key];
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }
}

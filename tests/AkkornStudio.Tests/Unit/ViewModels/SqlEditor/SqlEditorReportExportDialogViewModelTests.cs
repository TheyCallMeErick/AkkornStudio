using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.ViewModels.SqlEditor;

public sealed class SqlEditorReportExportDialogViewModelTests
{
    [Fact]
    public void Constructor_InitializesDefaultsForHtmlReport()
    {
        var sut = new SqlEditorReportExportDialogViewModel("Quarterly Audit");

        Assert.Equal(4, sut.ReportTypes.Count);
        Assert.Equal(SqlEditorReportType.HtmlFullFeature, sut.SelectedType?.Type);
        Assert.Equal("Quarterly_Audit.html", sut.FileName);
        Assert.Equal("Quarterly Audit", sut.Title);
        Assert.Equal(string.Empty, sut.Description);
        Assert.True(sut.IncludeSchema);
        Assert.True(sut.IncludeSql);
        Assert.False(sut.IncludeLineage);
        Assert.Equal(SqlEditorReportExportProfile.Technical, sut.Profile);
        Assert.Equal(SqlEditorReportMetadataLevel.Essential, sut.MetadataLevel);
        Assert.Equal(SqlEditorReportEmptyValueDisplayMode.Blank, sut.EmptyValueDisplayMode);
        Assert.True(sut.ShowProfileOptions);
        Assert.True(sut.ShowMetadataOptions);
        Assert.True(sut.ShowSqlOptions);
        Assert.False(sut.ShowLineageOptions);
        Assert.True(sut.CanConfirm);
    }

    [Fact]
    public void SelectedType_WhenChangedToJson_UpdatesFileExtensionAndVisibility()
    {
        var sut = new SqlEditorReportExportDialogViewModel("Test");

        sut.FileName = "custom_name";
        sut.SelectedType = sut.ReportTypes.Single(type => type.Type == SqlEditorReportType.JsonContract);

        Assert.Equal("custom_name.json", sut.FileName);
        Assert.True(sut.ShowLineageOptions);
        Assert.Equal("json", sut.SuggestedExtension);
    }

    [Fact]
    public void BuildRequest_TrimsValuesAndUsesNewOptions()
    {
        var sut = new SqlEditorReportExportDialogViewModel("Test");
        sut.SelectedType = sut.ReportTypes.Single(type => type.Type == SqlEditorReportType.JsonContract);
        sut.Title = "  SQL Export  ";
        sut.Description = "  Support payload  ";
        sut.IncludeSchema = false;
        sut.IncludeSql = false;
        sut.IncludeLineage = true;
        sut.Profile = SqlEditorReportExportProfile.Audit;
        sut.MetadataLevel = SqlEditorReportMetadataLevel.Complete;
        sut.EmptyValueDisplayMode = SqlEditorReportEmptyValueDisplayMode.NullLiteral;

        SqlEditorReportExportRequest request = sut.BuildRequest("/tmp/report.json");

        Assert.Equal(SqlEditorReportType.JsonContract, request.ReportType);
        Assert.Equal("/tmp/report.json", request.FilePath);
        Assert.Equal("SQL Export", request.Title);
        Assert.Equal("Support payload", request.Description);
        Assert.False(request.IncludeSchema);
        Assert.False(request.IncludeSql);
        Assert.True(request.IncludeLineage);
        Assert.Equal(SqlEditorReportExportProfile.Audit, request.Profile);
        Assert.Equal(SqlEditorReportMetadataLevel.Complete, request.MetadataLevel);
        Assert.Equal(SqlEditorReportEmptyValueDisplayMode.NullLiteral, request.EmptyValueDisplayMode);
    }

    [Fact]
    public void CanConfirm_ReturnsFalseWhenFileNameOrTitleAreBlank()
    {
        var sut = new SqlEditorReportExportDialogViewModel("Test");

        sut.FileName = " ";
        Assert.False(sut.CanConfirm);

        sut.FileName = "report.html";
        sut.Title = " ";
        Assert.False(sut.CanConfirm);
    }

    [Fact]
    public void SlugifyTitleIntoFileName_UsesTitleAndExtension()
    {
        var sut = new SqlEditorReportExportDialogViewModel("Relatorio Inicial");
        sut.Title = "Mapa de Cobranca 2026";

        sut.SlugifyTitleIntoFileName();

        Assert.Equal("mapa-de-cobranca-2026.html", sut.FileName);
    }

    [Fact]
    public void ApplyTransforms_UpdatesTitleAndFileName()
    {
        var sut = new SqlEditorReportExportDialogViewModel("Relatorio Inicial");
        sut.Title = "relatorio final";
        sut.FileName = "Meu Arquivo.HTML";

        sut.ApplyTitleTransform("upper");
        sut.ApplyFileNameTransform("slug");

        Assert.Equal("RELATORIO FINAL", sut.Title);
        Assert.Equal("meu-arquivo.html", sut.FileName);
    }
}

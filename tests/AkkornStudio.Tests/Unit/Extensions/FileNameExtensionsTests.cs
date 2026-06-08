using AkkornStudio.UI.Extensions;

namespace AkkornStudio.Tests.Unit.Extensions;

public sealed class FileNameExtensionsTests
{
    [Fact]
    public void EnsureExtension_ReplacesPreviousExtension()
    {
        string result = "report.HTML".EnsureExtension("html", "json");

        Assert.Equal("report.json", result);
    }

    [Fact]
    public void ToSafeFileBase_NormalizesUnsafeCharacters()
    {
        string result = "Mapa de Cobrança 2026".ToSafeFileBase();

        Assert.Equal("Mapa_de_Cobrança_2026", result);
    }
}

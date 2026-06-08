using AkkornStudio.UI.Extensions;

namespace AkkornStudio.Tests.Unit.Extensions;

public sealed class StringTransformExtensionsTests
{
    [Fact]
    public void ToSlugCase_RemovesAccentsAndNormalizesSeparators()
    {
        string value = "Mapa de Cobrança 2026";

        string result = value.ToSlugCase();

        Assert.Equal("mapa-de-cobranca-2026", result);
    }

    [Fact]
    public void TransformText_SupportsExpectedModes()
    {
        Assert.Equal("RELATORIO FINAL", "Relatorio Final".TransformText("upper"));
        Assert.Equal("relatorio final", "Relatorio Final".TransformText("lower"));
        Assert.Equal("Relatorio Final", "relatorio final".TransformText("title"));
        Assert.Equal("Relatorio final", "RELATORIO FINAL".TransformText("sentence"));
        Assert.Equal("relatorio-final", "Relatorio Final".TransformText("slug"));
    }

    [Fact]
    public void ToSlugToken_SupportsCustomSeparatorAndFallback()
    {
        Assert.Equal("relatorio_final", "Relatorio Final".ToSlugToken('_', "empty"));
        Assert.Equal("empty", "   ".ToSlugToken('_', "empty"));
    }
}

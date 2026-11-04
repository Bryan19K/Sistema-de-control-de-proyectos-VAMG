using Tekcom.Gestion.Domain.Logic;

namespace Tekcom.Gestion.Domain.Tests.Logic;

public class NameNormalizerTests
{
    [Theory]
    [InlineData("SUB-BOX", "SUB BOX")]
    [InlineData("sub box", "SUB BOX")]
    [InlineData("Sub_Box", "SUB BOX")]
    [InlineData("  Trébol  ", "TREBOL")]
    [InlineData("VIÑETAS", "VINETAS")]
    [InlineData("FUSIÓNES   EN XBOX", "FUSIONES EN XBOX")]
    [InlineData("FO 5 M INSTALADO", "FO 5 M INSTALADO")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normaliza_sin_acentos_mayusculas_ni_simbolos(string? input, string expected)
    {
        NameNormalizer.Key(input).Should().Be(expected);
    }
}

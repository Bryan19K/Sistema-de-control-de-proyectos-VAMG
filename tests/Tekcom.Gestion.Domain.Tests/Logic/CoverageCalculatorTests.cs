using Tekcom.Gestion.Domain.Logic;

namespace Tekcom.Gestion.Domain.Tests.Logic;

public class CoverageCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 11, 5);

    [Theory]
    [InlineData("PENDIENTE", "123", CoverageState.Pending)]
    [InlineData("Vigente", null, CoverageState.Pending)]
    [InlineData("Vigente", "  ", CoverageState.Pending)]
    [InlineData("Vigente", "121023582", CoverageState.Covered)]
    [InlineData("inscrito", "105-9902134-6", CoverageState.Covered)]
    public void Sin_numero_o_con_estado_pendiente_falta_y_con_numero_esta_al_dia(string? status, string? policy, CoverageState expected) =>
        CoverageCalculator.Evaluate(status, policy, null, Today).Should().Be(expected);

    [Fact]
    public void Una_vigencia_terminada_esta_vencida_y_una_que_termina_pronto_se_avisa()
    {
        CoverageCalculator.Evaluate("Vigente", "1", Today.AddDays(-1), Today).Should().Be(CoverageState.Expired);
        CoverageCalculator.Evaluate("Vigente", "1", Today.AddDays(10), Today).Should().Be(CoverageState.ExpiringSoon);
        CoverageCalculator.Evaluate("Vigente", "1", Today.AddDays(40), Today).Should().Be(CoverageState.Covered);
    }

    [Theory]
    [InlineData("Vigencia: 29/10/2026 - 29/11/2026", 2026, 11, 29)]
    [InlineData("Vigencia hasta 31/10/2026", 2026, 10, 31)]
    [InlineData("Póliza emitida – vigencia del 29/09/2026 al 29/10/2026", 2026, 10, 29)]
    public void Saca_la_fecha_final_del_texto_de_vigencia(string text, int year, int month, int day) =>
        CoverageCalculator.ParseEnd(text).Should().Be(new DateOnly(year, month, day));

    [Fact]
    public void Sin_fecha_en_el_texto_no_hay_vigencia() => CoverageCalculator.ParseEnd("inscrito").Should().BeNull();
}

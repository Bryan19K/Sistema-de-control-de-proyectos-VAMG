using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Domain.Logic;

namespace Tekcom.Gestion.Domain.Tests.Logic;

public class StageProgressCalculatorTests
{
    private static StageWeight Stage(StageStatus status, decimal days, bool counts = true) => new(status, days, counts);

    [Fact]
    public void El_avance_se_pondera_por_los_dias_planificados_de_cada_etapa()
    {
        var progress = StageProgressCalculator.Percent(
        [
            Stage(StageStatus.Finished, 1),
            Stage(StageStatus.Finished, 3),
            Stage(StageStatus.Pending, 6),
        ]);

        progress.Should().Be(40, "4 de 10 días están finalizados");
    }

    [Fact]
    public void Una_etapa_en_proceso_no_suma_avance()
    {
        StageProgressCalculator.Percent([Stage(StageStatus.InProgress, 5), Stage(StageStatus.Finished, 5)])
            .Should().Be(50);
    }

    [Fact]
    public void Las_etapas_no_aplica_y_las_informativas_no_cuentan()
    {
        var progress = StageProgressCalculator.Percent(
        [
            Stage(StageStatus.Finished, 2),
            Stage(StageStatus.NotApplicable, 8),
            Stage(StageStatus.Pending, 99, counts: false),
        ]);

        progress.Should().Be(100);
    }

    [Fact]
    public void Sin_etapas_el_avance_es_cero()
    {
        StageProgressCalculator.Percent([]).Should().Be(0);
    }
}

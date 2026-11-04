using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Domain.Logic;

namespace Tekcom.Gestion.Domain.Tests.Logic;

public class PaceCalculatorTests
{
    private static readonly DateOnly Start = new(2026, 10, 20);

    [Fact]
    public void Un_sprint_con_15_por_ciento_hecho_y_60_por_ciento_del_tiempo_esta_atrasado()
    {
        // Criterio de aceptación del Sprint 2: 15 % hecho con 60 % del tiempo = Atrasado.
        var end = Start.AddDays(20);
        var today = Start.AddDays(12);

        var result = PaceCalculator.Calculate(15, Start, end, today);

        result.ElapsedPercent.Should().Be(60);
        result.Status.Should().Be(PaceStatus.Behind);
        result.DaysDelta.Should().BeNegative();
    }

    [Theory]
    [InlineData(80, 60, PaceStatus.Ahead)]
    [InlineData(65, 60, PaceStatus.OnTime)]
    [InlineData(50, 60, PaceStatus.OnTime)]
    [InlineData(49, 60, PaceStatus.Behind)]
    [InlineData(71, 60, PaceStatus.Ahead)]
    [InlineData(70, 60, PaceStatus.OnTime)]
    public void El_estado_depende_del_margen_de_10_puntos(int progress, int elapsed, PaceStatus expected)
    {
        var end = Start.AddDays(100);
        var today = Start.AddDays(elapsed);

        PaceCalculator.Calculate(progress, Start, end, today, marginPercent: 10).Status.Should().Be(expected);
    }

    [Fact]
    public void El_margen_es_configurable()
    {
        var end = Start.AddDays(100);
        var today = Start.AddDays(60);

        PaceCalculator.Calculate(45, Start, end, today, marginPercent: 20).Status.Should().Be(PaceStatus.OnTime);
        PaceCalculator.Calculate(45, Start, end, today, marginPercent: 10).Status.Should().Be(PaceStatus.Behind);
    }

    [Fact]
    public void Calcula_dias_de_atraso_y_fecha_de_cierre_proyectada()
    {
        var end = Start.AddDays(100);
        var today = Start.AddDays(50);

        var result = PaceCalculator.Calculate(25, Start, end, today);

        result.DaysDelta.Should().Be(-25, "va 25 puntos por debajo de lo esperado sobre 100 días");
        result.ProjectedEnd.Should().Be(Start.AddDays(200), "con la mitad del ritmo tardará el doble");
    }

    [Fact]
    public void Un_avance_completo_queda_como_finalizado()
    {
        var result = PaceCalculator.Calculate(100, Start, Start.AddDays(10), Start.AddDays(20));

        result.Status.Should().Be(PaceStatus.Completed);
    }

    [Fact]
    public void Antes_de_la_fecha_de_inicio_el_tiempo_transcurrido_es_cero()
    {
        var result = PaceCalculator.Calculate(0, Start, Start.AddDays(10), Start.AddDays(-5));

        result.ElapsedPercent.Should().Be(0);
        result.Status.Should().Be(PaceStatus.OnTime);
    }

    [Fact]
    public void Sin_fechas_el_ritmo_es_desconocido()
    {
        PaceCalculator.Calculate(40, null, null, Start).Status.Should().Be(PaceStatus.Unknown);
        PaceCalculator.Calculate(40, Start, Start, Start).Status.Should().Be(PaceStatus.Unknown);
    }
}

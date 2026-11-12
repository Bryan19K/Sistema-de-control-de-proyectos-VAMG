using Tekcom.Gestion.Application.Common;

namespace Tekcom.Gestion.Application.Tests.Services;

public class DashboardMathTests
{
    [Theory]
    [InlineData(122, 100, 22.0)]
    [InlineData(92, 100, -8.0)]
    public void Variation_is_measured_against_the_previous_period(double current, double previous, double expected) =>
        DashboardMath.PercentChange((decimal)current, (decimal)previous).Should().Be((decimal)expected);

    [Fact]
    public void Without_a_previous_period_there_is_no_variation() =>
        DashboardMath.PercentChange(50, 0).Should().BeNull();

    [Fact]
    public void Chip_text_has_sign_and_one_decimal_at_most()
    {
        DashboardMath.ChangeText(22m).Should().Be("+22 %");
        DashboardMath.ChangeText(-8.5m).Should().Be("-8.5 %");
        DashboardMath.ChangeText(null).Should().BeEmpty();
    }

    [Fact]
    public void Used_percent_can_exceed_100_when_overspent()
    {
        DashboardMath.UsedPercent(168, 150).Should().Be(112m);
        DashboardMath.UsedPercent(10, 0).Should().Be(0);
    }

    [Theory]
    [InlineData("Bryan Mejia", "bryan@tekcom.com", "Bienvenido, Bryan Mejia")]
    [InlineData("  ", "bryan@tekcom.com", "Bienvenido, bryan")]
    [InlineData(null, null, "Bienvenido")]
    public void Welcome_uses_the_full_name_or_the_email_prefix(string? name, string? email, string expected) =>
        DashboardMath.Welcome(name, email).Should().Be(expected);

    [Theory]
    [InlineData("REALIZADA", true)]
    [InlineData("Liquidada", true)]
    [InlineData("PENDIENTE", false)]
    [InlineData(null, false)]
    public void Liquidated_orders_are_recognized_by_their_status(string? status, bool expected) =>
        DashboardMath.IsLiquidated(status).Should().Be(expected);

    [Fact]
    public void Estimated_close_uses_the_pace_since_the_first_installation()
    {
        // 20 % en 20 días = 1 % por día: faltan 80 días.
        DashboardMath.EstimatedClose(20m, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 21)).Should().Be(new DateOnly(2027, 1, 9));
        DashboardMath.EstimatedClose(0m, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 21)).Should().BeNull();
        DashboardMath.EstimatedClose(10m, new DateOnly(2026, 10, 18), new DateOnly(2026, 10, 21)).Should().BeNull("menos de una semana no alcanza para estimar");
        DashboardMath.EstimatedClose(100m, null, new DateOnly(2026, 10, 21)).Should().Be(new DateOnly(2026, 10, 21));
    }

    [Fact]
    public void Unit_cost_divides_the_total_and_needs_units()
    {
        DashboardMath.UnitCost(4841.14m, 4106m).Should().Be(1.18m);
        DashboardMath.UnitCost(100m, 0m).Should().BeNull();
    }
}

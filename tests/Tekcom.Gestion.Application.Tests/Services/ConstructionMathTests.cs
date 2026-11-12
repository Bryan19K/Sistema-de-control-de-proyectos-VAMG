using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Tests.Support;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Infrastructure.Services;

namespace Tekcom.Gestion.Application.Tests.Services;

public class ConstructionMathTests
{
    [Theory]
    [InlineData("APROBADO", PermitStatus.Approved)]
    [InlineData("Aprobados ", PermitStatus.Approved)]
    [InlineData("EN TRAMITE", PermitStatus.InProcess)]
    [InlineData("permiso en trámite", PermitStatus.InProcess)]
    [InlineData("PENDIENTE", PermitStatus.RequestPending)]
    [InlineData("", PermitStatus.RequestPending)]
    [InlineData(null, PermitStatus.RequestPending)]
    public void The_permit_is_read_from_the_excel_text(string? text, PermitStatus expected) =>
        ConstructionMath.PermitFromText(text).Should().Be(expected);

    [Fact]
    public void The_three_permit_states_have_their_names() =>
        Enum.GetValues<PermitStatus>().Select(ConstructionMath.PermitName).Should().Equal("SOLICITUD PENDIENTE", "PERMISO EN TRÁMITE", "PERMISO APROBADO");

    [Fact]
    public void Weekly_home_passes_follow_the_installed_pieces_without_passing_the_projection()
    {
        // Nodo de 1,000 HP con 10 piezas proyectadas (8 de cable y 2 fusiones): cada pieza vale 100 HP.
        var nodes = new Dictionary<int, NodeHpBasis>
        {
            [1] = new(1000, new Dictionary<QuantityGroup, decimal> { [QuantityGroup.FcCable] = 8, [QuantityGroup.Fusion] = 2 }),
        };
        var moves = new List<PieceMove>
        {
            new(1, QuantityGroup.FcCable, new DateOnly(2026, 10, 12), 3),   // lunes
            new(1, QuantityGroup.FcCable, new DateOnly(2026, 10, 16), 2),   // misma semana
            new(1, QuantityGroup.Fusion, new DateOnly(2026, 10, 20), 5),    // semana siguiente, pero solo caben 2
            new(2, QuantityGroup.FcCable, new DateOnly(2026, 10, 12), 9),   // nodo sin HP cargado: no cuenta
        };

        var weeks = ConstructionMath.WeeklyHp(moves, nodes);

        weeks[new DateOnly(2026, 10, 12)].Should().Be(500m);
        weeks[new DateOnly(2026, 10, 19)].Should().Be(200m);
        weeks.Should().HaveCount(2);
    }

    [Fact]
    public async Task The_weekly_goal_is_1000_until_someone_changes_it_and_must_be_positive()
    {
        using var db = await TestDb.CreateAsync();
        var goals = new GoalsService(db);

        (await goals.GetWeeklyHpGoalAsync()).Should().Be(1000);
        await goals.SaveWeeklyHpGoalAsync(1200);
        (await goals.GetWeeklyHpGoalAsync()).Should().Be(1200);
        await Assert.ThrowsAsync<BusinessRuleException>(() => goals.SaveWeeklyHpGoalAsync(0));
        (await goals.GetWeeklyHpGoalAsync()).Should().Be(1200);
    }
}

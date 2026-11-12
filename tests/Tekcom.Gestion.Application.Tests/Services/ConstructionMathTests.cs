using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Domain.Entities;
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

public class PermitDatesTests
{
    private static readonly DateOnly Today = new(2026, 11, 12);

    [Fact]
    public void Changing_the_status_marks_the_request_and_approval_dates()
    {
        ConstructionMath.PermitDates(PermitStatus.InProcess, null, null, Today).Should().Be((Today, (DateOnly?)null));
        ConstructionMath.PermitDates(PermitStatus.Approved, new DateOnly(2026, 11, 2), null, Today).Should().Be((new DateOnly(2026, 11, 2), Today));
        ConstructionMath.PermitDates(PermitStatus.Approved, null, null, Today).Should().Be((Today, Today));
        ConstructionMath.PermitDates(PermitStatus.RequestPending, Today, Today, Today).Should().Be(((DateOnly?)null, (DateOnly?)null));
    }

    [Fact]
    public void Permit_days_run_to_the_approval_or_to_today()
    {
        ConstructionMath.PermitDays(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 9), Today).Should().Be(7);
        ConstructionMath.PermitDays(new DateOnly(2026, 11, 2), null, Today).Should().Be(10);
        ConstructionMath.PermitDays(null, null, Today).Should().BeNull();
    }

    [Fact]
    public async Task The_service_fills_the_dates_and_rejects_an_approval_before_the_request()
    {
        using var db = await TestDb.CreateAsync();
        var quantities = new QuantityService(db);
        var sites = new SiteService(db, quantities, db.Clock);
        var projects = new ProjectService(db);
        await using var ctx = db.CreateDbContext();
        var ftth = await ctx.ProjectTypes.SingleAsync(t => t.Code == "FTTH");
        var project = await projects.SaveAsync(new Project { Code = "P-1", Name = "Permisos", ProjectTypeId = ftth.Id });
        var site = await sites.SaveAsync(new Site { ProjectId = project.Id, Code = "SON01" });

        await sites.SetPermitStatusAsync(site.Id, PermitStatus.InProcess);
        await sites.SetMunicipalPermitStatusAsync(site.Id, PermitStatus.Approved);

        var saved = (await sites.GetAsync(site.Id))!;
        saved.ElectricalRequestedOn.Should().Be(db.Clock.Today);
        saved.ElectricalApprovedOn.Should().BeNull();
        (saved.MunicipalRequestedOn, saved.MunicipalApprovedOn).Should().Be((db.Clock.Today, db.Clock.Today));

        await sites.SetPermitDatesAsync(site.Id, PermitKind.Electrical, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 8));
        (await sites.GetAsync(site.Id))!.ElectricalApprovedOn.Should().Be(new DateOnly(2026, 10, 8));
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            sites.SetPermitDatesAsync(site.Id, PermitKind.Electrical, new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 1)));
    }
}

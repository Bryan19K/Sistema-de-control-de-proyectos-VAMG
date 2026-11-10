using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Application.Tests.Support;
using Tekcom.Gestion.Infrastructure.Services;

namespace Tekcom.Gestion.Application.Tests.Services;

/// <summary>Los colores de los gráficos del dashboard se pueden cambiar y volver a los de referencia.</summary>
public class AppearanceTests
{
    [Fact]
    public async Task Without_saved_values_the_reference_colors_are_used()
    {
        using var db = await TestDb.CreateAsync();
        var colors = await new AppearanceService(db).GetColorsAsync();

        Assert.Equal(DashboardColors.Default, colors);
        Assert.Equal("#54A32E", colors.Done);
        Assert.Equal("#92D14F", colors.InProgress);
        Assert.Equal("#EF472D", colors.Alert);
    }

    [Fact]
    public async Task Saved_colors_are_returned_and_can_be_changed_again()
    {
        using var db = await TestDb.CreateAsync();
        var service = new AppearanceService(db);

        await service.SaveColorsAsync(new DashboardColors("#112233", "#aabbcc", "#445566"));
        Assert.Equal(new DashboardColors("#112233", "#AABBCC", "#445566"), await service.GetColorsAsync());

        await service.SaveColorsAsync(new DashboardColors("#000000", "#FFFFFF", "#FF0000"));
        Assert.Equal(new DashboardColors("#000000", "#FFFFFF", "#FF0000"), await service.GetColorsAsync());
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("#112233; background:url(x)")]
    public async Task A_color_that_is_not_hex_is_rejected(string bad)
    {
        using var db = await TestDb.CreateAsync();
        var service = new AppearanceService(db);

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.SaveColorsAsync(new DashboardColors(bad, "#92D14F", "#EF472D")));
        Assert.Equal(DashboardColors.Default, await service.GetColorsAsync());
    }

    [Fact]
    public async Task Reset_goes_back_to_the_reference_colors()
    {
        using var db = await TestDb.CreateAsync();
        var service = new AppearanceService(db);
        await service.SaveColorsAsync(new DashboardColors("#112233", "#445566", "#778899"));

        await service.ResetColorsAsync();

        Assert.Equal(DashboardColors.Default, await service.GetColorsAsync());
    }

    [Fact]
    public async Task Chart_types_default_to_factory_values_and_can_be_changed()
    {
        using var db = await TestDb.CreateAsync();
        var service = new AppearanceService(db);
        Assert.Equal(DashboardCharts.Default, await service.GetChartsAsync());

        var custom = new DashboardCharts(ChartKind.Pie, ChartKind.Donut, ChartKind.Columns, ChartKind.Bars, ChartKind.Pie);
        await service.SaveChartsAsync(custom);
        Assert.Equal(custom, await service.GetChartsAsync());

        await service.ResetChartsAsync();
        Assert.Equal(DashboardCharts.Default, await service.GetChartsAsync());
    }

    [Fact]
    public async Task An_unknown_chart_type_is_rejected()
    {
        using var db = await TestDb.CreateAsync();
        var service = new AppearanceService(db);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.SaveChartsAsync(DashboardCharts.Default with { Nodes = (ChartKind)99 }));
        Assert.Equal(DashboardCharts.Default, await service.GetChartsAsync());
    }
}

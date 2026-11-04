using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Tests.Support;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Infrastructure.Services;

namespace Tekcom.Gestion.Application.Tests.Services;

public class MaterialServiceTests
{
    [Fact]
    public async Task El_catalogo_base_trae_los_14_largos_de_FC_y_los_9_de_MPO()
    {
        using var db = await TestDb.CreateAsync();
        var service = new MaterialService(db);

        var fc = await service.ListAsync(category: MaterialCategory.FiberCable);
        var mpo = await service.ListAsync(category: MaterialCategory.MpoCable);

        fc.Select(m => m.LengthMeters).Should().Equal(5, 50, 80, 100, 120, 150, 180, 200, 250, 300, 350, 400, 450, 500);
        mpo.Select(m => m.LengthMeters).Should().Equal(100, 150, 200, 250, 300, 350, 400, 450, 500);
    }

    [Theory]
    [InlineData("SUB-BOX")]
    [InlineData("sub box")]
    [InlineData("Sub_Box")]
    public async Task Un_alias_encuentra_el_material_sin_importar_mayusculas_ni_guiones(string name)
    {
        using var db = await TestDb.CreateAsync();
        var service = new MaterialService(db);

        var material = await service.ResolveAsync(name);

        material!.Name.Should().Be("NAP de paso");
    }

    [Fact]
    public async Task No_permite_un_alias_que_ya_pertenece_a_otro_material()
    {
        using var db = await TestDb.CreateAsync();
        var service = new MaterialService(db);
        var final = (await service.ResolveAsync("END-BOX"))!;

        var act = () => service.AddAliasAsync(final.Id, "SUB-BOX");

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*NAP de paso*");
    }

    [Fact]
    public async Task No_permite_crear_un_material_duplicado_ni_con_el_nombre_de_un_alias()
    {
        using var db = await TestDb.CreateAsync();
        var service = new MaterialService(db);

        var byName = () => service.SaveAsync(new Material { Name = "nap de paso", Unit = "UND" });
        await byName.Should().ThrowAsync<BusinessRuleException>();

        var byAlias = () => service.SaveAsync(new Material { Name = "Sub-Box", Unit = "UND" });
        await byAlias.Should().ThrowAsync<BusinessRuleException>().WithMessage("*NAP de paso*");
    }

    [Fact]
    public async Task Aro_de_sujecion_y_trebol_son_materiales_separados()
    {
        using var db = await TestDb.CreateAsync();
        var service = new MaterialService(db);

        var aro = await service.ResolveAsync("ARO DE SUJECION");
        var trebol = await service.ResolveAsync("treboles");

        aro!.Id.Should().NotBe(trebol!.Id);
    }

    [Fact]
    public async Task Buscar_encuentra_por_nombre_alias_o_codigo()
    {
        using var db = await TestDb.CreateAsync();
        var service = new MaterialService(db);

        (await service.ListAsync("hbbox")).Should().ContainSingle(m => m.Name == "Hub-box");
        (await service.ListAsync("mpo 400")).Should().ContainSingle(m => m.Name == "Cable MPO 400 m");
    }
}

using Tekcom.Gestion.Domain.Identity;

namespace Tekcom.Gestion.Domain.Tests.Identity;

public class AppRolesTests
{
    [Fact]
    public void All_contiene_los_cinco_roles_del_plan_sin_duplicados()
    {
        AppRoles.All.Should().BeEquivalentTo(
        [
            AppRoles.Admin,
            AppRoles.Bodega,
            AppRoles.Supervisor,
            AppRoles.Gerencia,
            AppRoles.Contratista,
        ]);

        AppRoles.All.Should().OnlyHaveUniqueItems();
    }
}

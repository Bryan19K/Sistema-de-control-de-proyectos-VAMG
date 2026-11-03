using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Infrastructure.Identity;
using Tekcom.Gestion.Infrastructure.Persistence;

namespace Tekcom.Gestion.Application.Tests.Persistence;

public class ApplicationDbContextTests
{
    [Fact]
    public async Task El_modelo_se_construye_y_crea_el_esquema_de_Identity()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .UseApplicationServiceProvider(IdentitySchema.CreateMinimalServiceProvider())
            .Options;

        await using var db = new ApplicationDbContext(options);
        var created = await db.Database.EnsureCreatedAsync();

        created.Should().BeTrue();
        db.Model.FindEntityType(typeof(IdentityUserPasskey<string>))
            .Should().NotBeNull("el esquema v3 de Identity incluye la tabla de passkeys");
        db.Model.FindEntityType(typeof(ApplicationUser))!
            .FindProperty(nameof(ApplicationUser.FullName))!
            .GetMaxLength().Should().Be(120);
    }
}

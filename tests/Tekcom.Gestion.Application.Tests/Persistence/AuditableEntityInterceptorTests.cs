using Microsoft.EntityFrameworkCore;

namespace Tekcom.Gestion.Application.Tests.Persistence;

public class AuditableEntityInterceptorTests : IDisposable
{
    private readonly SqliteDbFixture _fixture = new();

    [Fact]
    public async Task Al_crear_asigna_CreatedAt_y_CreatedBy()
    {
        await using var db = _fixture.CreateContext();
        var note = new Note { Text = "Nueva" };

        db.Notes.Add(note);
        await db.SaveChangesAsync();

        note.CreatedAt.Should().Be(_fixture.Clock.UtcNow);
        note.CreatedBy.Should().Be("bryan@tekcom.com");
        note.UpdatedAt.Should().BeNull();
        note.UpdatedBy.Should().BeNull();
        note.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Al_modificar_asigna_UpdatedAt_y_UpdatedBy_sin_tocar_CreatedAt()
    {
        var createdAt = _fixture.Clock.UtcNow;
        int id;
        await using (var db = _fixture.CreateContext())
        {
            var note = new Note { Text = "Original" };
            db.Notes.Add(note);
            await db.SaveChangesAsync();
            id = note.Id;
        }

        _fixture.Clock.UtcNow = createdAt.AddHours(2);
        _fixture.CurrentUser.UserName = "pareja@tekcom.com";

        await using (var db = _fixture.CreateContext())
        {
            var note = await db.Notes.SingleAsync(n => n.Id == id);
            note.Text = "Editada";
            await db.SaveChangesAsync();

            note.CreatedAt.Should().Be(createdAt);
            note.CreatedBy.Should().Be("bryan@tekcom.com");
            note.UpdatedAt.Should().Be(createdAt.AddHours(2));
            note.UpdatedBy.Should().Be("pareja@tekcom.com");
        }
    }

    [Fact]
    public async Task Al_guardar_sin_cambios_reales_no_marca_UpdatedAt()
    {
        int id;
        await using (var db = _fixture.CreateContext())
        {
            var note = new Note { Text = "Sin cambios" };
            db.Notes.Add(note);
            await db.SaveChangesAsync();
            id = note.Id;
        }

        await using (var db = _fixture.CreateContext())
        {
            var note = await db.Notes.SingleAsync(n => n.Id == id);
            await db.SaveChangesAsync();

            note.UpdatedAt.Should().BeNull();
        }
    }

    [Fact]
    public async Task Eliminar_convierte_el_borrado_en_logico_y_conserva_el_registro()
    {
        int id;
        await using (var db = _fixture.CreateContext())
        {
            var note = new Note { Text = "Para borrar" };
            db.Notes.Add(note);
            await db.SaveChangesAsync();
            id = note.Id;
        }

        _fixture.Clock.UtcNow = _fixture.Clock.UtcNow.AddMinutes(5);

        await using (var db = _fixture.CreateContext())
        {
            var note = await db.Notes.SingleAsync(n => n.Id == id);
            db.Notes.Remove(note);
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateContext())
        {
            (await db.Notes.AnyAsync(n => n.Id == id)).Should().BeFalse("el filtro global oculta los eliminados");

            var deleted = await db.Notes.IgnoreQueryFilters().SingleAsync(n => n.Id == id);
            deleted.IsDeleted.Should().BeTrue();
            deleted.DeletedAt.Should().Be(_fixture.Clock.UtcNow);
            deleted.DeletedBy.Should().Be("bryan@tekcom.com");
            deleted.Text.Should().Be("Para borrar", "nada se borra físicamente");
        }
    }

    [Fact]
    public async Task Sin_usuario_autenticado_CreatedBy_queda_nulo()
    {
        _fixture.CurrentUser.UserName = null;
        _fixture.CurrentUser.UserId = null;

        await using var db = _fixture.CreateContext();
        var note = new Note { Text = "Proceso del sistema" };
        db.Notes.Add(note);
        await db.SaveChangesAsync();

        note.CreatedBy.Should().BeNull();
        note.CreatedAt.Should().Be(_fixture.Clock.UtcNow);
    }

    public void Dispose() => _fixture.Dispose();
}

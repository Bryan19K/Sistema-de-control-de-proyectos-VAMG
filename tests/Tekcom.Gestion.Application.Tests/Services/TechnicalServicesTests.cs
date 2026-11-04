using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Application.Tests.Support;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Infrastructure.Services;

namespace Tekcom.Gestion.Application.Tests.Services;

public class TechnicalServicesTests
{
    private sealed class Env : IDisposable
    {
        public required TestDb Db { get; init; }
        public required EquipmentService Equipment { get; init; }
        public required DocumentService Documents { get; init; }
        public required MinuteService Minutes { get; init; }
        public required FakeFileStorage Storage { get; init; }
        public required Project Project { get; init; }
        public required Site Site { get; init; }
        public DateOnly Today => Db.Clock.Today;
        public void Dispose() => Db.Dispose();
    }

    private static async Task<Env> SetupAsync()
    {
        var db = await TestDb.CreateAsync();
        var sites = new SiteService(db, new QuantityService(db), db.Clock);
        await using var context = db.CreateDbContext();
        var ftth = await context.ProjectTypes.SingleAsync(t => t.Code == "FTTH");
        var project = await new ProjectService(db).SaveAsync(new Project { Code = "T-1", Name = "Técnico", ProjectTypeId = ftth.Id });
        var site = await sites.SaveAsync(new Site { ProjectId = project.Id, Code = "SON19" });
        var storage = new FakeFileStorage();
        return new Env
        {
            Db = db, Project = project, Site = site, Storage = storage,
            Equipment = new EquipmentService(db, db.Clock), Documents = new DocumentService(db, storage), Minutes = new MinuteService(db, db.Clock),
        };
    }

    private static Task<Equipment> NewOltAsync(Env env, string name = "OLT Sonsonate", string? serial = "OLT-001") =>
        env.Equipment.SaveEquipmentAsync(new Equipment { ProjectId = env.Project.Id, SiteId = env.Site.Id, Name = name, Serial = serial, Brand = "Huawei", Model = "MA5800" });

    private static Task<EquipmentSlot> NewSlotAsync(Env env, int equipmentId, int slot = 1, int ports = 16) =>
        env.Equipment.SaveSlotAsync(new EquipmentSlot { EquipmentId = equipmentId, SlotNumber = slot, CardModel = "GPFD", CardSerial = $"CARD-{slot}", PortCount = ports });

    // ---------------------------------------------------------------- equipos y SFP

    [Fact]
    public async Task El_serial_del_equipo_no_se_repite_en_el_proyecto_sin_importar_mayusculas()
    {
        using var env = await SetupAsync();
        await NewOltAsync(env);

        var duplicate = () => NewOltAsync(env, "Otra OLT", "olt-001");

        await duplicate.Should().ThrowAsync<BusinessRuleException>().WithMessage("*serial*");
        (await NewOltAsync(env, "Sin serial", null)).Id.Should().BeGreaterThan(0);
        (await NewOltAsync(env, "Sin serial 2", null)).Id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Una_ranura_solo_tiene_una_tarjeta_y_el_equipo_con_tarjetas_no_se_puede_quitar()
    {
        using var env = await SetupAsync();
        var olt = await NewOltAsync(env);
        await NewSlotAsync(env, olt.Id, 1);

        var again = () => NewSlotAsync(env, olt.Id, 1);
        await again.Should().ThrowAsync<BusinessRuleException>().WithMessage("*ranura 1*");

        var remove = () => env.Equipment.RemoveEquipmentAsync(olt.Id);
        await remove.Should().ThrowAsync<BusinessRuleException>().WithMessage("*Retirado*");
    }

    [Fact]
    public async Task El_SFP_exige_serial_unico_puerto_valido_y_un_solo_SFP_instalado_por_puerto()
    {
        using var env = await SetupAsync();
        var olt = await NewOltAsync(env);
        var slot = await NewSlotAsync(env, olt.Id, 1, ports: 8);

        Transceiver Sfp(string serial, int port, TransceiverStatus status = TransceiverStatus.Installed) => new()
        {
            ProjectId = env.Project.Id, EquipmentId = olt.Id, SlotId = slot.Id, PortNumber = port, Serial = serial, Status = status, Model = "SFP+ 10G", WavelengthNm = 1310,
        };

        var first = await env.Equipment.SaveTransceiverAsync(Sfp("SN-100", 1));
        first.InstalledOn.Should().Be(env.Today, "al instalarlo se anota la fecha");

        (await env.Equipment.Invoking(e => e.SaveTransceiverAsync(Sfp("sn-100", 2))).Should().ThrowAsync<BusinessRuleException>()).WithMessage("*serial*");
        (await env.Equipment.Invoking(e => e.SaveTransceiverAsync(Sfp("SN-101", 9))).Should().ThrowAsync<BusinessRuleException>()).WithMessage("*entre 1 y 8*");
        (await env.Equipment.Invoking(e => e.SaveTransceiverAsync(Sfp("SN-102", 1))).Should().ThrowAsync<BusinessRuleException>()).WithMessage("*ya tiene un SFP instalado*");

        // Un SFP reemplazado libera el puerto.
        first.Status = TransceiverStatus.Replaced;
        await env.Equipment.SaveTransceiverAsync(first);
        (await env.Equipment.SaveTransceiverAsync(Sfp("SN-102", 1))).Id.Should().BeGreaterThan(0);

        var view = (await env.Equipment.ListAsync(env.Project.Id)).Single();
        view.Ports.Should().Be(8);
        view.PortsUsed.Should().Be(1);
    }

    [Fact]
    public async Task Un_SFP_en_stock_no_necesita_equipo_y_uno_instalado_si()
    {
        using var env = await SetupAsync();

        var stock = await env.Equipment.SaveTransceiverAsync(new Transceiver { ProjectId = env.Project.Id, Serial = "STK-1", Status = TransceiverStatus.InStock });
        stock.EquipmentId.Should().BeNull();

        var installed = () => env.Equipment.SaveTransceiverAsync(new Transceiver { ProjectId = env.Project.Id, Serial = "STK-2", Status = TransceiverStatus.Installed });
        await installed.Should().ThrowAsync<BusinessRuleException>().WithMessage("*equipo*");
    }

    [Fact]
    public async Task La_busqueda_de_SFP_encuentra_por_serial_modelo_o_equipo_y_filtra_por_estado()
    {
        using var env = await SetupAsync();
        var olt = await NewOltAsync(env);
        await env.Equipment.SaveTransceiverAsync(new Transceiver { ProjectId = env.Project.Id, EquipmentId = olt.Id, Serial = "ABC-1", Status = TransceiverStatus.Installed });
        await env.Equipment.SaveTransceiverAsync(new Transceiver { ProjectId = env.Project.Id, Serial = "XYZ-9", Model = "Bidi 1G", Status = TransceiverStatus.InStock });

        (await env.Equipment.TransceiversAsync(env.Project.Id, "abc")).Select(t => t.Serial).Should().Equal("ABC-1");
        (await env.Equipment.TransceiversAsync(env.Project.Id, "bidi")).Select(t => t.Serial).Should().Equal("XYZ-9");
        (await env.Equipment.TransceiversAsync(env.Project.Id, "sonsonate")).Select(t => t.Serial).Should().Equal("ABC-1");
        (await env.Equipment.TransceiversAsync(env.Project.Id, null, TransceiverStatus.InStock)).Should().ContainSingle();
    }

    // ---------------------------------------------------------------- documentos

    private static Task<ProjectDocument> UploadAsync(Env env, string title, DocumentKind kind, bool signed = false, int? siteId = null, int? taskId = null, string fileName = "atp.pdf") =>
        env.Documents.UploadAsync(new DocumentUpload(env.Project.Id, siteId, taskId, kind, title, env.Today, signed, null, fileName, "application/pdf"),
            new MemoryStream([1, 2, 3, 4]));

    [Fact]
    public async Task Se_sube_un_documento_firmado_de_un_nodo_y_se_puede_abrir_y_filtrar()
    {
        using var env = await SetupAsync();
        var doc = await UploadAsync(env, "ATP SON19 firmado", DocumentKind.Atp, signed: true, siteId: env.Site.Id);
        await UploadAsync(env, "Foto de cierre", DocumentKind.Photo, siteId: env.Site.Id, fileName: "foto.jpg");
        await UploadAsync(env, "Minuta general", DocumentKind.Minute);

        doc.Size.Should().Be(4);
        doc.CreatedBy.Should().Be(env.Db.CurrentUser.UserName, "queda quién lo subió");
        (await env.Documents.ListAsync(new DocumentFilter { ProjectId = env.Project.Id, SiteId = env.Site.Id })).Should().HaveCount(2);
        (await env.Documents.ListAsync(new DocumentFilter { ProjectId = env.Project.Id, Kind = DocumentKind.Atp })).Should().ContainSingle().Which.IsSigned.Should().BeTrue();

        var opened = await env.Documents.OpenAsync(doc.Id);
        opened!.Value.Content.Length.Should().Be(4);
        opened.Value.Content.Dispose();
    }

    [Fact]
    public async Task El_documento_se_vincula_a_una_tarea_del_proyecto_y_no_a_nodos_o_tareas_ajenos()
    {
        using var env = await SetupAsync();
        var task = await new TaskService(env.Db, env.Storage, env.Db.Clock).SaveAsync(new TaskItem { ProjectId = env.Project.Id, Title = "Entregar ATP" });

        var linked = await UploadAsync(env, "ATP", DocumentKind.Atp, taskId: task.Id);
        (await env.Documents.ListAsync(new DocumentFilter { ProjectId = env.Project.Id, TaskItemId = task.Id })).Should().ContainSingle().Which.Id.Should().Be(linked.Id);

        var badTask = () => UploadAsync(env, "ATP", DocumentKind.Atp, taskId: 9999);
        await badTask.Should().ThrowAsync<BusinessRuleException>().WithMessage("*tarea*");
        var badSite = () => UploadAsync(env, "ATP", DocumentKind.Atp, siteId: 9999);
        await badSite.Should().ThrowAsync<BusinessRuleException>().WithMessage("*nodo*");
    }

    [Fact]
    public async Task Quitar_un_documento_lo_oculta_pero_conserva_el_archivo()
    {
        using var env = await SetupAsync();
        var doc = await UploadAsync(env, "ATP", DocumentKind.Atp);

        await env.Documents.RemoveAsync(doc.Id);

        (await env.Documents.ListAsync(new DocumentFilter { ProjectId = env.Project.Id })).Should().BeEmpty();
        env.Storage.Count.Should().Be(1);
        (await env.Db.CreateDbContext().Documents.IgnoreQueryFilters().CountAsync()).Should().Be(1, "queda en la base marcado como borrado");
    }

    // ---------------------------------------------------------------- minutas

    [Fact]
    public async Task La_minuta_guarda_pendientes_y_los_sincroniza_al_editar()
    {
        using var env = await SetupAsync();
        var minute = await env.Minutes.SaveAsync(new Minute
        {
            ProjectId = env.Project.Id, Date = env.Today, Title = "Reunión semanal", Attendees = "Bryan, Ana",
            Items =
            [
                new MinuteItem { Description = "Entregar ATP SON19", SiteId = env.Site.Id, DueDate = env.Today.AddDays(2) },
                new MinuteItem { Description = "Comprar SFP" },
            ],
        });

        var loaded = (await env.Minutes.GetAsync(minute.Id))!;
        loaded.Items.Should().HaveCount(2);

        // Se edita: se cierra uno, se quita otro y se agrega uno nuevo.
        var keep = loaded.Items.Single(i => i.Description.StartsWith("Entregar"));
        keep.IsDone = true;
        loaded.Items = [keep, new MinuteItem { Description = "Cobrar contratista" }];
        await env.Minutes.SaveAsync(loaded);

        var after = (await env.Minutes.GetAsync(minute.Id))!;
        after.Items.Select(i => i.Description).Should().BeEquivalentTo("Entregar ATP SON19", "Cobrar contratista");
        after.Items.Single(i => i.IsDone).DoneOn.Should().Be(env.Today);
    }

    [Fact]
    public async Task Los_pendientes_abiertos_se_ordenan_por_fecha_y_se_filtran_por_nodo()
    {
        using var env = await SetupAsync();
        await env.Minutes.SaveAsync(new Minute
        {
            ProjectId = env.Project.Id, Date = env.Today, Title = "M1",
            Items =
            [
                new MinuteItem { Description = "Sin fecha" },
                new MinuteItem { Description = "Tarde", DueDate = env.Today.AddDays(9), SiteId = env.Site.Id },
                new MinuteItem { Description = "Pronto", DueDate = env.Today.AddDays(1) },
                new MinuteItem { Description = "Hecho", DueDate = env.Today, IsDone = true },
            ],
        });

        (await env.Minutes.PendingAsync(env.Project.Id)).Select(i => i.Description).Should().Equal("Pronto", "Tarde", "Sin fecha");
        (await env.Minutes.PendingAsync(env.Project.Id, env.Site.Id)).Select(i => i.Description).Should().Equal("Tarde");
    }

    [Fact]
    public async Task La_minuta_exige_titulo_y_descripcion_en_cada_pendiente()
    {
        using var env = await SetupAsync();

        var noTitle = () => env.Minutes.SaveAsync(new Minute { ProjectId = env.Project.Id, Date = env.Today, Title = " " });
        await noTitle.Should().ThrowAsync<BusinessRuleException>().WithMessage("*título*");

        var emptyItem = () => env.Minutes.SaveAsync(new Minute { ProjectId = env.Project.Id, Date = env.Today, Title = "X", Items = [new MinuteItem { Description = " " }] });
        await emptyItem.Should().ThrowAsync<BusinessRuleException>().WithMessage("*descripción*");
    }
}

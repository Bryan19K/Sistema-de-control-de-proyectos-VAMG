using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Common.Interfaces;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Infrastructure.Persistence;

namespace Tekcom.Gestion.Infrastructure.Services;

public sealed class EquipmentService(IDbContextFactory<ApplicationDbContext> factory, IDateTimeProvider clock) : IEquipmentService
{
    public async Task<List<EquipmentView>> ListAsync(int projectId, int? siteId = null, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var query = db.EquipmentItems.AsNoTracking().Include(e => e.Site).Include(e => e.Slots).Where(e => e.ProjectId == projectId);
        if (siteId is { } site)
        {
            query = query.Where(e => e.SiteId == site);
        }

        var equipment = await query.AsSplitQuery().ToListAsync(ct);
        var ids = equipment.Select(e => e.Id).ToList();
        var transceivers = await db.Transceivers.AsNoTracking().Include(t => t.Equipment).Include(t => t.Slot).Where(t => t.EquipmentId != null && ids.Contains(t.EquipmentId.Value)).ToListAsync(ct);

        return equipment
            .OrderBy(e => e.Kind).ThenBy(e => e.Name)
            .Select(e => new EquipmentView(e,
                e.Slots.OrderBy(s => s.SlotNumber).ToList(),
                transceivers.Where(t => t.EquipmentId == e.Id).OrderBy(t => t.SlotId).ThenBy(t => t.PortNumber).ToList()))
            .ToList();
    }

    public async Task<Equipment> SaveEquipmentAsync(Equipment equipment, CancellationToken ct = default)
    {
        var name = (equipment.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new BusinessRuleException("El nombre del equipo es obligatorio.");
        }

        await using var db = factory.CreateDbContext();
        await EnsureProjectAsync(db, equipment.ProjectId, ct);
        if (equipment.SiteId is { } siteId && !await db.Sites.AnyAsync(s => s.Id == siteId && s.ProjectId == equipment.ProjectId, ct))
        {
            throw new BusinessRuleException("El nodo no pertenece a este proyecto.");
        }

        var serial = Clean(equipment.Serial);
        if (serial is not null && await db.EquipmentItems.AnyAsync(e => e.ProjectId == equipment.ProjectId && e.Id != equipment.Id && e.Serial != null && e.Serial.ToLower() == serial.ToLower(), ct))
        {
            throw new BusinessRuleException($"Ya existe un equipo con el serial {serial} en este proyecto.");
        }

        var entity = equipment.Id == 0
            ? db.EquipmentItems.Add(new Equipment { ProjectId = equipment.ProjectId }).Entity
            : await db.EquipmentItems.FirstOrDefaultAsync(e => e.Id == equipment.Id, ct) ?? throw new BusinessRuleException("El equipo no existe.");

        entity.SiteId = equipment.SiteId;
        entity.Kind = equipment.Kind;
        entity.Name = name;
        entity.Brand = Clean(equipment.Brand);
        entity.Model = Clean(equipment.Model);
        entity.Serial = serial;
        entity.Status = equipment.Status;
        entity.Notes = Clean(equipment.Notes);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task RemoveEquipmentAsync(int equipmentId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var equipment = await db.EquipmentItems.FirstOrDefaultAsync(e => e.Id == equipmentId, ct);
        if (equipment is null)
        {
            return;
        }

        if (await db.EquipmentSlots.AnyAsync(s => s.EquipmentId == equipmentId, ct) || await db.Transceivers.AnyAsync(t => t.EquipmentId == equipmentId, ct))
        {
            throw new BusinessRuleException("El equipo ya tiene tarjetas o SFP registrados. Cámbielo a estado Retirado en lugar de quitarlo.");
        }

        db.EquipmentItems.Remove(equipment); // borrado lógico
        await db.SaveChangesAsync(ct);
    }

    public async Task<EquipmentSlot> SaveSlotAsync(EquipmentSlot slot, CancellationToken ct = default)
    {
        if (slot.SlotNumber < 0)
        {
            throw new BusinessRuleException("El número de ranura no puede ser negativo.");
        }

        if (slot.PortCount < 0 || slot.PortCount > 128)
        {
            throw new BusinessRuleException("Los puertos de la tarjeta deben estar entre 0 y 128.");
        }

        await using var db = factory.CreateDbContext();
        if (!await db.EquipmentItems.AnyAsync(e => e.Id == slot.EquipmentId, ct))
        {
            throw new BusinessRuleException("El equipo no existe.");
        }

        if (await db.EquipmentSlots.AnyAsync(s => s.EquipmentId == slot.EquipmentId && s.Id != slot.Id && s.SlotNumber == slot.SlotNumber, ct))
        {
            throw new BusinessRuleException($"La ranura {slot.SlotNumber} ya tiene una tarjeta registrada.");
        }

        var entity = slot.Id == 0
            ? db.EquipmentSlots.Add(new EquipmentSlot { EquipmentId = slot.EquipmentId }).Entity
            : await db.EquipmentSlots.FirstOrDefaultAsync(s => s.Id == slot.Id, ct) ?? throw new BusinessRuleException("La tarjeta no existe.");

        if (slot.Id != 0 && slot.PortCount < entity.PortCount
            && await db.Transceivers.AnyAsync(t => t.SlotId == slot.Id && t.PortNumber > slot.PortCount && t.Status == TransceiverStatus.Installed, ct))
        {
            throw new BusinessRuleException("Hay SFP instalados en puertos que quedarían fuera de la tarjeta.");
        }

        entity.SlotNumber = slot.SlotNumber;
        entity.CardModel = Clean(slot.CardModel);
        entity.CardSerial = Clean(slot.CardSerial);
        entity.PortCount = slot.PortCount;
        entity.Status = slot.Status;
        entity.Notes = Clean(slot.Notes);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task RemoveSlotAsync(int slotId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var slot = await db.EquipmentSlots.FirstOrDefaultAsync(s => s.Id == slotId, ct);
        if (slot is null)
        {
            return;
        }

        if (await db.Transceivers.AnyAsync(t => t.SlotId == slotId, ct))
        {
            throw new BusinessRuleException("La tarjeta tiene SFP registrados. Muévalos o márquelos como reemplazados primero.");
        }

        db.EquipmentSlots.Remove(slot);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<Transceiver>> TransceiversAsync(int projectId, string? search = null, TransceiverStatus? status = null, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var query = db.Transceivers.AsNoTracking().Include(t => t.Equipment).Include(t => t.Slot).Where(t => t.ProjectId == projectId);
        if (status is { } st)
        {
            query = query.Where(t => t.Status == st);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var text = search.Trim().ToLower();
            query = query.Where(t => t.Serial.ToLower().Contains(text) || (t.Model != null && t.Model.ToLower().Contains(text))
                                     || (t.Equipment != null && t.Equipment.Name.ToLower().Contains(text)));
        }

        return await query.OrderBy(t => t.Serial).ToListAsync(ct);
    }

    public async Task<Transceiver> SaveTransceiverAsync(Transceiver transceiver, CancellationToken ct = default)
    {
        var serial = (transceiver.Serial ?? string.Empty).Trim();
        if (serial.Length == 0)
        {
            throw new BusinessRuleException("El serial del SFP es obligatorio.");
        }

        await using var db = factory.CreateDbContext();
        await EnsureProjectAsync(db, transceiver.ProjectId, ct);

        if (await db.Transceivers.AnyAsync(t => t.ProjectId == transceiver.ProjectId && t.Id != transceiver.Id && t.Serial.ToLower() == serial.ToLower(), ct))
        {
            throw new BusinessRuleException($"El serial {serial} ya está registrado en este proyecto.");
        }

        if (transceiver.WavelengthNm is <= 0)
        {
            throw new BusinessRuleException("La longitud de onda debe ser mayor que cero.");
        }

        Equipment? equipment = null;
        if (transceiver.EquipmentId is { } equipmentId)
        {
            equipment = await db.EquipmentItems.AsNoTracking().FirstOrDefaultAsync(e => e.Id == equipmentId && e.ProjectId == transceiver.ProjectId, ct)
                ?? throw new BusinessRuleException("El equipo no pertenece a este proyecto.");
        }

        var installed = transceiver.Status == TransceiverStatus.Installed;
        if (installed && equipment is null)
        {
            throw new BusinessRuleException("Para marcar el SFP como instalado elija el equipo donde está.");
        }

        int? slotId = transceiver.SlotId;
        int? port = transceiver.PortNumber;
        if (equipment is null)
        {
            slotId = null;
            port = null;
        }

        if (slotId is { } sid)
        {
            var slot = await db.EquipmentSlots.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sid && s.EquipmentId == equipment!.Id, ct)
                ?? throw new BusinessRuleException("La tarjeta no pertenece al equipo elegido.");
            if (port is { } p)
            {
                if (p < 1 || p > slot.PortCount)
                {
                    throw new BusinessRuleException($"El puerto debe estar entre 1 y {slot.PortCount} (puertos de la tarjeta {slot.SlotNumber}).");
                }

                if (installed && await db.Transceivers.AnyAsync(t => t.Id != transceiver.Id && t.SlotId == sid && t.PortNumber == p && t.Status == TransceiverStatus.Installed, ct))
                {
                    throw new BusinessRuleException($"El puerto {p} de la ranura {slot.SlotNumber} ya tiene un SFP instalado.");
                }
            }
        }
        else if (port is not null)
        {
            throw new BusinessRuleException("Elija la tarjeta para indicar el puerto.");
        }

        var entity = transceiver.Id == 0
            ? db.Transceivers.Add(new Transceiver { ProjectId = transceiver.ProjectId }).Entity
            : await db.Transceivers.FirstOrDefaultAsync(t => t.Id == transceiver.Id, ct) ?? throw new BusinessRuleException("El SFP no existe.");

        var becameInstalled = installed && entity.Status != TransceiverStatus.Installed;
        entity.EquipmentId = equipment?.Id;
        entity.SlotId = slotId;
        entity.PortNumber = port;
        entity.Model = Clean(transceiver.Model);
        entity.Serial = serial;
        entity.WavelengthNm = transceiver.WavelengthNm;
        entity.Status = transceiver.Status;
        entity.Notes = Clean(transceiver.Notes);
        entity.InstalledOn = installed
            ? transceiver.InstalledOn ?? (becameInstalled || entity.InstalledOn is null ? clock.Today : entity.InstalledOn)
            : transceiver.InstalledOn;
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task RemoveTransceiverAsync(int transceiverId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var transceiver = await db.Transceivers.FirstOrDefaultAsync(t => t.Id == transceiverId, ct);
        if (transceiver is not null)
        {
            db.Transceivers.Remove(transceiver); // borrado lógico
            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task EnsureProjectAsync(ApplicationDbContext db, int projectId, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct))
        {
            throw new BusinessRuleException("El proyecto no existe.");
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

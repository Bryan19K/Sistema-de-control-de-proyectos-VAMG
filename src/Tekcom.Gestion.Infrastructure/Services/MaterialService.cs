using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Domain.Logic;
using Tekcom.Gestion.Infrastructure.Persistence;
using Tekcom.Gestion.Infrastructure.Persistence.Seed;

namespace Tekcom.Gestion.Infrastructure.Services;

public sealed class MaterialService(IDbContextFactory<ApplicationDbContext> factory) : IMaterialService
{
    public async Task<List<Material>> ListAsync(string? search = null, MaterialCategory? category = null,
        bool includeInactive = false, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var query = db.Materials.AsNoTracking().Include(m => m.Aliases).AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(m => m.IsActive);
        }

        if (category is not null)
        {
            query = query.Where(m => m.Category == category);
        }

        var list = await query.OrderBy(m => m.Category).ThenBy(m => m.LengthMeters).ThenBy(m => m.Name).ToListAsync(ct);

        var key = NameNormalizer.Key(search);
        if (key.Length > 0)
        {
            list = list.Where(m =>
                NameNormalizer.Key(m.Name).Contains(key)
                || NameNormalizer.Key(m.Code).Contains(key)
                || m.Aliases.Any(a => a.AliasKey.Contains(key))).ToList();
        }

        return list;
    }

    public async Task<Material?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        return await db.Materials.AsNoTracking().Include(m => m.Aliases).FirstOrDefaultAsync(m => m.Id == id, ct);
    }

    public async Task<Material> SaveAsync(Material material, CancellationToken ct = default)
    {
        var name = (material.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new BusinessRuleException("El nombre del material es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(material.Unit))
        {
            throw new BusinessRuleException("La unidad de medida es obligatoria.");
        }

        if (material.MinimumStock < 0)
        {
            throw new BusinessRuleException("El stock mínimo no puede ser negativo.");
        }

        await using var db = factory.CreateDbContext();
        var key = NameNormalizer.Key(name);
        var owner = await CatalogSeeder.FindByKeyAsync(db, key, ct);
        if (owner is not null && owner.Id != material.Id)
        {
            throw new BusinessRuleException($"'{name}' ya existe en el catálogo como '{owner.Name}'.");
        }

        var entity = material.Id == 0
            ? db.Materials.Add(new Material()).Entity
            : await db.Materials.FirstAsync(m => m.Id == material.Id, ct);

        entity.Name = name;
        entity.Code = string.IsNullOrWhiteSpace(material.Code) ? null : material.Code.Trim();
        entity.Unit = material.Unit.Trim();
        entity.Category = material.Category;
        entity.Group = material.Group;
        entity.LengthMeters = material.LengthMeters;
        entity.TracksStock = material.TracksStock;
        entity.MinimumStock = material.MinimumStock;
        entity.IsActive = material.IsActive;
        entity.Notes = string.IsNullOrWhiteSpace(material.Notes) ? null : material.Notes.Trim();

        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task AddAliasAsync(int materialId, string alias, CancellationToken ct = default)
    {
        alias = (alias ?? string.Empty).Trim();
        var key = NameNormalizer.Key(alias);
        if (key.Length == 0)
        {
            throw new BusinessRuleException("El alias es obligatorio.");
        }

        await using var db = factory.CreateDbContext();
        var material = await db.Materials.FirstOrDefaultAsync(m => m.Id == materialId, ct)
            ?? throw new BusinessRuleException("El material no existe.");

        var owner = await CatalogSeeder.FindByKeyAsync(db, key, ct);
        if (owner is not null)
        {
            throw new BusinessRuleException(owner.Id == materialId
                ? $"'{alias}' ya es el nombre o un alias de este material."
                : $"'{alias}' ya está asignado a '{owner.Name}'.");
        }

        db.MaterialAliases.Add(new MaterialAlias { MaterialId = material.Id, Alias = alias, AliasKey = key });
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAliasAsync(int aliasId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var alias = await db.MaterialAliases.FirstOrDefaultAsync(a => a.Id == aliasId, ct);
        if (alias is not null)
        {
            db.MaterialAliases.Remove(alias); // borrado lógico por el interceptor
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<Material?> ResolveAsync(string name, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        return await CatalogSeeder.FindByKeyAsync(db, NameNormalizer.Key(name), ct);
    }
}

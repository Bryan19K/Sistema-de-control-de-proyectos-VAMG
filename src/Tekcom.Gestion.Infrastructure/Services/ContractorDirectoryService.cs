using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Common.Interfaces;
using Tekcom.Gestion.Application.Models;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Logic;
using Tekcom.Gestion.Infrastructure.Persistence;

namespace Tekcom.Gestion.Infrastructure.Services;

public sealed class ContractorDirectoryService(IDbContextFactory<ApplicationDbContext> factory, IDateTimeProvider clock) : IContractorDirectoryService
{
    public async Task<List<ContractorView>> ListAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var contractors = await db.Contractors.AsNoTracking().Where(c => includeInactive || c.IsActive).ToListAsync(ct);
        var people = await db.ContractorPeople.AsNoTracking().Where(p => p.IsActive).ToListAsync(ct);
        var today = clock.Today;

        return contractors
            .OrderBy(c => c.Stage).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c =>
            {
                var states = people.Where(p => p.ContractorId == c.Id)
                    .Select(p => CoverageCalculator.Evaluate(p.InsuranceStatus, p.PolicyNumber, p.CoverageEnd, today)).ToList();
                return new ContractorView(c, states.Count, states.Count(s => s == CoverageState.Covered),
                    states.Count(s => s == CoverageState.Pending), states.Count(s => s == CoverageState.Expired),
                    states.Count(s => s == CoverageState.ExpiringSoon));
            }).ToList();
    }

    public async Task<Contractor?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        return await db.Contractors.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<Contractor> SaveAsync(Contractor contractor, CancellationToken ct = default)
    {
        var name = (contractor.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new BusinessRuleException("El nombre del contratista es obligatorio.");
        }

        await using var db = factory.CreateDbContext();
        var key = NameNormalizer.Key(name);
        var others = await db.Contractors.AsNoTracking().Where(c => c.Id != contractor.Id).Select(c => c.Name).ToListAsync(ct);
        if (others.Any(o => NameNormalizer.Key(o) == key))
        {
            throw new BusinessRuleException($"Ya existe un contratista llamado '{name}'.");
        }

        if (contractor.Crews is < 0)
        {
            throw new BusinessRuleException("Las cuadrillas no pueden ser negativas.");
        }

        Contractor target;
        if (contractor.Id == 0)
        {
            target = new Contractor();
            db.Contractors.Add(target);
        }
        else
        {
            target = await db.Contractors.FirstOrDefaultAsync(c => c.Id == contractor.Id, ct)
                ?? throw new BusinessRuleException("El contratista no existe.");
        }

        target.Name = name;
        target.ResponsibleName = Clean(contractor.ResponsibleName);
        target.Phone = Clean(contractor.Phone);
        target.IsActive = contractor.IsActive;
        target.Stage = contractor.Stage;
        target.ProviderType = Clean(contractor.ProviderType);
        target.LegalRepresentative = Clean(contractor.LegalRepresentative);
        target.Dui = Clean(contractor.Dui);
        target.Nrc = Clean(contractor.Nrc);
        target.Address = Clean(contractor.Address);
        target.Email = Clean(contractor.Email);
        target.Crews = contractor.Crews;
        target.SupervisorName = Clean(contractor.SupervisorName);
        target.SupervisorPhone = Clean(contractor.SupervisorPhone);
        target.CrewLeaderName = Clean(contractor.CrewLeaderName);
        target.CrewLeaderPhone = Clean(contractor.CrewLeaderPhone);
        target.BankHolder = Clean(contractor.BankHolder);
        target.Bank = Clean(contractor.Bank);
        target.AccountType = Clean(contractor.AccountType);
        target.AccountNumber = Clean(contractor.AccountNumber);
        target.Notes = Clean(contractor.Notes);
        await db.SaveChangesAsync(ct);
        return target;
    }

    public async Task SetActiveAsync(int id, bool active, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var contractor = await db.Contractors.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new BusinessRuleException("El contratista no existe.");
        contractor.IsActive = active;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<PersonView>> PeopleAsync(int? contractorId = null, CoverageState? state = null, string? search = null,
        bool onlyNeedingAttention = false, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var query = db.ContractorPeople.AsNoTracking().Include(p => p.Contractor).Where(p => p.IsActive);
        if (contractorId is { } id)
        {
            query = query.Where(p => p.ContractorId == id);
        }

        var list = await query.ToListAsync(ct);
        var today = clock.Today;
        var text = NameNormalizer.Key(search);
        return list
            .Select(p => new PersonView(p, p.Contractor?.Name ?? "—", CoverageCalculator.Evaluate(p.InsuranceStatus, p.PolicyNumber, p.CoverageEnd, today)))
            .Where(v => state is null || v.State == state)
            .Where(v => !onlyNeedingAttention || CoverageCalculator.NeedsAttention(v.State))
            .Where(v => text.Length == 0 || NameNormalizer.Key(v.Person.FullName).Contains(text, StringComparison.Ordinal)
                        || NameNormalizer.Key(v.Person.Dui).Contains(text, StringComparison.Ordinal))
            .OrderBy(v => v.ContractorName, StringComparer.OrdinalIgnoreCase).ThenBy(v => v.Person.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<ContractorPerson> SavePersonAsync(ContractorPerson person, CancellationToken ct = default)
    {
        var name = (person.FullName ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new BusinessRuleException("El nombre de la persona es obligatorio.");
        }

        await using var db = factory.CreateDbContext();
        if (!await db.Contractors.AnyAsync(c => c.Id == person.ContractorId, ct))
        {
            throw new BusinessRuleException("Elija el contratista de la persona.");
        }

        ContractorPerson target;
        if (person.Id == 0)
        {
            target = new ContractorPerson { ContractorId = person.ContractorId };
            db.ContractorPeople.Add(target);
        }
        else
        {
            target = await db.ContractorPeople.FirstOrDefaultAsync(p => p.Id == person.Id, ct)
                ?? throw new BusinessRuleException("La persona no existe.");
        }

        target.ContractorId = person.ContractorId;
        target.FullName = name;
        target.Dui = Clean(person.Dui);
        target.Position = Clean(person.Position);
        target.Phone = Clean(person.Phone);
        target.InsuranceType = Clean(person.InsuranceType);
        target.InsuranceStatus = Clean(person.InsuranceStatus);
        target.Insurer = Clean(person.Insurer);
        target.PolicyNumber = Clean(person.PolicyNumber);
        target.CoverageEnd = person.CoverageEnd ?? CoverageCalculator.ParseEnd(person.InsuranceStatus);
        target.Notes = Clean(person.Notes);
        target.IsActive = person.IsActive;
        await db.SaveChangesAsync(ct);
        return target;
    }

    public async Task RemovePersonAsync(int personId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var person = await db.ContractorPeople.FirstOrDefaultAsync(p => p.Id == personId, ct)
            ?? throw new BusinessRuleException("La persona no existe.");
        db.ContractorPeople.Remove(person);
        await db.SaveChangesAsync(ct);
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

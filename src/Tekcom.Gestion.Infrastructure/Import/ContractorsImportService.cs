using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Import;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Logic;
using Tekcom.Gestion.Infrastructure.Persistence;

namespace Tekcom.Gestion.Infrastructure.Import;

internal sealed record CtContractor(string Name, Dictionary<string, string?> Fields, int? Crews, ContractorStage Stage);

internal sealed record CtPerson(string Contractor, string FullName, string? Dui, string? Position, string? Phone, string? Type,
    string? Status, string? Insurer, string? Policy, string? Notes);

internal sealed class ContractorData
{
    public List<CtContractor> Contractors { get; } = [];
    public List<CtPerson> People { get; } = [];
    public int SkippedContractors { get; set; }
    public int SkippedPeople { get; set; }
}

/// <summary>
/// Carga la base de datos de contratistas y su personal de campo con póliza o afiliación.
/// Optical Precision ya no trabaja con TEK-COM: ni la empresa ni su gente se cargan.
/// Importar de nuevo solo completa lo que esté vacío, para no pisar lo que se editó en el sistema.
/// </summary>
public sealed class ContractorsImportService(IDbContextFactory<ApplicationDbContext> factory) : IDataPackImporter
{
    public PackKind Kind => PackKind.Contractors;

    internal static bool IsExcluded(string? name)
    {
        var key = NameNormalizer.Key(name);
        return key.Contains("OPTICAL", StringComparison.Ordinal) && (key.Contains("PRES", StringComparison.Ordinal) || key.Contains("PREC", StringComparison.Ordinal));
    }

    private static string? Value(string text)
    {
        var t = text.Trim();
        return t.Length == 0 || t.Equals("N/A", StringComparison.OrdinalIgnoreCase) ? null : t;
    }

    public Task<PackAnalysis?> AnalyzeAsync(byte[] file, string fileName, CancellationToken ct = default)
    {
        using var book = Xl.Open(file);
        var main = book is null ? null : Xl.Sheet(book, "DATOS DE CONTRATISTAS");
        var staff = book is null ? null : Xl.Sheet(book, "PERSONAL EN CAMPO");
        if (book is null || (main is null && staff is null))
        {
            return Task.FromResult<PackAnalysis?>(null);
        }

        var report = new ImportReport { FileName = fileName };
        var data = new ContractorData();
        if (main is not null)
        {
            ReadContractors(main, data);
        }

        if (staff is not null)
        {
            ReadPeople(staff, data);
        }

        if (data.SkippedContractors + data.SkippedPeople > 0)
        {
            report.Issues.Add(new ImportIssue(ImportSeverity.Info, "(archivo)", null, null,
                $"Optical Precision ya no trabaja con TEK-COM: se omitieron {data.SkippedContractors} contratista y {data.SkippedPeople} personas suyas."));
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var pending = data.People.Count(p => CoverageCalculator.Evaluate(p.Status, p.Policy, CoverageCalculator.ParseEnd(p.Status), today) == CoverageState.Pending);
        report.Summary.Add(new("Contratistas", $"{data.Contractors.Count} ({data.Contractors.Count(c => c.Stage == ContractorStage.Active)} activos, " +
                                              $"{data.Contractors.Count(c => c.Stage == ContractorStage.Prospect)} por cotizar, {data.Contractors.Count(c => c.Stage == ContractorStage.Declined)} dijeron que no)"));
        report.Summary.Add(new("Personal de campo", $"{data.People.Count} personas · {pending} con póliza o afiliación pendiente"));
        report.Issues.Add(new ImportIssue(ImportSeverity.Info, "(archivo)", null, null,
            "Los datos personales (DUI, cuentas bancarias) solo los ve el administrador. Si vuelve a cargar el archivo, solo se completa lo que esté vacío."));

        return Task.FromResult<PackAnalysis?>(new PackAnalysis
        {
            Kind = PackKind.Contractors,
            Title = "Base de datos de contratistas y personal",
            Description = "Contratistas con sus datos, cuadrillas y personal de campo con póliza o número de afiliación.",
            Report = report,
            Payload = data,
        });
    }

    private static void ReadContractors(IXLWorksheet sheet, ContractorData data)
    {
        var last = sheet.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = 4; r <= last; r++)
        {
            var name = Xl.Text(sheet, r, 2);
            if (name.Length == 0)
            {
                continue;
            }

            if (IsExcluded(name))
            {
                data.SkippedContractors++;
                continue;
            }

            var fields = new Dictionary<string, string?>
            {
                ["ProviderType"] = Value(Xl.Text(sheet, r, 3)), ["LegalRepresentative"] = Value(Xl.Text(sheet, r, 4)),
                ["Dui"] = Value(Xl.Text(sheet, r, 5)), ["Nrc"] = Value(Xl.Text(sheet, r, 6)), ["Address"] = Value(Xl.Text(sheet, r, 7)),
                ["Phone"] = Value(Xl.Text(sheet, r, 8)), ["Email"] = Value(Xl.Text(sheet, r, 9)),
                ["SupervisorName"] = Value(Xl.Text(sheet, r, 11)), ["SupervisorPhone"] = Value(Xl.Text(sheet, r, 12)),
                ["CrewLeaderName"] = Value(Xl.Text(sheet, r, 13)), ["CrewLeaderPhone"] = Value(Xl.Text(sheet, r, 14)),
                ["BankHolder"] = Value(Xl.Text(sheet, r, 15)), ["Bank"] = Value(Xl.Text(sheet, r, 16)), ["AccountType"] = Value(Xl.Text(sheet, r, 17)),
                ["AccountNumber"] = Value(Xl.Text(sheet, r, 18)), ["Notes"] = Value(Xl.Text(sheet, r, 19)),
            };
            var crews = Xl.Number(sheet, r, 10);
            var notes = NameNormalizer.Key(fields["Notes"]);
            var stage = fields["Nrc"] is not null || fields["Dui"] is not null
                ? ContractorStage.Active
                : notes.StartsWith("DIJO QUE NO", StringComparison.Ordinal) ? ContractorStage.Declined : ContractorStage.Prospect;
            data.Contractors.Add(new CtContractor(name, fields, crews is null ? null : (int)crews.Value, stage));
        }
    }

    private static void ReadPeople(IXLWorksheet sheet, ContractorData data)
    {
        var last = sheet.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = 3; r <= last; r++)
        {
            var name = Xl.Text(sheet, r, 4);
            var contractor = Xl.Text(sheet, r, 3);
            if (name.Length == 0 || contractor.Length == 0)
            {
                continue;
            }

            if (IsExcluded(contractor))
            {
                data.SkippedPeople++;
                continue;
            }

            data.People.Add(new CtPerson(contractor, name, Value(Xl.Text(sheet, r, 5)), Value(Xl.Text(sheet, r, 6)), Value(Xl.Text(sheet, r, 7)),
                Value(Xl.Text(sheet, r, 8)), Value(Xl.Text(sheet, r, 9)), Value(Xl.Text(sheet, r, 10)), Value(Xl.Text(sheet, r, 11)),
                Value(Xl.Text(sheet, r, 12))));
        }
    }

    public async Task<ImportReport> ApplyAsync(PackAnalysis analysis, int? projectId, CancellationToken ct = default)
    {
        var data = (ContractorData)analysis.Payload!;
        var report = new ImportReport { FileName = analysis.Report.FileName };
        report.Issues.AddRange(analysis.Report.Issues);

        await using var db = factory.CreateDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var contractors = await db.Contractors.ToListAsync(ct);

        // Optical Precision ya no trabaja con TEK-COM: se marca inactivo (su historial de entregas se conserva).
        var deactivated = 0;
        foreach (var excluded in contractors.Where(c => IsExcluded(c.Name) && c.IsActive))
        {
            excluded.IsActive = false;
            deactivated++;
        }

        var created = 0;
        foreach (var item in data.Contractors)
        {
            var contractor = ContractorMatcher.Find(contractors, item.Name);
            if (contractor is null)
            {
                contractor = new Contractor { Name = Xl.TitleCase(item.Name), Stage = item.Stage };
                db.Contractors.Add(contractor);
                contractors.Add(contractor);
                created++;
            }

            Fill(contractor, item.Fields);
            contractor.Crews ??= item.Crews;
        }

        await db.SaveChangesAsync(ct);

        var people = await db.ContractorPeople.ToListAsync(ct);
        var peopleCreated = 0;
        var peopleUpdated = 0;
        foreach (var item in data.People)
        {
            var contractor = ContractorMatcher.Find(contractors, item.Contractor);
            if (contractor is null)
            {
                contractor = new Contractor { Name = Xl.TitleCase(item.Contractor), Stage = ContractorStage.Active };
                db.Contractors.Add(contractor);
                contractors.Add(contractor);
                await db.SaveChangesAsync(ct);
                created++;
            }

            var duiKey = DigitsOnly(item.Dui);
            var nameKey = NameNormalizer.Key(item.FullName);
            var person = people.FirstOrDefault(p => p.ContractorId == contractor.Id &&
                (duiKey.Length >= 8 ? DigitsOnly(p.Dui) == duiKey : NameNormalizer.Key(p.FullName) == nameKey));
            var isNew = person is null;
            if (person is null)
            {
                person = new ContractorPerson { ContractorId = contractor.Id, FullName = item.FullName.Trim() };
                db.ContractorPeople.Add(person);
                people.Add(person);
                peopleCreated++;
            }
            else
            {
                peopleUpdated++;
            }

            person.Dui ??= item.Dui;
            person.Position ??= item.Position;
            person.Phone ??= item.Phone;
            person.InsuranceType ??= item.Type;
            person.InsuranceStatus ??= item.Status;
            person.Insurer ??= item.Insurer;
            person.PolicyNumber ??= item.Policy;
            person.Notes ??= item.Notes;
            if (isNew || person.CoverageEnd is null)
            {
                person.CoverageEnd ??= CoverageCalculator.ParseEnd(person.InsuranceStatus);
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var states = (await db.ContractorPeople.AsNoTracking().Where(p => p.IsActive).ToListAsync(ct))
            .Select(p => CoverageCalculator.Evaluate(p.InsuranceStatus, p.PolicyNumber, p.CoverageEnd, today)).ToList();
        report.Summary.Add(new("Contratistas nuevos", created.ToString(CultureInfo.InvariantCulture)));
        report.Summary.Add(new("Personal nuevo", peopleCreated.ToString(CultureInfo.InvariantCulture)));
        report.Summary.Add(new("Personal que ya estaba", peopleUpdated.ToString(CultureInfo.InvariantCulture)));
        report.Summary.Add(new("Personal con póliza o afiliación pendiente", states.Count(s => s == CoverageState.Pending).ToString(CultureInfo.InvariantCulture)));
        if (deactivated > 0)
        {
            report.Summary.Add(new("Contratistas dados de baja (Optical Precision)", deactivated.ToString(CultureInfo.InvariantCulture)));
        }

        report.Applied = true;
        return report;
    }

    private static string DigitsOnly(string? text) => new((text ?? string.Empty).Where(char.IsDigit).ToArray());

    private static void Fill(Contractor c, Dictionary<string, string?> f)
    {
        c.ProviderType ??= f["ProviderType"];
        c.LegalRepresentative ??= f["LegalRepresentative"];
        c.Dui ??= f["Dui"];
        c.Nrc ??= f["Nrc"];
        c.Address ??= f["Address"];
        c.Phone ??= f["Phone"];
        c.Email ??= f["Email"];
        c.SupervisorName ??= f["SupervisorName"];
        c.SupervisorPhone ??= f["SupervisorPhone"];
        c.CrewLeaderName ??= f["CrewLeaderName"];
        c.CrewLeaderPhone ??= f["CrewLeaderPhone"];
        c.BankHolder ??= f["BankHolder"];
        c.Bank ??= f["Bank"];
        c.AccountType ??= f["AccountType"];
        c.AccountNumber ??= f["AccountNumber"];
        c.Notes ??= f["Notes"];
        c.ResponsibleName ??= f["LegalRepresentative"];
    }
}

/// <summary>Encuentra el contratista aunque el nombre cambie un poco entre hojas (NEFTALI/NEFTALY, "Jonathan Eduardo Olivares Orellana"/"JONATHAN OLIVARES").</summary>
internal static class ContractorMatcher
{
    public static Contractor? Find(IReadOnlyList<Contractor> contractors, string name)
    {
        var key = NameNormalizer.Key(name);
        if (key.Length == 0)
        {
            return null;
        }

        var exact = contractors.FirstOrDefault(c => NameNormalizer.Key(c.Name) == key);
        if (exact is not null)
        {
            return exact;
        }

        var tokens = key.Split(' ').ToHashSet();
        foreach (var c in contractors)
        {
            var other = NameNormalizer.Key(c.Name).Split(' ').ToHashSet();
            if (other.Count > 0 && (other.IsSubsetOf(tokens) || tokens.IsSubsetOf(other)) && Math.Min(other.Count, tokens.Count) >= 1 && Shared(tokens, other) >= 1 && key.Length > 3)
            {
                return c;
            }
        }

        return contractors.FirstOrDefault(c => Distance(NameNormalizer.Key(c.Name), key) <= 1);
    }

    private static int Shared(HashSet<string> a, HashSet<string> b) => a.Count(b.Contains);

    private static int Distance(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > 1)
        {
            return 99;
        }

        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) { d[i, 0] = i; }
        for (var j = 0; j <= b.Length; j++) { d[0, j] = j; }
        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }
        }

        return d[a.Length, b.Length];
    }
}

using Tekcom.Gestion.Domain.Entities;

namespace Tekcom.Gestion.Application.Models;

public sealed record FundBalance(ExpenseFund Fund, decimal Spent, int Entries)
{
    public decimal Balance => Fund.AssignedAmount - Spent;
    public bool Overspent => Balance < 0;
}

public sealed record CostCenterTotal(string CostCenter, string? ProjectName, decimal Total, int Entries);

public sealed record CategoryTotal(string Category, decimal Total);

public sealed class ExpenseSummary
{
    public decimal PurchasesSubtotal { get; init; }
    public decimal PurchasesVat { get; init; }
    public decimal PurchasesTotal { get; init; }
    public decimal PettyCashTotal { get; init; }
    public decimal TravelTotal { get; init; }
    public decimal GrandTotal => PurchasesTotal + PettyCashTotal + TravelTotal;
    public int Entries { get; init; }
    public List<CategoryTotal> ByCategory { get; init; } = [];
    public List<CostCenterTotal> ByCostCenter { get; init; } = [];
    public List<FundBalance> Funds { get; init; } = [];
}

public sealed record OrdersSummary(decimal Subtotal, decimal Vat, decimal Total, int Orders, int HomesPassed);

public sealed class BuildingOverview
{
    public required Project Project { get; init; }
    public List<ProjectPhase> Phases { get; init; } = [];
    public int Units { get; init; }
    public int UnitsFinished { get; init; }
    public int Levels { get; init; }
    public int MaterialLines { get; init; }
}

public sealed record BuildingMaterialRow(string Material, string Unit, decimal Assigned, decimal Installed, decimal Returned)
{
    /// <summary>Lo asignado que no se instaló ni se devolvió.</summary>
    public decimal PendingReturn => Assigned - Installed - Returned;
}

public sealed record ContractorView(Contractor Contractor, int People, int Covered, int Pending, int Expired, int ExpiringSoon)
{
    public int NeedAttention => Pending + Expired + ExpiringSoon;
}

public sealed record PersonView(ContractorPerson Person, string ContractorName, Tekcom.Gestion.Domain.Logic.CoverageState State);

public enum LoadCheckState
{
    /// <summary>Coincide con lo que traía el Excel.</summary>
    Ok = 0,

    /// <summary>No hay nada cargado de este dato: falta subir el Excel.</summary>
    Missing = 1,

    /// <summary>Hay datos pero la cifra no coincide con el Excel (suele ser por ediciones hechas en el sistema).</summary>
    Different = 2,
}

public sealed record LoadCheck(string File, string Name, string Expected, string Actual, LoadCheckState State);

public sealed record LoadFileStatus(string File, string Description, bool Loaded, int Differences, IReadOnlyList<LoadCheck> Checks);

public sealed class LoadVerification
{
    public List<LoadFileStatus> Files { get; init; } = [];
    public bool AllLoaded => Files.All(f => f.Loaded);
    public IEnumerable<string> Missing => Files.Where(f => !f.Loaded).Select(f => f.File);
    public int Differences => Files.Sum(f => f.Differences);
}

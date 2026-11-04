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

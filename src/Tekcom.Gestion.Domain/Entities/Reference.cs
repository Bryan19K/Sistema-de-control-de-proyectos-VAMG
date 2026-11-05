using Tekcom.Gestion.Domain.Common;
using Tekcom.Gestion.Domain.Enums;

namespace Tekcom.Gestion.Domain.Entities;

public class Client : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
}

public class ProjectType : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ProjectModules Modules { get; set; } = ProjectModules.General;

    /// <summary>Los tipos de sistema (FTTH, General) no se pueden eliminar.</summary>
    public bool IsSystem { get; set; }

    public bool Has(ProjectModules module) => (Modules & module) == module;
}

public class Supervisor : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Contratista o brigada que instala en los nodos.</summary>
public class Contractor : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? ResponsibleName { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;

    // Base de datos de contratistas
    public ContractorStage Stage { get; set; } = ContractorStage.Active;
    public string? ProviderType { get; set; }
    public string? LegalRepresentative { get; set; }
    public string? Dui { get; set; }
    public string? Nrc { get; set; }
    public string? Address { get; set; }
    public string? Email { get; set; }
    public int? Crews { get; set; }
    public string? SupervisorName { get; set; }
    public string? SupervisorPhone { get; set; }
    public string? CrewLeaderName { get; set; }
    public string? CrewLeaderPhone { get; set; }
    public string? BankHolder { get; set; }
    public string? Bank { get; set; }
    public string? AccountType { get; set; }
    public string? AccountNumber { get; set; }
    public string? Notes { get; set; }

    public ICollection<ContractorPerson> People { get; set; } = [];
}

public enum ContractorStage
{
    /// <summary>Contratista que trabaja con TEK-COM.</summary>
    Active = 0,

    /// <summary>Se le envió el preciario o el archivo para cotizar.</summary>
    Prospect = 1,

    /// <summary>Dijo que no.</summary>
    Declined = 2,
}

/// <summary>Persona de campo de un contratista, con su seguro de vida o afiliación al ISSS.</summary>
public class ContractorPerson : AuditableEntity
{
    public int ContractorId { get; set; }
    public Contractor? Contractor { get; set; }

    public string FullName { get; set; } = string.Empty;
    public string? Dui { get; set; }
    public string? Position { get; set; }
    public string? Phone { get; set; }

    /// <summary>ISSS (Seguro Social), Seguro de Vida a Término…</summary>
    public string? InsuranceType { get; set; }

    /// <summary>Texto como lo lleva el Excel: Vigente, Inscrito, PENDIENTE, "Vigencia hasta 31/10/2026"…</summary>
    public string? InsuranceStatus { get; set; }
    public string? Insurer { get; set; }

    /// <summary>Número de póliza o de afiliación.</summary>
    public string? PolicyNumber { get; set; }

    /// <summary>Fin de la vigencia, si se conoce.</summary>
    public DateOnly? CoverageEnd { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

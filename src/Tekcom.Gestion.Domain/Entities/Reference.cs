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
}

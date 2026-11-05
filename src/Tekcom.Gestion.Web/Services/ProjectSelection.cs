using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;

namespace Tekcom.Gestion.Web.Services;

/// <summary>
/// Proyecto con el que se está trabajando. Vive mientras dura la sesión del navegador, así que las
/// secciones (nodos, materiales, bodega...) muestran siempre el mismo proyecto.
/// </summary>
public sealed class ProjectSelection(IProjectService projects)
{
    public int? CurrentId { get; private set; }

    public async Task<List<Project>> ListAsync() => await projects.ListAsync();

    /// <summary>Si no hay proyecto elegido, toma el primero activo; los de FTTH van primero porque son los que tienen nodos y bodega.</summary>
    public async Task<int?> EnsureAsync()
    {
        if (CurrentId is not null)
        {
            return CurrentId;
        }

        var all = await projects.ListAsync();
        var ordered = all.OrderBy(p => p.ProjectType?.Code == "FTTH" ? 0 : 1).ToList();
        CurrentId = (ordered.FirstOrDefault(p => p.Status == ProjectStatus.Active) ?? ordered.FirstOrDefault())?.Id;
        return CurrentId;
    }

    public void Select(int id) => CurrentId = id;
}

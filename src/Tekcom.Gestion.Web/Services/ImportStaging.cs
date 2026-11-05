using System.Collections.Concurrent;
using Tekcom.Gestion.Application.Import;

namespace Tekcom.Gestion.Web.Services;

/// <summary>Un archivo ya leído que espera confirmación (o ya importado), guardado en el servidor.</summary>
public sealed class StagedImport
{
    public required string Id { get; init; }
    public required string User { get; init; }
    public required string FileName { get; init; }
    public DateTimeOffset Created { get; } = DateTimeOffset.UtcNow;
    public List<PackAnalysis> Packs { get; set; } = [];
    public List<PackAnalysis>? Done { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Guarda en memoria lo que se leyó de cada Excel. El archivo se sube con una petición web normal (no por la conexión
/// en vivo de Blazor, que el celular corta al abrir el selector de archivos) y la pantalla lo recupera por su id.
/// </summary>
public sealed class ImportStaging
{
    private readonly ConcurrentDictionary<string, StagedImport> _items = new();
    private static readonly TimeSpan Retention = TimeSpan.FromHours(2);

    public StagedImport Add(string user, string fileName)
    {
        Cleanup();
        var item = new StagedImport { Id = Guid.NewGuid().ToString("N"), User = user, FileName = fileName };
        _items[item.Id] = item;
        return item;
    }

    public StagedImport? Get(string? id, string user) =>
        id is not null && _items.TryGetValue(id, out var item) && string.Equals(item.User, user, StringComparison.OrdinalIgnoreCase) ? item : null;

    private void Cleanup()
    {
        var limit = DateTimeOffset.UtcNow - Retention;
        foreach (var (key, item) in _items)
        {
            if (item.Created < limit)
            {
                _items.TryRemove(key, out _);
            }
        }
    }
}

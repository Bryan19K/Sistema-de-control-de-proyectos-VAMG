namespace Tekcom.Gestion.Application.Common.Interfaces;

/// <summary>Almacenamiento de archivos adjuntos (tareas, documentos, fotos de ATP).</summary>
public interface IFileStorage
{
    /// <summary>Guarda el archivo y devuelve el nombre con el que quedó almacenado.</summary>
    Task<string> SaveAsync(Stream content, string originalName, CancellationToken ct = default);

    Task<Stream?> OpenReadAsync(string storedName, CancellationToken ct = default);

    Task DeleteAsync(string storedName, CancellationToken ct = default);
}

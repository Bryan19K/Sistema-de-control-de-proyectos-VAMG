using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Common.Interfaces;

namespace Tekcom.Gestion.Infrastructure.Services;

/// <summary>
/// Guarda los archivos en una carpeta del servidor (Storage:Path; por defecto App_Data/uploads).
/// El nombre guardado lo genera el sistema, así que un nombre de archivo malicioso no puede salirse
/// de la carpeta. En Azure App Service la carpeta debe estar bajo D:\home para que persista.
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    public const long MaxFileBytes = 20 * 1024 * 1024;

    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".bat", ".cmd", ".com", ".msi", ".ps1", ".vbs", ".js", ".jar", ".scr", ".sh",
    };

    private readonly string _root;

    public LocalFileStorage(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration["Storage:Path"];
        _root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(environment.ContentRootPath, "App_Data", "uploads")
            : configured;
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(Stream content, string originalName, CancellationToken ct = default)
    {
        var extension = Path.GetExtension(originalName ?? string.Empty);
        if (BlockedExtensions.Contains(extension))
        {
            throw new BusinessRuleException($"No se permiten archivos {extension}.");
        }

        var stored = Guid.NewGuid().ToString("N") + extension.ToLowerInvariant();
        var path = Path.Combine(_root, stored);

        await using var target = File.Create(path);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await content.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > MaxFileBytes)
            {
                target.Close();
                File.Delete(path);
                throw new BusinessRuleException("El archivo supera el máximo de 20 MB.");
            }

            await target.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        return stored;
    }

    public Task<Stream?> OpenReadAsync(string storedName, CancellationToken ct = default)
    {
        var path = SafePath(storedName);
        return Task.FromResult<Stream?>(File.Exists(path) ? File.OpenRead(path) : null);
    }

    public Task DeleteAsync(string storedName, CancellationToken ct = default)
    {
        var path = SafePath(storedName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string SafePath(string storedName)
    {
        var name = Path.GetFileName(storedName);
        if (string.IsNullOrWhiteSpace(name) || name != storedName)
        {
            throw new BusinessRuleException("Nombre de archivo inválido.");
        }

        return Path.Combine(_root, name);
    }
}

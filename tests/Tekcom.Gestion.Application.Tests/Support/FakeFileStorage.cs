using Tekcom.Gestion.Application.Common.Interfaces;

namespace Tekcom.Gestion.Application.Tests.Support;

public sealed class FakeFileStorage : IFileStorage
{
    private readonly Dictionary<string, byte[]> _files = [];

    public int Count => _files.Count;

    public async Task<string> SaveAsync(Stream content, string originalName, CancellationToken ct = default)
    {
        using var memory = new MemoryStream();
        await content.CopyToAsync(memory, ct);
        var name = Guid.NewGuid().ToString("N") + Path.GetExtension(originalName);
        _files[name] = memory.ToArray();
        return name;
    }

    public Task<Stream?> OpenReadAsync(string storedName, CancellationToken ct = default) =>
        Task.FromResult<Stream?>(_files.TryGetValue(storedName, out var bytes) ? new MemoryStream(bytes) : null);

    public Task DeleteAsync(string storedName, CancellationToken ct = default)
    {
        _files.Remove(storedName);
        return Task.CompletedTask;
    }
}

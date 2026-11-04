using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Common.Interfaces;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Infrastructure.Persistence;

namespace Tekcom.Gestion.Infrastructure.Services;

public sealed class DocumentService(IDbContextFactory<ApplicationDbContext> factory, IFileStorage storage) : IDocumentService
{
    public async Task<List<ProjectDocument>> ListAsync(DocumentFilter filter, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var query = db.Documents.AsNoTracking().Include(d => d.Site).Include(d => d.TaskItem).Where(d => d.ProjectId == filter.ProjectId);
        if (filter.SiteId is { } siteId) { query = query.Where(d => d.SiteId == siteId); }
        if (filter.TaskItemId is { } taskId) { query = query.Where(d => d.TaskItemId == taskId); }
        if (filter.Kind is { } kind) { query = query.Where(d => d.Kind == kind); }

        return (await query.ToListAsync(ct)).OrderByDescending(d => d.DocumentDate).ThenByDescending(d => d.Id).ToList();
    }

    public async Task<ProjectDocument> UploadAsync(DocumentUpload upload, Stream content, CancellationToken ct = default)
    {
        var title = (upload.Title ?? string.Empty).Trim();
        var fileName = Path.GetFileName(upload.FileName ?? string.Empty);
        if (title.Length == 0)
        {
            title = Path.GetFileNameWithoutExtension(fileName);
        }

        if (title.Length == 0 || fileName.Length == 0)
        {
            throw new BusinessRuleException("Elija el archivo y escriba un título.");
        }

        await using var db = factory.CreateDbContext();
        await ValidateLinksAsync(db, upload.ProjectId, upload.SiteId, upload.TaskItemId, ct);

        // El almacenamiento valida tamaño y tipo de archivo (rechaza ejecutables).
        var stored = await storage.SaveAsync(content, fileName, ct);
        long size = 0;
        await using (var check = await storage.OpenReadAsync(stored, ct))
        {
            size = check?.Length ?? 0;
        }

        var document = new ProjectDocument
        {
            ProjectId = upload.ProjectId,
            SiteId = upload.SiteId,
            TaskItemId = upload.TaskItemId,
            Kind = upload.Kind,
            Title = title,
            DocumentDate = upload.Date,
            IsSigned = upload.IsSigned,
            Notes = string.IsNullOrWhiteSpace(upload.Notes) ? null : upload.Notes.Trim(),
            FileName = fileName,
            StoredName = stored,
            ContentType = string.IsNullOrWhiteSpace(upload.ContentType) ? "application/octet-stream" : upload.ContentType,
            Size = size,
        };
        db.Documents.Add(document);
        await db.SaveChangesAsync(ct);
        return document;
    }

    public async Task UpdateAsync(int documentId, string title, DocumentKind kind, DateOnly date, bool isSigned, string? notes, int? siteId, int? taskItemId, CancellationToken ct = default)
    {
        title = (title ?? string.Empty).Trim();
        if (title.Length == 0)
        {
            throw new BusinessRuleException("El título es obligatorio.");
        }

        await using var db = factory.CreateDbContext();
        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct)
            ?? throw new BusinessRuleException("El documento no existe.");
        await ValidateLinksAsync(db, document.ProjectId, siteId, taskItemId, ct);

        document.Title = title;
        document.Kind = kind;
        document.DocumentDate = date;
        document.IsSigned = isSigned;
        document.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        document.SiteId = siteId;
        document.TaskItemId = taskItemId;
        await db.SaveChangesAsync(ct);
    }

    public async Task<(ProjectDocument Document, Stream Content)?> OpenAsync(int documentId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (document is null)
        {
            return null;
        }

        var stream = await storage.OpenReadAsync(document.StoredName, ct);
        return stream is null ? null : (document, stream);
    }

    public async Task RemoveAsync(int documentId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (document is not null)
        {
            db.Documents.Remove(document); // borrado lógico; el archivo se conserva
            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task ValidateLinksAsync(ApplicationDbContext db, int projectId, int? siteId, int? taskId, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct))
        {
            throw new BusinessRuleException("El proyecto no existe.");
        }

        if (siteId is { } s && !await db.Sites.AnyAsync(x => x.Id == s && x.ProjectId == projectId, ct))
        {
            throw new BusinessRuleException("El nodo no pertenece a este proyecto.");
        }

        if (taskId is { } t && !await db.Tasks.AnyAsync(x => x.Id == t && x.ProjectId == projectId, ct))
        {
            throw new BusinessRuleException("La tarea no pertenece a este proyecto.");
        }
    }
}

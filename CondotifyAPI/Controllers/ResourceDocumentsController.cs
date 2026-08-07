using System.Security.Claims;
using CondotifyAPI.Data.Documents;
using CondotifyAPI.Domain.DTO.Documents;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.Enums.Mobile;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.Documents;
using CondotifyAPI.Services.Finance;
using CondotifyAPI.Services.Mobile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}/documents")]
public sealed class ResourceDocumentsController(
    DatabaseContext context,
    IResourceDocumentStore store,
    IBoletoPdfProcessor pdf,
    IPlatformPushNotifier notifier) : ControllerBase
{
    [HttpPost]
    [RequireLicensePermission(LicensePermissionEnum.ManageDocuments)]
    [RequestSizeLimit(3_000_000)]
    public async Task<IActionResult> Upload(Guid licenseId, [FromForm] ResourceDocumentUploadForm form, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(form.Title) || form.Title.Length > 160)
            return BadRequest(new { Result = "InvalidTitle", Errors = "Informe um titulo valido." });
        if (!Enum.TryParse<ResourceDocumentCategoryEnum>(form.Category, out var category))
            return BadRequest(new { Result = "InvalidCategory", Errors = "Categoria invalida." });
        if (form.File is null || form.File.Length == 0)
            return BadRequest(new { Result = "FileRequired", Errors = "Selecione o PDF do documento." });
        if (!string.Equals(form.File.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { Result = "InvalidFileType", Errors = "O arquivo deve ser um PDF." });

        await using var stream = new MemoryStream();
        await form.File.CopyToAsync(stream, cancellationToken);
        var sourceBytes = stream.ToArray();

        try
        {
            pdf.CountPages(sourceBytes);
        }
        catch (Exception)
        {
            return BadRequest(new { Result = "InvalidPdf", Errors = "Nao foi possivel ler o PDF enviado." });
        }

        string reference;
        try
        {
            reference = await store.StoreAsync(licenseId, sourceBytes, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { Result = "FileTooLarge", Errors = "O documento deve ter no maximo 2 MB." });
        }

        var actorId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedActor) ? parsedActor : Guid.Empty;
        var actorName = User.FindFirstValue("name") ?? User.Identity?.Name ?? "Administracao";
        var now = DateTime.UtcNow;

        var document = new ResourceDocumentDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = licenseId,
            Category = category,
            Title = form.Title.Trim(),
            Description = (form.Description ?? string.Empty).Trim(),
            StorageReference = reference,
            UploadedByUserId = actorId,
            UploadedByName = actorName,
            PublishedAt = now,
            CreatedAt = now
        };
        context.ResourceDocuments.Add(document);
        await context.SaveChangesAsync(cancellationToken);

        var links = await context.ResidentUnitLinks.AsNoTracking()
            .Where(x => x.Unit.Block.LicenseId == licenseId)
            .ToListAsync(cancellationToken);
        foreach (var residentId in ResolveLicenseNotificationTargets(links, now))
        {
            await notifier.NotifyResidentAsync(
                residentId,
                MobileNotificationCategory.Operational,
                "Novo documento disponivel",
                $"Novo documento disponivel: {document.Title}.",
                "/documentos",
                $"document-published:{document.Id:N}",
                cancellationToken);
        }

        return Ok(ToOut(document));
    }

    [HttpGet]
    [RequireLicensePermission(LicensePermissionEnum.ManageDocuments)]
    public async Task<IActionResult> List(Guid licenseId, CancellationToken cancellationToken)
    {
        var documents = await context.ResourceDocuments.AsNoTracking()
            .Where(x => x.LicenseId == licenseId)
            .OrderByDescending(x => x.PublishedAt)
            .ToListAsync(cancellationToken);

        return Ok(documents.Select(ToOut).ToList());
    }

    [HttpGet("{documentId:guid}/file")]
    [RequireLicensePermission(LicensePermissionEnum.ManageDocuments)]
    public async Task<IActionResult> GetFile(Guid licenseId, Guid documentId, CancellationToken cancellationToken)
    {
        var document = await context.ResourceDocuments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == documentId && x.LicenseId == licenseId, cancellationToken);
        if (document is null) return NotFound();

        var bytes = await store.ReadAsync(licenseId, document.StorageReference, cancellationToken);
        return bytes is null ? NotFound() : File(bytes, "application/pdf");
    }

    [HttpDelete("{documentId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageDocuments)]
    public async Task<IActionResult> Delete(Guid licenseId, Guid documentId, CancellationToken cancellationToken)
    {
        var document = await context.ResourceDocuments
            .FirstOrDefaultAsync(x => x.Id == documentId && x.LicenseId == licenseId, cancellationToken);
        if (document is null) return NotFound();

        await store.DeleteAsync(licenseId, document.StorageReference, cancellationToken);
        context.ResourceDocuments.Remove(document);
        await context.SaveChangesAsync(cancellationToken);
        return Ok(new { Result = "Deleted" });
    }

    private static ResourceDocumentOut ToOut(ResourceDocumentDTO document) => new()
    {
        Id = document.Id,
        Category = document.Category.ToString(),
        Title = document.Title,
        Description = document.Description,
        UploadedByName = document.UploadedByName,
        PublishedAt = document.PublishedAt
    };

    /// <summary>
    /// Quem deve ser notificado sobre um documento recem-publicado: um morador
    /// por vinculo VIGENTE (mesma regra de
    /// <see cref="ResidentAuthorizationService.LinkIsCurrentlyValid"/>) em
    /// qualquer unidade desta licenca, deduplicado (o mesmo morador com vinculo
    /// em duas unidades da mesma licenca aparece uma unica vez). Puro e recebendo
    /// <paramref name="now"/> explicitamente para ser testavel sem banco.
    /// </summary>
    internal static IReadOnlyCollection<Guid> ResolveLicenseNotificationTargets(
        IEnumerable<ResidentUnitLinkDTO> links,
        DateTime now) =>
        links
            .Where(link => ResidentAuthorizationService.LinkIsCurrentlyValid(link, now))
            .Select(link => link.ResidentId)
            .Distinct()
            .ToList();
}

using CondotifyAPI.Data.Documents;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize(Policy = "Resident")]
[Route("api/resident/documents")]
public sealed class ResidentResourceDocumentsController(
    DatabaseContext context,
    IResidentAuthorizationService authorization,
    IResourceDocumentStore store) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var grant = await authorization.GetGrantAsync(User, cancellationToken);
        if (grant is null) return Forbid();

        var documents = await context.ResourceDocuments.AsNoTracking()
            .Where(x => x.LicenseId == grant.LicenseId)
            .OrderByDescending(x => x.PublishedAt)
            .Select(x => new ResidentResourceDocumentOut
            {
                Id = x.Id,
                Category = x.Category.ToString(),
                Title = x.Title,
                Description = x.Description,
                PublishedAt = x.PublishedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(documents);
    }

    [HttpGet("{documentId:guid}/file")]
    public async Task<IActionResult> Download(Guid documentId, CancellationToken cancellationToken)
    {
        var grant = await authorization.GetGrantAsync(User, cancellationToken);
        if (grant is null) return Forbid();

        var document = await context.ResourceDocuments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == documentId && x.LicenseId == grant.LicenseId, cancellationToken);
        if (document is null) return NotFound();

        var bytes = await store.ReadAsync(grant.LicenseId, document.StorageReference, cancellationToken);
        return bytes is null ? NotFound() : File(bytes, "application/pdf");
    }
}

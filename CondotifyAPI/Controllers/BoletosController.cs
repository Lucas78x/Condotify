using System.Security.Claims;
using CondotifyAPI.Data.Finance;
using CondotifyAPI.Domain.DTO.Finance;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.Finance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}/boletos")]
public sealed class BoletosController(
    DatabaseContext context,
    IBoletoDocumentStore store,
    IBoletoPdfProcessor pdf) : ControllerBase
{
    private const int MaxPages = 600;

    [HttpPost("batches")]
    [RequireLicensePermission(LicensePermissionEnum.ManageFinance)]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> UploadBatch(Guid licenseId, [FromForm] BoletoBatchUploadForm form, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(form.Reference) || form.Reference.Length > 80)
            return BadRequest(new { Result = "InvalidReference", Errors = "Informe uma referencia valida para o lote." });
        if (form.File is null || form.File.Length == 0)
            return BadRequest(new { Result = "FileRequired", Errors = "Selecione o PDF com os boletos." });
        if (!string.Equals(form.File.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { Result = "InvalidFileType", Errors = "O arquivo deve ser um PDF." });

        await using var stream = new MemoryStream();
        await form.File.CopyToAsync(stream, cancellationToken);
        var sourceBytes = stream.ToArray();

        int pageCount;
        try
        {
            pageCount = pdf.CountPages(sourceBytes);
        }
        catch (Exception)
        {
            return BadRequest(new { Result = "InvalidPdf", Errors = "Nao foi possivel ler o PDF enviado." });
        }
        if (pageCount == 0 || pageCount > MaxPages)
            return BadRequest(new { Result = "InvalidPageCount", Errors = $"O PDF deve ter entre 1 e {MaxPages} paginas." });

        var actorId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedActor) ? parsedActor : Guid.Empty;
        var actorName = User.FindFirstValue("name") ?? User.Identity?.Name ?? "Administracao";
        var now = DateTime.UtcNow;

        var batch = new BoletoBatchDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = licenseId,
            Reference = form.Reference.Trim(),
            DueDate = form.DueDate,
            UploadedByUserId = actorId,
            UploadedByName = actorName,
            Status = BoletoBatchStatusEnum.Processing,
            SourceFileName = Path.GetFileName(form.File.FileName),
            TotalPages = pageCount,
            CreatedAt = now
        };
        context.BoletoBatches.Add(batch);

        // Um candidato por morador: se ele tiver mais de um vinculo ativo nesta
        // licenca, usa o vinculo principal (IsPrimary) - nunca gera mais de uma
        // unidade candidata para o mesmo CPF por conta propria.
        var residents = (await context.Residents.AsNoTracking()
            .Where(x => x.CPF != "" && x.UnitLinks.Any(link => link.IsActive && link.Unit.Block.LicenseId == licenseId))
            .Select(x => new
            {
                x.Id,
                x.CPF,
                UnitId = x.UnitLinks
                    .Where(link => link.IsActive && link.Unit.Block.LicenseId == licenseId)
                    .OrderByDescending(link => link.IsPrimary)
                    .Select(link => link.UnitId)
                    .First()
            })
            .ToListAsync(cancellationToken))
            .Select(x => new BoletoPageMatcher.ResidentCandidate(x.Id, x.CPF, x.UnitId))
            .ToList();

        var units = (await context.Units.AsNoTracking()
            .Where(x => x.Block.LicenseId == licenseId)
            .Select(x => new { x.Id, BlockName = x.Block.Name, x.Number })
            .ToListAsync(cancellationToken))
            .Select(x => new BoletoPageMatcher.UnitCandidate(x.Id, x.BlockName, x.Number))
            .ToList();

        for (var pageNumber = 1; pageNumber <= pageCount; pageNumber++)
        {
            var pageText = SafeExtractText(sourceBytes, pageNumber);
            var match = BoletoPageMatcher.Match(pageText, residents, units);
            var pagePdf = pdf.ExtractPageAsPdf(sourceBytes, pageNumber);
            var reference = await store.StoreAsync(licenseId, pagePdf, cancellationToken);

            context.BoletoDocuments.Add(new BoletoDocumentDTO
            {
                Id = Guid.NewGuid(),
                BatchId = batch.Id,
                UnitId = match.UnitId,
                PageNumber = pageNumber,
                MatchMethod = match.Method,
                Ignored = false,
                StorageReference = reference,
                ExtractedSnippet = Snippet(pageText),
                CreatedAt = now
            });
        }

        batch.Status = BoletoBatchStatusEnum.PendingReview;
        await context.SaveChangesAsync(cancellationToken);

        var detail = await LoadDetailAsync(licenseId, batch.Id, cancellationToken);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpGet("batches")]
    [RequireLicensePermission(LicensePermissionEnum.ManageFinance)]
    public async Task<IActionResult> ListBatches(Guid licenseId, CancellationToken cancellationToken)
    {
        var batches = await context.BoletoBatches.AsNoTracking()
            .Include(x => x.Documents)
            .Where(x => x.LicenseId == licenseId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(batches.Select(x => ToBatchOut(x, x.Documents)).ToList());
    }

    [HttpGet("batches/{batchId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageFinance)]
    public async Task<IActionResult> GetBatch(Guid licenseId, Guid batchId, CancellationToken cancellationToken)
    {
        var detail = await LoadDetailAsync(licenseId, batchId, cancellationToken);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpGet("documents/{documentId:guid}/file")]
    [RequireLicensePermission(LicensePermissionEnum.ManageFinance)]
    public async Task<IActionResult> GetDocumentFile(Guid licenseId, Guid documentId, CancellationToken cancellationToken)
    {
        var document = await context.BoletoDocuments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == documentId && x.Batch.LicenseId == licenseId, cancellationToken);
        if (document is null) return NotFound();

        var bytes = await store.ReadAsync(licenseId, document.StorageReference, cancellationToken);
        return bytes is null ? NotFound() : File(bytes, "application/pdf");
    }

    private async Task<BoletoBatchDetailOut?> LoadDetailAsync(Guid licenseId, Guid batchId, CancellationToken cancellationToken)
    {
        var batch = await context.BoletoBatches.AsNoTracking()
            .Include(x => x.Documents).ThenInclude(x => x.Unit).ThenInclude(x => x!.Block)
            .FirstOrDefaultAsync(x => x.Id == batchId && x.LicenseId == licenseId, cancellationToken);
        if (batch is null) return null;

        return new BoletoBatchDetailOut
        {
            Batch = ToBatchOut(batch, batch.Documents),
            Documents = batch.Documents
                .OrderBy(x => x.PageNumber)
                .Select(ToDocumentOut)
                .ToList()
        };
    }

    private static BoletoBatchOut ToBatchOut(BoletoBatchDTO batch, ICollection<BoletoDocumentDTO> documents) => new()
    {
        Id = batch.Id,
        Reference = batch.Reference,
        DueDate = batch.DueDate,
        Status = batch.Status.ToString(),
        TotalPages = batch.TotalPages,
        MatchedCount = documents.Count(x => x.UnitId.HasValue),
        UnmatchedCount = documents.Count(x => !x.UnitId.HasValue && !x.Ignored),
        UploadedByName = batch.UploadedByName,
        CreatedAt = batch.CreatedAt,
        PublishedAt = batch.PublishedAt
    };

    private static BoletoDocumentOut ToDocumentOut(BoletoDocumentDTO document) => new()
    {
        Id = document.Id,
        PageNumber = document.PageNumber,
        UnitId = document.UnitId,
        UnitLabel = document.Unit is null ? string.Empty : $"{document.Unit.Block.Name} / {document.Unit.Number}".Trim(' ', '/'),
        MatchMethod = document.MatchMethod.ToString(),
        Ignored = document.Ignored,
        ExtractedSnippet = document.ExtractedSnippet
    };

    private string SafeExtractText(byte[] sourceBytes, int pageNumber)
    {
        try { return pdf.ExtractPageText(sourceBytes, pageNumber); }
        catch (Exception) { return string.Empty; }
    }

    private static string Snippet(string pageText)
    {
        var collapsed = string.Join(' ', pageText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length > 240 ? collapsed[..240] : collapsed;
    }
}

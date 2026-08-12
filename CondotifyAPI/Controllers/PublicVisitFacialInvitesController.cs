using System.Text.Json;
using CondotifyAPI.Data.Operations;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.Enums.AccessControl;
using CondotifyAPI.Domain.Enums.Invitation;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.AccessControl;
using CondotifyAPI.Services.Extensions;
using CondotifyAPI.Services.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[AllowAnonymous]
[EnableRateLimiting("facial-invite")]
[Route("api/public/visit-facial-invites")]
public sealed class PublicVisitFacialInvitesController(
    DatabaseContext context,
    IVisitFacialInviteService invites,
    IAccessRouteResolver routeResolver,
    IPrivateMediaStore media) : ControllerBase
{
    [HttpGet("{token}")]
    public async Task<IActionResult> Get(string token, CancellationToken cancellationToken)
    {
        var invite = await FindAsync(token, cancellationToken);
        if (invite is null) return NotFound(new { Errors = "Convite facial não encontrado ou inválido." });

        var now = DateTime.UtcNow;
        if (invite.Visit.Status is (AccessVisitStatusEnum.Canceled or AccessVisitStatusEnum.Denied or AccessVisitStatusEnum.CheckedOut or AccessVisitStatusEnum.Expired) &&
            invite.Status is (VisitFacialInviteStatusEnum.Pending or VisitFacialInviteStatusEnum.Opened))
        {
            invite.Status = VisitFacialInviteStatusEnum.Canceled;
            invite.UpdatedAt = now;
        }
        else if (invite.ExpiresAt <= now && invite.Status is VisitFacialInviteStatusEnum.Pending or VisitFacialInviteStatusEnum.Opened)
            Expire(invite, now);
        else if (invite.Status == VisitFacialInviteStatusEnum.Pending)
        {
            invite.Status = VisitFacialInviteStatusEnum.Opened;
            invite.OpenedAt = now;
            invite.UpdatedAt = now;
        }

        await context.SaveChangesAsync(cancellationToken);
        return Ok(await ToPublicOutAsync(invite));
    }

    [HttpPost("{token}/complete")]
    [RequestSizeLimit(1_500_000)]
    public async Task<IActionResult> Complete(
        string token,
        [FromBody] CompleteVisitFacialInviteIn input,
        CancellationToken cancellationToken)
    {
        var invite = await FindAsync(token, cancellationToken);
        if (invite is null) return NotFound(new { Errors = "Convite facial não encontrado ou inválido." });
        if (invite.Status == VisitFacialInviteStatusEnum.Completed)
            return Conflict(new { Errors = "A foto deste convite já foi cadastrada." });
        if (invite.Status == VisitFacialInviteStatusEnum.Canceled)
            return Conflict(new { Errors = "Este convite foi cancelado. Solicite um novo link." });
        if (invite.Visit.Status is AccessVisitStatusEnum.Canceled or AccessVisitStatusEnum.Denied or AccessVisitStatusEnum.CheckedOut or AccessVisitStatusEnum.Expired)
            return Conflict(new { Errors = "A autorização desta visita não está mais ativa." });

        var now = DateTime.UtcNow;
        if (invite.ExpiresAt <= now || invite.Visit.ValidTo <= now)
        {
            Expire(invite, now);
            await context.SaveChangesAsync(cancellationToken);
            return BadRequest(new { Errors = "Este convite expirou. Solicite um novo link ao morador." });
        }
        if (!input.Consent)
            return BadRequest(new { Errors = "Confirme a autorização para uso da foto no controle de acesso." });
        if (invite.UploadAttempts >= 5)
            return StatusCode(StatusCodes.Status429TooManyRequests, new { Errors = "O limite de tentativas foi atingido. Solicite um novo link." });

        var resolution = await routeResolver.ResolveAsync(invite.LicenseId, invite.Visit.GuestResident, AccessCredentialTypeEnum.Face);
        if (resolution.Targets.Count == 0)
            return Conflict(new { Errors = "Nenhum equipamento facial está configurado nas rotas deste convite." });

        invite.UploadAttempts++;
        invite.UpdatedAt = now;
        var imageLimit = resolution.Targets.Any(x => x.Device.Type.IsInIntelbras()) ? 100_000 : 1_000_000;
        var validation = FaceImageValidator.Validate(input.ImageBase64, imageLimit);
        if (!validation.Success)
        {
            if (invite.UploadAttempts >= 5) invite.Status = VisitFacialInviteStatusEnum.Canceled;
            await context.SaveChangesAsync(cancellationToken);
            return BadRequest(new { Errors = validation.Error });
        }

        string photoReference;
        try
        {
            photoReference = await media.StoreDataUriAsync(invite.LicenseId, input.ImageBase64.Trim(), cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            await context.SaveChangesAsync(cancellationToken);
            return BadRequest(new { Errors = exception.Message });
        }

        var guest = invite.Visit.GuestResident;
        var previousPhoto = guest.ImgUrl;
        guest.ImgUrl = photoReference;
        invite.Visit.PhotoUrl = photoReference;
        invite.Visit.Status = AccessVisitStatusEnum.Scheduled;
        invite.Visit.UpdatedAt = now;
        invite.Visit.Credential.IsActive = true;
        invite.Visit.Credential.UpdatedAt = now;
        invite.Status = VisitFacialInviteStatusEnum.Completed;
        invite.CompletedAt = now;
        invite.UpdatedAt = now;

        context.AccessBatchOperations.Add(new AccessBatchOperationDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = invite.LicenseId,
            Operation = "ReconcileCredentials",
            IdempotencyKey = $"visit-facial-invite:{invite.Id:N}",
            Status = AccessBatchStatusEnum.Queued,
            RequestedBy = $"visitor-invite:{invite.Id:N}",
            FilterJson = JsonSerializer.Serialize(new { credentialIds = new[] { invite.Visit.CredentialId } }),
            CreatedAt = now
        });
        context.AccessOperationAudits.Add(new AccessOperationAuditDTO
        {
            Id = Guid.NewGuid(), LicenseId = invite.LicenseId, EntityType = "Visit", EntityId = invite.VisitId,
            Action = "FacialEnrollmentCompleted", Status = "Queued",
            Summary = $"{invite.Visit.VisitorName} concluiu o cadastro facial do convite temporário.",
            DetailsJson = JsonSerializer.Serialize(new { invite.Visit.ValidFrom, invite.Visit.ValidTo, invite.Visit.Credential.MaxUses, validation.Width, validation.Height, resolution.RouteNames }),
            UserName = "Visitante pelo convite", CreatedAt = now
        });
        await context.SaveChangesAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(previousPhoto) && previousPhoto != photoReference)
            await media.DeleteAsync(invite.LicenseId, previousPhoto, cancellationToken);
        return Ok(await ToPublicOutAsync(invite));
    }

    private async Task<CondotifyAPI.Domain.DTO.Invitation.VisitFacialInviteDTO?> FindAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 200) return null;
        var hash = invites.HashToken(token);
        return await context.VisitFacialInvites.IgnoreQueryFilters()
            .Include(x => x.License)
            .Include(x => x.Visit).ThenInclude(x => x.HostResident).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
            .Include(x => x.Visit).ThenInclude(x => x.GuestResident).ThenInclude(x => x.UnitLinks).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
            .Include(x => x.Visit).ThenInclude(x => x.Credential)
            .FirstOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
    }

    private async Task<PublicVisitFacialInviteOut> ToPublicOutAsync(CondotifyAPI.Domain.DTO.Invitation.VisitFacialInviteDTO invite)
    {
        var resolution = await routeResolver.ResolveAsync(invite.LicenseId, invite.Visit.GuestResident, AccessCredentialTypeEnum.Face);
        var routes = resolution.Targets.SelectMany(x => x.Portals)
            .GroupBy(x => new { x.RouteName, x.DaysOfWeekMask, x.StartTime, x.EndTime })
            .Select(x => new PublicVisitRouteOut { Name = x.Key.RouteName, DaysOfWeekMask = x.Key.DaysOfWeekMask, StartTime = x.Key.StartTime, EndTime = x.Key.EndTime })
            .OrderBy(x => x.Name)
            .ToList();
        return new PublicVisitFacialInviteOut
        {
            VisitorName = invite.Visit.VisitorName,
            LicenseName = invite.License.Name,
            BlockName = invite.Visit.HostResident.Unit.Block.Name,
            UnitNumber = invite.Visit.HostResident.Unit.Number,
            Status = invite.Status.ToString(),
            ValidFrom = invite.Visit.ValidFrom,
            ValidTo = invite.Visit.ValidTo,
            ExpiresAt = invite.ExpiresAt,
            MaxUses = invite.Visit.Credential.MaxUses,
            Routes = routes
        };
    }

    private static void Expire(CondotifyAPI.Domain.DTO.Invitation.VisitFacialInviteDTO invite, DateTime now)
    {
        invite.Status = VisitFacialInviteStatusEnum.Expired;
        invite.UpdatedAt = now;
        invite.Visit.Credential.IsActive = false;
        if (invite.Visit.Status == AccessVisitStatusEnum.PendingEnrollment)
            invite.Visit.Status = AccessVisitStatusEnum.Expired;
        invite.Visit.UpdatedAt = now;
    }
}

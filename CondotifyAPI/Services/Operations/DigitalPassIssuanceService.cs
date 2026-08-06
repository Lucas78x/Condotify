using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Operations;
using CondotifyAPI.Domain.Enums.Invitation;
using CondotifyAPI.Infrastructure;
using Condotify.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.Operations;

public enum DigitalPassIssueOutcome { Success, VisitNotFound, VisitNotEligible, MissingCredential }
public sealed record DigitalPassIssueResult(DigitalPassIssueOutcome Outcome, DigitalPassViewModel? Pass, string? Error);

public enum DigitalPassRevokeOutcome { Success, NotFound }
public sealed record DigitalPassRevokeResult(DigitalPassRevokeOutcome Outcome);

public interface IDigitalPassIssuanceService
{
    Task<DigitalPassIssueResult> IssueAsync(Guid licenseId, Guid visitId, string requestHostRoot, Guid? actorUserId, string actorName, CancellationToken cancellationToken);
    Task<DigitalPassRevokeResult> RevokeAsync(Guid licenseId, Guid visitId, Guid? actorUserId, string actorName, CancellationToken cancellationToken);
}

/// <summary>
/// Shared by the staff portal (DigitalPassesController) and the resident
/// mobile app (ResidentProfileController) so a bug fixed here - like the two
/// found while building the first caller - only needs fixing once.
/// </summary>
public sealed class DigitalPassIssuanceService(
    DatabaseContext context,
    IDigitalPassProviderService providers,
    IAppleWalletPassService appleWallet,
    IConfiguration configuration) : IDigitalPassIssuanceService
{
    public async Task<DigitalPassIssueResult> IssueAsync(Guid licenseId, Guid visitId, string requestHostRoot, Guid? actorUserId, string actorName, CancellationToken cancellationToken)
    {
        var visit = await context.AccessVisits
            .Include(x => x.License).Include(x => x.Credential)
            .Include(x => x.HostResident).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
            .FirstOrDefaultAsync(x => x.Id == visitId && x.LicenseId == licenseId, cancellationToken);
        if (visit is null) return new DigitalPassIssueResult(DigitalPassIssueOutcome.VisitNotFound, null, null);
        if (visit.ValidTo <= DateTime.UtcNow || visit.Status is not (AccessVisitStatusEnum.Scheduled or AccessVisitStatusEnum.CheckedIn))
            return new DigitalPassIssueResult(DigitalPassIssueOutcome.VisitNotEligible, null, "A visita nao esta valida para emissao de passe.");
        if (string.IsNullOrWhiteSpace(visit.Credential.Identifier))
            return new DigitalPassIssueResult(DigitalPassIssueOutcome.MissingCredential, null, "A visita ainda nao possui uma credencial de acesso.");

        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var now = DateTime.UtcNow;
        var pass = await context.DigitalPasses.FirstOrDefaultAsync(x => x.VisitId == visitId, cancellationToken);
        if (pass is null)
        {
            pass = new DigitalPassDTO { Id = Guid.NewGuid(), LicenseId = licenseId, VisitId = visitId, CreatedAt = now };
            context.DigitalPasses.Add(pass);
        }
        pass.TokenHash = Hash(token); pass.Status = DigitalPassStatusEnum.Active; pass.IssuedAt = now;
        pass.ExpiresAt = visit.ValidTo; pass.RevokedAt = null; pass.UpdatedAt = now;
        context.AccessOperationAudits.Add(new AccessOperationAuditDTO
        {
            Id = Guid.NewGuid(), LicenseId = licenseId, EntityType = "DigitalPass", EntityId = pass.Id,
            Action = "Issued", Status = "Success", Summary = $"Passe digital emitido para {visit.VisitorName}.",
            DetailsJson = JsonSerializer.Serialize(new { visitId, visit.ValidFrom, visit.ValidTo }),
            UserId = actorUserId, UserName = actorName, CreatedAt = now
        });
        await context.SaveChangesAsync(cancellationToken);
        pass.Visit = visit; pass.License = visit.License;

        var publicUrl = DigitalPassProviderService.ResolvePublicUrl(configuration, requestHostRoot, token);
        var output = providers.Build(pass, token, publicUrl);
        if (appleWallet.IsConfigured)
        {
            output.AppleWalletUrl = $"{requestHostRoot}/api/public/passes/{Uri.EscapeDataString(token)}/apple";
            output.AppleWalletConfigured = true;
        }
        return new DigitalPassIssueResult(DigitalPassIssueOutcome.Success, output, null);
    }

    public async Task<DigitalPassRevokeResult> RevokeAsync(Guid licenseId, Guid visitId, Guid? actorUserId, string actorName, CancellationToken cancellationToken)
    {
        var pass = await context.DigitalPasses.FirstOrDefaultAsync(x => x.VisitId == visitId && x.LicenseId == licenseId, cancellationToken);
        if (pass is null) return new DigitalPassRevokeResult(DigitalPassRevokeOutcome.NotFound);
        pass.Status = DigitalPassStatusEnum.Revoked; pass.RevokedAt = DateTime.UtcNow; pass.UpdatedAt = DateTime.UtcNow;
        pass.TokenHash = Hash($"revoked:{pass.Id:N}:{RandomNumberGenerator.GetHexString(16)}");
        context.AccessOperationAudits.Add(new AccessOperationAuditDTO
        {
            Id = Guid.NewGuid(), LicenseId = licenseId, EntityType = "DigitalPass", EntityId = pass.Id,
            Action = "Revoked", Status = "Success", Summary = "Passe digital revogado.", DetailsJson = "{}",
            UserId = actorUserId, UserName = actorName, CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync(cancellationToken);
        return new DigitalPassRevokeResult(DigitalPassRevokeOutcome.Success);
    }

    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

using System.Security.Cryptography;
using System.Text;
using CondotifyAPI.Domain.DTO.Invitation;
using CondotifyAPI.Domain.Enums.Invitation;
using CondotifyAPI.Infrastructure;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.AccessControl;

public interface IVisitFacialInviteService
{
    Task<IssuedVisitFacialInvite> IssueAsync(Guid licenseId, Guid visitId, string actor, DateTime expiresAt, CancellationToken cancellationToken = default);
    string HashToken(string token);
}

public sealed record IssuedVisitFacialInvite(Guid InviteId, string Url, DateTime ExpiresAt);

public sealed class VisitFacialInviteService(
    DatabaseContext context,
    IConfiguration configuration) : IVisitFacialInviteService
{
    public async Task<IssuedVisitFacialInvite> IssueAsync(
        Guid licenseId,
        Guid visitId,
        string actor,
        DateTime expiresAt,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var invite = await context.VisitFacialInvites.FirstOrDefaultAsync(x => x.VisitId == visitId, cancellationToken);
        if (invite is null)
        {
            invite = new VisitFacialInviteDTO
            {
                Id = Guid.NewGuid(),
                LicenseId = licenseId,
                VisitId = visitId,
                CreatedAt = now
            };
            context.VisitFacialInvites.Add(invite);
        }

        invite.TokenHash = HashToken(token);
        invite.Status = VisitFacialInviteStatusEnum.Pending;
        invite.UploadAttempts = 0;
        invite.CreatedBy = actor;
        invite.ExpiresAt = expiresAt;
        invite.OpenedAt = null;
        invite.CompletedAt = null;
        invite.UpdatedAt = now;
        await context.SaveChangesAsync(cancellationToken);

        var path = $"/convite/facial/{token}";
        var baseUrl = Environment.GetEnvironmentVariable("CONDOTIFY_PUBLIC_PORTAL_URL")
            ?? configuration["PublicPortal:BaseUrl"];
        var url = string.IsNullOrWhiteSpace(baseUrl) ? path : $"{baseUrl.TrimEnd('/')}{path}";
        return new IssuedVisitFacialInvite(invite.Id, url, invite.ExpiresAt);
    }

    public string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim())));
}

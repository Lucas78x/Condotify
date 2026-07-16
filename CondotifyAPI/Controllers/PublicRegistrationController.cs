using System.Security.Cryptography;
using System.Text;
using CondotifyAPI.Data.People;
using CondotifyAPI.Domain.Enums.Invitation;
using CondotifyAPI.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/public/registration-invites")]
public class PublicRegistrationController : ControllerBase
{
    private readonly DatabaseContext _context;

    public PublicRegistrationController(DatabaseContext context) => _context = context;

    [HttpGet("{token}")]
    public async Task<IActionResult> Get(string token)
    {
        var invite = await FindInviteAsync(token);
        if (invite == null) return NotFound(new { Errors = "Convite nao encontrado ou invalido." });

        var now = DateTime.UtcNow;
        if (invite.ExpiresAt <= now && invite.Status is RegistrationInviteStatusEnum.Pending or RegistrationInviteStatusEnum.Opened)
            invite.Status = RegistrationInviteStatusEnum.Expired;
        else if (invite.Status == RegistrationInviteStatusEnum.Pending)
        {
            invite.Status = RegistrationInviteStatusEnum.Opened;
            invite.OpenedAt = now;
        }

        invite.UpdatedAt = now;
        await _context.SaveChangesAsync();
        return Ok(ToPublicOut(invite));
    }

    [HttpPost("{token}/complete")]
    public async Task<IActionResult> Complete(string token, [FromBody] CompleteRegistrationInviteIn input)
    {
        var invite = await FindInviteAsync(token);
        if (invite == null) return NotFound(new { Errors = "Convite nao encontrado ou invalido." });
        if (invite.Status is RegistrationInviteStatusEnum.Completed or RegistrationInviteStatusEnum.Canceled)
            return Conflict(new { Errors = "Este convite ja foi finalizado." });

        var now = DateTime.UtcNow;
        if (invite.ExpiresAt <= now)
        {
            invite.Status = RegistrationInviteStatusEnum.Expired;
            invite.UpdatedAt = now;
            await _context.SaveChangesAsync();
            return BadRequest(new { Errors = "Este convite expirou. Solicite um novo convite a administracao." });
        }
        if (string.IsNullOrWhiteSpace(input.Name)) return BadRequest(new { Errors = "Informe o nome completo." });

        var resident = invite.Resident;
        resident.Name = input.Name.Trim();
        resident.Email = input.Email?.Trim() ?? resident.Email;
        resident.PhoneNumber = input.PhoneNumber?.Trim() ?? resident.PhoneNumber;
        resident.CPF = input.CPF?.Trim() ?? resident.CPF;
        resident.RG = input.RG?.Trim() ?? resident.RG;
        resident.BirthDate = input.BirthDate?.Trim() ?? resident.BirthDate;
        resident.FirstAccess = false;
        invite.Status = RegistrationInviteStatusEnum.Completed;
        invite.CompletedAt = now;
        invite.UpdatedAt = now;
        await _context.SaveChangesAsync();
        return Ok(ToPublicOut(invite));
    }

    private async Task<CondotifyAPI.Domain.DTO.Invitation.RegistrationInviteDTO?> FindInviteAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 200) return null;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim())));
        return await _context.RegistrationInvites
            .Include(x => x.License)
            .Include(x => x.Resident).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
            .FirstOrDefaultAsync(x => x.TokenHash == hash);
    }

    private static PublicRegistrationInviteOut ToPublicOut(CondotifyAPI.Domain.DTO.Invitation.RegistrationInviteDTO invite) => new()
    {
        ResidentName = invite.Resident.Name,
        LicenseName = invite.License.Name,
        BlockName = invite.Resident.Unit.Block.Name,
        UnitNumber = invite.Resident.Unit.Number,
        Email = invite.Resident.Email,
        PhoneNumber = invite.Resident.PhoneNumber,
        Status = invite.Status.ToString(),
        ExpiresAt = invite.ExpiresAt
    };
}

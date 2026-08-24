using System.Security.Cryptography;
using System.Text;
using CondotifyAPI.Data.People;
using CondotifyAPI.Domain.Enums.Invitation;
using CondotifyAPI.Domain.Models.Resident;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/public/registration-invites")]
public class PublicRegistrationController : ControllerBase
{
    private readonly DatabaseContext _context;
    private readonly IPasswordHasher<ResidentAccess> _passwordHasher;

    public PublicRegistrationController(DatabaseContext context, IPasswordHasher<ResidentAccess> passwordHasher)
    {
        _context = context;
        _passwordHasher = passwordHasher;
    }

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
        var requestedEmail = string.IsNullOrWhiteSpace(input.Email) ? resident.Email.Trim() : input.Email.Trim();
        if (!string.IsNullOrWhiteSpace(requestedEmail))
        {
            var normalizedEmail = requestedEmail.ToLowerInvariant();
            var anotherResidentUsesEmail = await _context.Residents.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.Id != resident.Id && x.Email.ToLower() == normalizedEmail);
            if (anotherResidentUsesEmail)
                return Conflict(new { Errors = "Este e-mail já pertence a outra conta de morador. Peça à administração para revisar o cadastro antes de continuar." });
        }

        var passwordResult = ResidentPasswordSetter.Resolve(input.Password, _passwordHasher);
        if (!passwordResult.Succeeded) return BadRequest(new { Errors = passwordResult.Error });

        resident.Name = input.Name.Trim();
        resident.Email = requestedEmail;
        resident.PhoneNumber = input.PhoneNumber?.Trim() ?? resident.PhoneNumber;
        resident.CPF = input.CPF?.Trim() ?? resident.CPF;
        resident.RG = input.RG?.Trim() ?? resident.RG;
        resident.BirthDate = input.BirthDate?.Trim() ?? resident.BirthDate;
        resident.Password = passwordResult.Hash!;
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
        // IgnoreQueryFilters() deliberado: aceitar convite e AllowAnonymous (autenticado
        // pelo token na URL, nao por JWT), sem principal para popular o accessor. Consulta
        // ja restrita a um hash de token especifico -- ver Task 7 do plano de filtro de tenant.
        return await _context.RegistrationInvites.IgnoreQueryFilters()
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

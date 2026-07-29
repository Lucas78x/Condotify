
using CondotifyAPI.Commands.Users;
using System.Text;

namespace DigitalWorldOnline.Management.Api.Data;

public class CreateUserAccessIn
{
    public string EnterpriseId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? CPF { get; set; }
    public string? RG { get; set; }
    public string? BirthDate { get; set; }
    public AccessTypeEnum Type { get; set; }
}

public static class CreateAccountInConverter
{
    public static CreateUserAccessCommand ToCommand(this CreateUserAccessIn user)
    {
        return new CreateUserAccessCommand(
            Guid.TryParse(user.EnterpriseId, out var enterpriseId) ? enterpriseId : Guid.Empty,
            user.Name,
            user.Email,
            user.Password.Base64Decrypt(),
            user.PhoneNumber ?? string.Empty,
            user.CPF ?? string.Empty,
            user.RG ?? string.Empty,
            user.BirthDate ?? string.Empty,
            user.Type);
    }

    public static string Base64Decrypt(this string toDecrypt)
    {
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(toDecrypt)); }
        catch { return toDecrypt; }
    }
}


using CondotifyAPI.Commands.Users;
using System.Text;

namespace DigitalWorldOnline.Management.Api.Data;

public class CreateUserAccessIn
{
    public string Name { get; set; }
    public string Email { get; set; }
    public string Password { get; set; }
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
            user.Name,
            user.Email,
            user.Password.Base64Decrypt(),
            user.PhoneNumber,
            user.RG,
            user.CPF,
            user.BirthDate,
            user.Type);
    }

    public static string Base64Decrypt(this string toDecrypt)
    {
        try { return Encoding.ASCII.GetString(Convert.FromBase64String(toDecrypt)); }
        catch { return toDecrypt; }
    }
}

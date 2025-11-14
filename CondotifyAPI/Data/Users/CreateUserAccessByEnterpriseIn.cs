using CondotifyAPI.Commands.Users;
using System.Text;

namespace DigitalWorldOnline.Management.Api.Data
{
    public class CreateUserAccessByEnterpriseIn
    {
        public string EnterpriseId { get; set; } 
        public string Name { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
        public string? PhoneNumber { get; set; }
        public string? CPF { get; set; }
        public string? RG { get; set; }
        public string? BirthDate { get; set; }
        public AccessTypeEnum Type { get; set; }
    }

    public static class CreateUserAccessByEnterpriseInConverter
    {
        public static CreateUserAccessByEnterpriseCommand ToCommand(this CreateUserAccessByEnterpriseIn user)
        {
            return new CreateUserAccessByEnterpriseCommand(
                Guid.Parse(user.EnterpriseId),                 
                user.Name,
                user.Email,
                user.Password.Base64Decrypt(),
                user.PhoneNumber,
                user.RG,
                user.CPF,
                user.BirthDate,
                user.Type);
        }
    }
}

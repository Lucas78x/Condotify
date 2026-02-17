using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using CondotifyAPI.Domain.Models.Users;

namespace CondotifyAPI.Jwt
{
    public interface IJwtTokenService
    {
        string CreateAccessToken(UserAccess user);
    }

    public sealed class JwtTokenService : IJwtTokenService
    {
        private readonly string _secret;
        private readonly string _issuer;
        private readonly string _audience;

        public JwtTokenService(IConfiguration config)
        {
            _secret = config["JWT:Secret"]!;
            _issuer = config["JWT:Issuer"] ?? "Condotify";
            _audience = config["JWT:Audience"] ?? "Condotify";
        }

        public string CreateAccessToken(UserAccess user)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
                new("name", user.Name ?? "")
                // aqui você pode adicionar roles/permissões se precisar
            };

            var token = new JwtSecurityToken(
                issuer: _issuer,
                audience: _audience,
                claims: claims,
                expires: DateTime.UtcNow.AddHours(8), // 8 horas
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}

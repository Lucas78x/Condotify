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
            _secret = Environment.GetEnvironmentVariable("JWTCondotify_Secret")
                ?? config["JWT:Secret"]
                ?? throw new InvalidOperationException("JWTCondotify_Secret nao definido!");
            _issuer = Environment.GetEnvironmentVariable("JWTCondotify_Issuer")
                ?? config["JWT:Issuer"]
                ?? "Condotify";
            _audience = Environment.GetEnvironmentVariable("JWTCondotify_Audience")
                ?? config["JWT:Audience"]
                ?? "Condotify";
        }

        public string CreateAccessToken(UserAccess user)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new("enterprise_id", user.EnterpriseId.ToString()),
                new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
                new("name", user.Name ?? ""),
                new("access_type", user.AccessType.ToString())
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

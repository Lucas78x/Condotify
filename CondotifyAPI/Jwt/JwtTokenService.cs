using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.Models.Users;

namespace CondotifyAPI.Jwt
{
    public interface IJwtTokenService
    {
        string CreateAccessToken(UserAccess user);

        /// <summary>
        /// Issues a resident access token. Takes <see cref="ResidentAccessDTO"/> (the
        /// projection resident login already loads from the database, rather than the
        /// bare <c>ResidentAccess</c> domain model) plus the licence id the resident is
        /// signing into, because a resident's licence is selected/known at login time and
        /// is not a property of the DTO itself. Deliberately does NOT accept or embed the
        /// resident's unit(s): unit membership can change (or be revoked) between issuance
        /// and expiry, and baking it into the token would let a revoked link keep working
        /// until the token expires. Callers must resolve current units per request instead.
        /// </summary>
        string CreateResidentAccessToken(ResidentAccessDTO resident, Guid licenseId);
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
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new("enterprise_id", user.EnterpriseId.ToString()),
                new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
                new("name", user.Name ?? ""),
                new("access_type", user.AccessType.ToString()),
                new(PrincipalTypes.Claim, PrincipalTypes.User)
            };

            return WriteToken(claims);
        }

        public string CreateResidentAccessToken(ResidentAccessDTO resident, Guid licenseId)
        {
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, resident.Id.ToString()),
                new(ClaimTypes.NameIdentifier, resident.Id.ToString()),
                new(JwtRegisteredClaimNames.Email, resident.Email ?? ""),
                new("license_id", licenseId.ToString()),
                new(PrincipalTypes.Claim, PrincipalTypes.Resident)
            };

            return WriteToken(claims);
        }

        private string WriteToken(List<Claim> claims)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var now = DateTime.UtcNow;
            var token = new JwtSecurityToken(
                issuer: _issuer,
                audience: _audience,
                claims: claims,
                notBefore: now,
                expires: now.AddHours(1), // 1 hora
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}

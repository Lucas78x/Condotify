using CondotifyAPI.Domain.Models.Users;
using Microsoft.AspNetCore.Identity;

namespace CondotifyAPI.Tests
{
    public class UserAccessTests
    {
        private readonly PasswordHasher<UserAccess> _hasher = new();

        [Fact]
        public void Create_ShouldHashPasswordAndSetProperties()
        {
            var enterpriseId = Guid.NewGuid();

            var user = UserAccess.Create(
                "Lucas Bastos",
                "lucas@test.com",
                "Senha123",
                "11999999999",
                "12345678900",
                "1234567",
                "1990-01-01",
                AccessTypeEnum.Admin,
                true,
                DateTime.UtcNow,
                DateTime.UtcNow,
                _hasher,
                enterpriseId);

            Assert.NotEqual(Guid.Empty, user.Id);
            Assert.Equal("Lucas Bastos", user.Name);
            Assert.Equal("lucas@test.com", user.Email);
            Assert.NotEqual("Senha123", user.PasswordHash);
            Assert.Equal(enterpriseId, user.EnterpriseId);
            Assert.True(user.VerifyPassword("Senha123", _hasher));
        }

        [Fact]
        public void Update_ShouldChangePropertiesAndRehashPassword()
        {
            var user = UserAccess.Create(
                "Lucas",
                "lucas@test.com",
                "Senha123",
                "11999999999",
                "12345678900",
                "1234567",
                "1990-01-01",
                AccessTypeEnum.Admin,
                true,
                DateTime.UtcNow,
                DateTime.UtcNow,
                _hasher,
                Guid.NewGuid());

            var oldHash = user.PasswordHash;

            var updated = user.Update(
                "Maria Silva",
                "maria@test.com",
                "NovaSenha456",
                "11888888888",
                "98765432100",
                "7654321",
                "1995-05-05",
                AccessTypeEnum.Viewer,
                false,
                DateTime.UtcNow.AddMinutes(5),
                _hasher);

            Assert.True(updated);
            Assert.Equal("Maria Silva", user.Name);
            Assert.Equal("maria@test.com", user.Email);
            Assert.NotEqual(oldHash, user.PasswordHash);
            Assert.True(user.VerifyPassword("NovaSenha456", _hasher));
            Assert.False(user.FirstAccess);
        }

        [Fact]
        public void Update_WithBlankPassword_ShouldKeepExistingPasswordHash()
        {
            var user = UserAccess.Create(
                "Lucas",
                "lucas@test.com",
                "Senha123",
                "11999999999",
                "12345678900",
                "1234567",
                "1990-01-01",
                AccessTypeEnum.Admin,
                true,
                DateTime.UtcNow,
                DateTime.UtcNow,
                _hasher,
                Guid.NewGuid());

            var oldHash = user.PasswordHash;

            user.Update(
                user.Name,
                user.Email,
                "",
                user.PhoneNumber,
                user.CPF,
                user.RG,
                user.BirthDate,
                user.AccessType,
                user.FirstAccess,
                user.LastAccess,
                _hasher);

            Assert.Equal(oldHash, user.PasswordHash);
            Assert.True(user.VerifyPassword("Senha123", _hasher));
        }
    }
}

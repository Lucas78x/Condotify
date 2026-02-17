using System;
using CondotifyAPI.Domain.Models.Users;
using Xunit;

namespace CondotifyAPI.Tests
{
    public class UserAccessTests
    {
        //[Fact]
        //public void Create_ShouldReturnUserAccessWithCorrectProperties()
        //{
        //    // Arrange
        //    var name = "Lucas Bastos";
        //    var email = "lucas@test.com";
        //    var password = "Senha123";
        //    var phoneNumber = "11999999999";
        //    var cpf = "12345678900";
        //    var rg = "1234567";
        //    var birthDate = "1990-01-01";
        //    var accessType = AccessTypeEnum.Admin;
        //    var firstAccess = true;
        //    var lastAccess = DateTime.UtcNow;
        //    var createdAt = DateTime.UtcNow;

        //    // Act
        //    var user = UserAccess.Create(
        //        name, email, password, phoneNumber, cpf, rg, birthDate,
        //        accessType, firstAccess, lastAccess, createdAt
        //    );

        //    // Assert
        //    Assert.NotNull(user);
        //    Assert.NotEqual(Guid.Empty, user.Id);
        //    Assert.Equal(name, user.Name);
        //    Assert.Equal(email, user.Email);
        //    Assert.Equal(password, user.Password);
        //    Assert.Equal(phoneNumber, user.PhoneNumber);
        //    Assert.Equal(cpf, user.CPF);
        //    Assert.Equal(rg, user.RG);
        //    Assert.Equal(birthDate, user.BirthDate);
        //    Assert.Equal(accessType, user.AccessType);
        //    Assert.Equal(firstAccess, user.FirstAccess);
        //    Assert.Equal(lastAccess, user.LastAccess);
        //    Assert.Equal(createdAt, user.CreatedAt);
        //}

        //[Fact]
        //public void Properties_ShouldBeSettableDirectly()
        //{
        //    // Arrange
        //    var user = new UserAccess
        //    {
        //        Id = Guid.NewGuid(),
        //        Name = "Maria",
        //        Email = "maria@test.com",
        //        Password = "12345",
        //        PhoneNumber = "11888888888",
        //        CPF = "98765432100",
        //        RG = "7654321",
        //        BirthDate = "1995-05-05",
        //        AccessType = AccessTypeEnum.Admin,
        //        FirstAccess = false,
        //        LastAccess = DateTime.UtcNow,
        //        CreatedAt = DateTime.UtcNow
        //    };

        //    // Assert
        //    Assert.Equal("Maria", user.Name);
        //    Assert.Equal("maria@test.com", user.Email);
        //    Assert.Equal("12345", user.Password);
        //    Assert.Equal("11888888888", user.PhoneNumber);
        //    Assert.Equal("98765432100", user.CPF);
        //    Assert.Equal("7654321", user.RG);
        //    Assert.Equal("1995-05-05", user.BirthDate);
        //    Assert.Equal(AccessTypeEnum.Admin, user.AccessType);
        //    Assert.False(user.FirstAccess);
        //}

        //[Fact]
        //public void Update_ShouldChangePropertiesCorrectly()
        //{
        //    // Arrange
        //    var user = UserAccess.Create(
        //        "Lucas", "lucas@test.com", "Senha123", "11999999999", "12345678900",
        //        "1234567", "1990-01-01", AccessTypeEnum.Admin, true, DateTime.UtcNow, DateTime.UtcNow
        //    );

        //    var newName = "Maria Silva";
        //    var newEmail = "maria@test.com";
        //    var newPassword = "NovaSenha456";
        //    var newPhone = "11888888888";
        //    var newCpf = "98765432100";
        //    var newRg = "7654321";
        //    var newBirthDate = "1995-05-05";
        //    var newAccessType = AccessTypeEnum.Viewer;
        //    var newFirstAccess = false;
        //    var newLastAccess = DateTime.UtcNow.AddMinutes(5);

        //    // Act
        //    var updated = user.Update(
        //        newName, newEmail, newPassword, newPhone, newCpf, newRg,
        //        newBirthDate, newAccessType, newFirstAccess, newLastAccess
        //    );

        //    // Assert
        //    Assert.True(updated);
        //    Assert.Equal(newName, user.Name);
        //    Assert.Equal(newEmail, user.Email);
        //    Assert.Equal(newPassword, user.Password);
        //    Assert.Equal(newPhone, user.PhoneNumber);
        //    Assert.Equal(newCpf, user.CPF);
        //    Assert.Equal(newRg, user.RG);
        //    Assert.Equal(newBirthDate, user.BirthDate);
        //    Assert.Equal(newAccessType, user.AccessType);
        //    Assert.Equal(newFirstAccess, user.FirstAccess);
        //    Assert.Equal(newLastAccess, user.LastAccess);
        //}

        //[Fact]
        //public void Create_ShouldGenerateUniqueIdForEachUser()
        //{
        //    // Act
        //    var user1 = UserAccess.Create("A", "a@test.com", "123", "111111111", "123", "1", "2000-01-01", AccessTypeEnum.Viewer, true, DateTime.UtcNow, DateTime.UtcNow);
        //    var user2 = UserAccess.Create("B", "b@test.com", "456", "222222222", "456", "2", "2001-01-01", AccessTypeEnum.Admin, false, DateTime.UtcNow, DateTime.UtcNow);

        //    // Assert
        //    Assert.NotEqual(user1.Id, user2.Id);
        //}
    }
}

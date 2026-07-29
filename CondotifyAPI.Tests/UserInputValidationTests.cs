using CondotifyAPI.Commands.Users;

namespace CondotifyAPI.Tests;

public sealed class UserInputValidationTests
{
    [Fact]
    public async Task UserValidator_ShouldRejectMissingEmailAndNameBeforePersistence()
    {
        var command = new CreateUserAccessCommand(
            Guid.NewGuid(),
            string.Empty,
            string.Empty,
            "secret123",
            string.Empty,
            "12345678901",
            "11223344",
            string.Empty,
            AccessTypeEnum.Default);

        var result = await new CreateUserAccessCommandValidator().ValidateAsync(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.PropertyName == nameof(command.Name));
        Assert.Contains(result.Errors, x => x.PropertyName == nameof(command.Email));
    }

    [Fact]
    public async Task UserValidator_ShouldRejectEmptyEnterprise()
    {
        var command = new CreateUserAccessCommand(
            Guid.Empty,
            "Test",
            "test@example.com",
            "secret123",
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            AccessTypeEnum.Default);

        var result = await new CreateUserAccessCommandValidator().ValidateAsync(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.PropertyName == nameof(command.EnterpriseId));
    }

    [Fact]
    public async Task EnterpriseUserValidator_ShouldRejectEmptyEnterprise()
    {
        var command = new CreateUserAccessByEnterpriseCommand(
            Guid.Empty,
            "Test",
            "test@example.com",
            "secret123",
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            AccessTypeEnum.Default);

        var result = await new CreateUserAccessByEnterpriseCommandValidator().ValidateAsync(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.PropertyName == nameof(command.EnterpriseId));
    }
}

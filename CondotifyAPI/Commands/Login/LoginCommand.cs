using CondotifyAPI.Domain.Enums.Login;
using MediatR;

namespace CondotifyAPI.Commands.Login
{
    public sealed record LoginCommand(string Email, string Password) : IRequest<LoginCommandResponse>;

    public sealed class LoginCommandResponse
    {
        public LoginResult Result { get; init; }
        public string? AccessToken { get; init; }
        public long? ExpiresInSeconds { get; init; }

        public static LoginCommandResponse Invalid() => new() { Result = LoginResult.InvalidCredentials };
        public static LoginCommandResponse Ok(string token, long expiresIn) => new()
        {
            Result = LoginResult.Success,
            AccessToken = token,
            ExpiresInSeconds = expiresIn
        };

    }
}

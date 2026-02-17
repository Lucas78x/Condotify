using CondotifyAPI.Commands.Login;
using CondotifyAPI.Domain.Interfaces;
using CondotifyAPI.Domain.Models.Users;
using CondotifyAPI.Jwt;
using MediatR;
using Microsoft.AspNetCore.Identity;

public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, LoginCommandResponse>
{
    private readonly ICondotifyQueriesRepository _repository;
    private readonly IJwtTokenService _jwt;
    private readonly IPasswordHasher<UserAccess> _passwordHasher;

    public LoginCommandHandler(
        ICondotifyQueriesRepository repository,
        IJwtTokenService jwt,
        IPasswordHasher<UserAccess> passwordHasher)
    {
        _repository = repository;
        _jwt = jwt;
        _passwordHasher = passwordHasher;
    }

    public async Task<LoginCommandResponse> Handle(LoginCommand request, CancellationToken ct)
    {
        var email = (request.Email ?? "").Trim();
        var password = request.Password ?? "";

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return LoginCommandResponse.Invalid();

        var user = await _repository.FindByEmailAsync(email, ct);

        if (user is null)
            return LoginCommandResponse.Invalid();

        if (!user.VerifyPassword(password, _passwordHasher))
            return LoginCommandResponse.Invalid();

        var token = _jwt.CreateAccessToken(user);
        return LoginCommandResponse.Ok(token, expiresIn: 60 * 60 * 8); // 8 horas
    }
}

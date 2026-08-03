using CondotifyAPI.Domain.Models.Resident;
using Microsoft.AspNetCore.Identity;

namespace CondotifyAPI.Services.Security;

/// <summary>
/// Decide se a senha informada por um morador pode ser aceita e, quando pode, produz o
/// hash a ser gravado em <c>ResidentAccessDTO.Password</c>. Extraida do controller de
/// convite (<c>PublicRegistrationController</c>) para que a decisao "validar depois fazer
/// hash" seja testavel sem um <c>DbContext</c> — este projeto de testes nao tem provider
/// InMemory/SQLite.
/// </summary>
public static class ResidentPasswordSetter
{
    /// <summary>
    /// Valida a senha com <see cref="PasswordPolicy"/> e, se valida, devolve o hash gerado
    /// por <paramref name="hasher"/>. A senha em si nunca aparece no resultado.
    /// </summary>
    /// <param name="subject">
    /// Repassado a <see cref="PasswordPolicy.Validate"/> - "senha" (padrao) para o primeiro
    /// acesso do morador (convite de cadastro); "nova senha" para os fluxos de troca/recuperacao
    /// de senha (task 8), onde ja existe uma senha anterior sendo substituida.
    /// </param>
    public static ResidentPasswordResult Resolve(string? password, IPasswordHasher<ResidentAccess> hasher, string subject = "senha")
    {
        var error = PasswordPolicy.Validate(password, subject);
        if (error is not null)
            return ResidentPasswordResult.Failure(error);

        // O PasswordHasher<TUser> padrao do ASP.NET Core Identity nao le a instancia de
        // TUser (mesmo padrao ja usado em LicenseAdministrationController e
        // DevelopmentDataSeeder); null! e seguro aqui.
        var hash = hasher.HashPassword(null!, password!);
        return ResidentPasswordResult.Success(hash);
    }
}

/// <summary>Resultado de <see cref="ResidentPasswordSetter.Resolve"/>: ou um erro, ou um hash — nunca os dois.</summary>
public sealed class ResidentPasswordResult
{
    public string? Error { get; }
    public string? Hash { get; }
    public bool Succeeded => Error is null;

    private ResidentPasswordResult(string? error, string? hash)
    {
        Error = error;
        Hash = hash;
    }

    public static ResidentPasswordResult Success(string hash) => new(null, hash);
    public static ResidentPasswordResult Failure(string error) => new(error, null);
}

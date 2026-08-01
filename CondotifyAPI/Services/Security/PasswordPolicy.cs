namespace CondotifyAPI.Services.Security;

/// <summary>
/// Regra unica de senha da plataforma. Extraida de AuthController para que
/// equipe e morador nao acabem com politicas divergentes.
/// </summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 8;
    public const int MaximumLength = 100;

    /// <summary>Devolve a mensagem de erro, ou null quando a senha e valida.</summary>
    public static string? Validate(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinimumLength || password.Length > MaximumLength)
            return $"A nova senha deve ter entre {MinimumLength} e {MaximumLength} caracteres.";

        if (!password.Any(char.IsUpper) || !password.Any(char.IsLower) ||
            !password.Any(char.IsDigit) || !password.Any(x => !char.IsLetterOrDigit(x)))
            return "Use letras maiúsculas e minúsculas, número e caractere especial.";

        return null;
    }
}

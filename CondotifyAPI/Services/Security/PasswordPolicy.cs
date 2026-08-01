namespace CondotifyAPI.Services.Security;

/// <summary>
/// Regra unica de senha da plataforma. Extraida de AuthController para que
/// equipe e morador nao acabem com politicas divergentes.
/// </summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 8;
    public const int MaximumLength = 100;

    /// <summary>
    /// Devolve a mensagem de erro, ou null quando a senha e valida.
    /// </summary>
    /// <param name="password">Senha a validar.</param>
    /// <param name="subject">
    /// Como a mensagem de tamanho se refere a senha. O padrao ("nova senha") preserva a
    /// mensagem historica de <c>AuthController</c>, onde o usuario esta trocando uma senha
    /// existente. Fluxos onde e a primeira senha do usuario (ex.: convite de morador) devem
    /// passar "senha", para nao dizer "nova" quando nao ha senha anterior.
    /// </param>
    public static string? Validate(string? password, string subject = "nova senha")
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinimumLength || password.Length > MaximumLength)
            return $"A {subject} deve ter entre {MinimumLength} e {MaximumLength} caracteres.";

        if (!password.Any(char.IsUpper) || !password.Any(char.IsLower) ||
            !password.Any(char.IsDigit) || !password.Any(x => !char.IsLetterOrDigit(x)))
            return "Use letras maiúsculas e minúsculas, número e caractere especial.";

        return null;
    }
}

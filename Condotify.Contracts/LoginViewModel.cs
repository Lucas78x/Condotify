using System.ComponentModel.DataAnnotations;

namespace Condotify.Models
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "O email é obrigatório.")]
        [EmailAddress(ErrorMessage = "Digite um email válido.")]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "A senha é obrigatória.")]
        [DataType(DataType.Password)]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "A senha deve ter entre 6 e 100 caracteres.")]
        [Display(Name = "Senha")]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }

        public bool MfaRequired { get; set; }
        public string MfaChallengeToken { get; set; } = string.Empty;

        [Display(Name = "Codigo de seguranca")]
        [StringLength(20, ErrorMessage = "Digite um codigo valido.")]
        public string MfaCode { get; set; } = string.Empty;
    }
}

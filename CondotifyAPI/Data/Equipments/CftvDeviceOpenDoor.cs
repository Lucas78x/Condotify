using FluentValidation;

namespace CondotifyAPI.Data.Equipments
{
    public class CftvDeviceOpenDoor
    {
        /// <summary>
        /// Identificador do dispositivo CFTV
        /// </summary>
        public Guid DeviceId { get; set; }

        /// <summary>
        /// Usuário / sistema que solicitou a abertura
        /// </summary>
        public Guid OpenBy { get; set; }
    }
    public class CftvDeviceOpenDoorValidator : AbstractValidator<CftvDeviceOpenDoor>
    {
        public CftvDeviceOpenDoorValidator()
        {
            RuleFor(x => x.DeviceId)
                .NotEmpty()
                .WithMessage("O DeviceId é obrigatório.");

            RuleFor(x => x.OpenBy)
                .NotEmpty()
                .WithMessage("O identificador de quem solicitou a abertura é obrigatório.");
        }
    }
}

using FluentValidation;
using System.Net;

namespace CondotifyAPI.Data.Equipments
{
    public class TestCftvConnectionIn
    {
        public string IpAddress { get; set; } = "";
        public string UserName { get; set; } = "";
        public string Password { get; set; } = "";

        public string? HTTPPort { get; set; }
        public string? RTSPPort { get; set; }

        public IpTypeEnum IpType { get; set; }
        public MarkEnum Mark { get; set; }

        public CFTVDeviceTypeEnum DeviceType { get; set; }

        public ICollection<int> Channels { get; set; }
    }
    public class TestCftvConnectionInValidator : AbstractValidator<TestCftvConnectionIn>
    {
        public TestCftvConnectionInValidator()
        {
            RuleFor(x => x.IpAddress)
                .NotEmpty()
                .Must(BeAValidIp)
                .WithMessage("IP inválido.");

            RuleFor(x => x.UserName)
                .NotEmpty()
                .MaximumLength(50);

            RuleFor(x => x.Password)
                .NotEmpty()
                .MaximumLength(50);

            RuleFor(x => x.HTTPPort)
                .Must(BeAValidPort)
                .When(x => !string.IsNullOrWhiteSpace(x.HTTPPort))
                .WithMessage("Porta HTTP inválida.");

            RuleFor(x => x.RTSPPort)
                .Must(BeAValidPort)
                .When(x => !string.IsNullOrWhiteSpace(x.RTSPPort))
                .WithMessage("Porta RTSP inválida.");

            RuleFor(x => x.DeviceType)
                .IsInEnum()
                .WithMessage("Tipo de dispositivo inválido.");

            RuleFor(x => x.Mark)
                .IsInEnum()
                .WithMessage("Marca do dispositivo inválida.");

            When(x => x.DeviceType != CFTVDeviceTypeEnum.Camera, () =>
            {
                RuleFor(x => x.Channels)
                    .NotNull()
                    .NotEmpty()
                    .WithMessage("Informe ao menos um canal para DVR/NVR.");

                RuleForEach(x => x.Channels)
                    .GreaterThan(0)
                    .WithMessage("O número do canal deve ser maior que zero.");
            });
        }

        private bool BeAValidIp(string ip)
        {
            return IPAddress.TryParse(ip, out _);
        }

        private bool BeAValidPort(string port)
        {
            return int.TryParse(port, out var p) && p > 0 && p <= 65535;
        }
    }
}

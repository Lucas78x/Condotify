using CondotifyAPI.Domain.Interfaces;
using CondotifyAPI.Domain.Models.Equipments;
using FluentValidation;
using MediatR;
using System.Net;

namespace CondotifyAPI.Commands.Equipments
{
    public class CreateCftvDeviceByLicenseCommand : IRequest<CFTVDevice?>
    {
        public Guid LicenseId { get; set; }

        public string Name { get; set; }
        public string IpAddress { get; set; } = "";
        public string UserName { get; set; } = "";
        public string Password { get; set; } = "";

        public string? HTTPPort { get; set; }
        public string? RTSPPort { get; set; }

        public IpTypeEnum IpType { get; set; }
        public ScreenProportionEnum Propotion { get; set; }
        public MarkEnum Mark { get; set; }

        public CFTVDeviceTypeEnum DeviceType { get; set; }

        public int MaxChannels;

        public ICollection<CFTVChannel> Channels { get; set; }

        public CreateCftvDeviceByLicenseCommand(
            Guid licenseId,
            string name,
            string ipAddress,
            string userName,
            string password,
            string port,
            string rtspPort,
            IpTypeEnum ipType,
            ScreenProportionEnum propotion,
            MarkEnum mark,

            CFTVDeviceTypeEnum deviceType,
            int maxChannels,
            ICollection<CFTVChannel> channels)
        {
            LicenseId = licenseId;
            Name = name;
            IpAddress = ipAddress;
            UserName = userName;
            Password = password;
            HTTPPort = port;
            RTSPPort = rtspPort;
            IpType = ipType;
            Propotion = propotion;
            Mark = mark;
            DeviceType = deviceType;
            MaxChannels = maxChannels;
            Channels = channels;
        }

        internal class Handler : IRequestHandler<CreateCftvDeviceByLicenseCommand, CFTVDevice?>
        {
            private readonly ICondotifyCommandsRepository _repository;

            public Handler(ICondotifyCommandsRepository repository)
            {
                _repository = repository;
            }

            public async Task<CFTVDevice?> Handle(CreateCftvDeviceByLicenseCommand request, CancellationToken cancellationToken)
            {
                var device = CFTVDevice.Create(
                    request.Name,
                    request.UserName,
                    request.Password,
                    request.IpAddress,
                    request.HTTPPort,
                    request.RTSPPort,
                    request.IpType,
                    request.Propotion,
                    request.Mark,
                    request.DeviceType,
                    request.MaxChannels,
                    request.Channels

                );

                return await _repository.AddCftvDeviceAsync(request.LicenseId, device);
            }
        }
    }

    public class CreateCftvDeviceByLicenseCommandValidator : AbstractValidator<CreateCftvDeviceByLicenseCommand>
    {
        public CreateCftvDeviceByLicenseCommandValidator()
        {
            RuleFor(x => x.LicenseId)
                .NotEmpty().WithMessage("LicenseId é obrigatório.");

            RuleFor(x => x.Name)
                .NotEmpty().MaximumLength(200);


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
                    .ChildRules(channel =>
                    {
                        channel.RuleFor(c => c.ChannelNumber)
                            .GreaterThan(0)
                            .WithMessage("O número do canal deve ser maior que zero.");

                        channel.RuleFor(c => c.Name)
                            .NotEmpty()
                            .WithMessage("O nome do canal é obrigatório.");
                    });
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


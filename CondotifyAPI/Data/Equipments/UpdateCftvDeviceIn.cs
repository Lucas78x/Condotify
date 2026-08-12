using System.Net;
using CondotifyAPI.Domain.Models.Equipments;
using FluentValidation;

namespace CondotifyAPI.Data.Equipments;

public sealed class UpdateCftvDeviceIn
{
    public string Name { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string? Password { get; set; }
    public string HTTPPort { get; set; } = "80";
    public string RTSPPort { get; set; } = "554";
    public IpTypeEnum IpType { get; set; }
    public ScreenProportionEnum Proportion { get; set; }
    public MarkEnum Mark { get; set; }
    public CFTVDeviceTypeEnum DeviceType { get; set; } = CFTVDeviceTypeEnum.Camera;
    public int MaxChannels { get; set; } = 1;
    public bool ResidentVisible { get; set; }
}

public sealed class UpdateCftvDeviceInValidator : AbstractValidator<UpdateCftvDeviceIn>
{
    public UpdateCftvDeviceInValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Informe o nome da câmera ou do gravador.")
            .MaximumLength(200);

        RuleFor(x => x.IpAddress)
            .NotEmpty()
            .Must(value => IPAddress.TryParse(value, out _))
            .WithMessage("Informe um endereço IP válido.");

        RuleFor(x => x.UserName)
            .NotEmpty().WithMessage("Informe o usuário do equipamento.")
            .MaximumLength(50);

        RuleFor(x => x.Password)
            .MaximumLength(50)
            .When(x => !string.IsNullOrWhiteSpace(x.Password));

        RuleFor(x => x.HTTPPort)
            .Must(BeAValidPort)
            .WithMessage("Informe uma porta HTTP válida.");

        RuleFor(x => x.RTSPPort)
            .Must(BeAValidPort)
            .WithMessage("Informe uma porta RTSP válida.");

        RuleFor(x => x.Mark).IsInEnum().WithMessage("A marca informada é inválida.");
        RuleFor(x => x.DeviceType).IsInEnum().WithMessage("O tipo informado é inválido.");
        RuleFor(x => x.MaxChannels).InclusiveBetween(1, 128)
            .WithMessage("Informe entre 1 e 128 canais.");
    }

    private static bool BeAValidPort(string? value) =>
        int.TryParse(value, out var port) && port is > 0 and <= 65535;
}

public sealed record CftvConnectionDiagnosticOut(
    bool PingOk,
    bool TcpRtspOk,
    bool RtspReady,
    int ChannelsReady,
    string Message);

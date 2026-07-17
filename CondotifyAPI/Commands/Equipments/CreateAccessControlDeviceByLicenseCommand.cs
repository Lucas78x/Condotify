using CondotifyAPI.Domain.Interfaces;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Domain.Models;
using FluentValidation;
using MediatR;

namespace CondotifyAPI.Commands.Equipments
{
    public class CreateAccessControlDeviceByLicenseCommand : IRequest<AccessControlDevice?>
    {
        public Guid LicenseId { get; set; }
        public string Name { get; set; }
        public string IPAddress { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string? MACAddress { get; set; }
        public string Model { get; set; }
        public string? SerialNumber { get; set; }
        public string? FirmwareVersion { get; set; }
        public DeviceTypeEnum Type { get; set; }
        public bool IsActive { get; set; }
        public Location Location { get; set; }

        public CreateAccessControlDeviceByLicenseCommand(
            Guid licenseId,
            string name,
            string ipAddress,
            int port,
            string username,
            string password,
            string? macAddress,
            string model,
            string? serialNumber,
            string? firmwareVersion,
            DeviceTypeEnum type,
            bool isActive,
            Location location)
        {
            LicenseId = licenseId;
            Name = name;
            IPAddress = ipAddress;
            Port = port;
            Username = username;
            Password = password;
            MACAddress = macAddress;
            Model = model;
            SerialNumber = serialNumber;
            FirmwareVersion = firmwareVersion;
            Type = type;
            IsActive = isActive;
            Location = location;
        }

        internal class Handler : IRequestHandler<CreateAccessControlDeviceByLicenseCommand, AccessControlDevice?>
        {
            private readonly ICondotifyCommandsRepository _repository;

            public Handler(ICondotifyCommandsRepository repository)
            {
                _repository = repository;
            }

            public async Task<AccessControlDevice?> Handle(CreateAccessControlDeviceByLicenseCommand request, CancellationToken cancellationToken)
            {
                var now = DateTime.UtcNow;
                var device = AccessControlDevice.Create(
                    request.Name,
                    request.IPAddress,
                    request.Port,
                    request.Username,
                    request.Password,
                    request.MACAddress,
                    request.Model,
                    request.SerialNumber,
                    request.FirmwareVersion,
                    request.Type,
                    request.IsActive,
                    request.Location,
                    now,
                    now
                );

                return await _repository.AddAccessControlDeviceAsync(request.LicenseId, device);
            }
        }
    }

    public class CreateAccessControlDeviceByLicenseCommandValidator : AbstractValidator<CreateAccessControlDeviceByLicenseCommand>
    {
        public CreateAccessControlDeviceByLicenseCommandValidator()
        {
            RuleFor(x => x.LicenseId)
                .NotEmpty().WithMessage("LicenseId é obrigatório.");

            RuleFor(x => x.Name)
                .NotEmpty().MaximumLength(200);

            RuleFor(x => x.IPAddress)
                .NotEmpty().WithMessage("O IP Address é obrigatório.");

            RuleFor(x => x.Port)
                .GreaterThan(0).WithMessage("Porta inválida.");

            RuleFor(x => x.Username)
                .NotEmpty();

            RuleFor(x => x.Password)
                .NotEmpty();

            RuleFor(x => x.Type)
                .IsInEnum();

            RuleFor(x => x.Location)
                .NotNull().WithMessage("Location é obrigatório.");
        }
    }
}

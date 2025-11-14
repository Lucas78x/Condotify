using CondotifyAPI.Domain.Interfaces;
using CondotifyAPI.Domain.Models.Users;
using FluentValidation;
using MediatR;

namespace CondotifyAPI.Commands.Users;

public class CreateUserAccessCommand : IRequest<UserAccessCreateResult>
{
    public string Name { get; set; }
    public string Email { get; set; }
    public string Password { get; set; }
    public string PhoneNumber { get; set; }
    public string CPF { get; set; }
    public string RG { get; set; }
    public string BirthDate { get; set; }
    public AccessTypeEnum Type { get; set; }

    public CreateUserAccessCommand(
        string name,
        string email,
        string password,
        string phoneNumber,
        string cpf,
        string rg,
        string birthDate,
        AccessTypeEnum type)
    {
        Name = name;
        Password = password;
        Email = email;
        PhoneNumber = phoneNumber;
        CPF = cpf;
        RG = rg;
        BirthDate = birthDate;
        Type = type;
    }

    internal class Handler : IRequestHandler<CreateUserAccessCommand,  UserAccessCreateResult>
    {
        private readonly ICondotifyCommandsRepository _repository;

        public Handler(ICondotifyCommandsRepository repository)
        {
            _repository = repository;
        }

        public async Task<UserAccessCreateResult> Handle(CreateUserAccessCommand request, CancellationToken cancellationToken)
        {
            var access = UserAccess.Create(
                request.Name,
                request.Email,
                request.Password,
                request.PhoneNumber,
                request.CPF,
                request.RG,
                request.BirthDate,
                request.Type,
                true,
                DateTime.Now,
                DateTime.Now);

            return await _repository.AddUserAccessAsync(access);
        }
    }
}

public class CreateUserAccessCommandValidator : AbstractValidator<CreateUserAccessCommand>
{
    public CreateUserAccessCommandValidator()
    {

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(6)
            .MaximumLength(50);

        RuleFor(x => x.Email)
            .NotEqual(x => x.Password);

        When(x => string.IsNullOrEmpty(x.CPF), () =>
        {
            RuleFor(x => x.Email)
                .NotEmpty()
                .EmailAddress()
                .MaximumLength(50);
        });

        When(x => string.IsNullOrEmpty(x.Email), () =>
        {
            RuleFor(x => x.CPF)
                .NotEmpty();
        });

        When(x => string.IsNullOrEmpty(x.RG), () =>
        {
            RuleFor(x => x.Email)
                .NotEmpty()
                .EmailAddress()
                .MaximumLength(50);
        });

        When(x => string.IsNullOrEmpty(x.Email), () =>
        {
            RuleFor(x => x.RG)
                .NotEmpty();
        });
    }
}

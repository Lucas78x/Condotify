using CondotifyAPI.Domain.Interfaces;
using CondotifyAPI.Domain.Models.Ticket;
using DigitalWorldOnline.Management.Api.Data;
using FluentValidation;
using MediatR;
using System.Net.Sockets;

namespace CondotifyAPI.Commands.Tickets;

public class CreateTicketCommand : IRequest<CreateTicketResultModel>
{
    public Guid UnitId { get; set; }
    public string Title { get; set; }
    public DateTime ExpiredDate { get; set; }

    /// <summary>
    /// Reference Owner
    /// </summary>
    public Guid LicenseId { get; set; }

    public bool IsSecondCopy { get; set; }
    public Guid? OriginalTicketId { get; set; }

    public CreateTicketCommand(
        Guid unitId,
        string title,
        DateTime expiredDate,
        Guid licenseId,
        bool isSecondCopy,
        Guid? originalTicketId)
    {
        UnitId = unitId;
        Title = title;
        ExpiredDate = expiredDate;
        LicenseId = licenseId;
        IsSecondCopy = isSecondCopy;
        OriginalTicketId = originalTicketId;
    }

    internal class Handler : IRequestHandler<CreateTicketCommand, CreateTicketResultModel>
    {
        private readonly ICondotifyCommandsRepository _repository;

        public Handler(ICondotifyCommandsRepository repository)
        {
            _repository = repository;
        }

        public async Task<CreateTicketResultModel> Handle(
            CreateTicketCommand request,
            CancellationToken cancellationToken)
        {
            var ticket = Ticket.Create(
                request.UnitId,
                request.Title,
                request.ExpiredDate,
                request.LicenseId,
                request.IsSecondCopy,
                request.OriginalTicketId,
                DateTime.Now,
                DateTime.Now);

            var result = await _repository.AddTicketAsync(ticket);

            if (result == TicketCreateResult.Created)
                return CreateTicketResultModel.Success(ticket.Id);

            return CreateTicketResultModel.Fail(result);
        }
    }

    public class CreateTicketCommandValidator : AbstractValidator<CreateTicketCommand>
    {
        public CreateTicketCommandValidator()
        {
            RuleFor(x => x.UnitId)
                .NotEmpty();

            RuleFor(x => x.LicenseId)
                .NotEmpty();

            RuleFor(x => x.Title)
                .NotEmpty()
                .MaximumLength(100);

            RuleFor(x => x.ExpiredDate)
                .GreaterThan(DateTime.Now);

            When(x => x.IsSecondCopy, () =>
            {
                RuleFor(x => x.OriginalTicketId)
                    .NotNull();
            });

            When(x => !x.IsSecondCopy, () =>
            {
                RuleFor(x => x.OriginalTicketId)
                    .Null();
            });
        }
    }
}

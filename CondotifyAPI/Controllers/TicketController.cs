using CondotifyAPI.Data.Tickets;
using DigitalWorldOnline.Management.Api.Data;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using static CondotifyAPI.Commands.Tickets.CreateTicketCommand;
using CondotifyAPI.Services.Security;

namespace DigitalWorldOnline.Management.Api.Controllers;

[ApiController]
[Route("api/tickets")]
public class TicketController : ControllerBase
{
    private readonly ISender _sender;
    private readonly string? _apiKey;

    public TicketController(ISender sender)
    {
        _sender = sender;
        _apiKey = ApiKeySecurity.GetConfiguredKey();
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromHeader(Name = "X-API-Key")] string apiKey,
        [FromBody] CreateTicketIn ticket)
    {
        if (!ApiKeySecurity.IsValid(_apiKey, apiKey))
            return Unauthorized();

        var command = ticket.ToCommand();
        var validator = await new CreateTicketCommandValidator().ValidateAsync(command);

        if (!validator.IsValid)
            return BadRequest(new
            {
                Result = "InvalidRequest",
                Errors = string.Join(";", validator.Errors.Select(x => x.ErrorMessage))
            });

        var result = await _sender.Send(command);

        if (result.Result == TicketCreateResult.Created)
            return Created("", new CreateTicketOut
            {
                Result = result.Result,
                TicketId = result.TicketId
            });

        return Conflict(new CreateTicketOut
        {
            Result = result.Result,
            Errors = MapTicketErrors(result.Result)
        });
    }

    private static string MapTicketErrors(TicketCreateResult result) =>
        result switch
        {
            TicketCreateResult.LicenseNotFound => "Licença não encontrada.",
            TicketCreateResult.UnitNotFound => "Unidade não encontrada.",
            TicketCreateResult.OriginalTicketNotFound => "Ticket original não encontrado.",
            TicketCreateResult.InvalidSecondCopy => "Segunda via inválida.",
            TicketCreateResult.TicketAlreadyExpired => "O ticket já está expirado.",
            _ => "Erro desconhecido."
        };
}

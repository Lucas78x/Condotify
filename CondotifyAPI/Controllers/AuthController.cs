using CondotifyAPI.Commands.Login;
using CondotifyAPI.Data.Login;
using CondotifyAPI.Domain.Enums.Login;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DigitalWorldOnline.Management.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly ISender _sender;

    public AuthController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet("validate")]
    [Authorize] 
    public IActionResult ValidateToken()
    {
        return Ok(new
        {
            Result = "Valid",
            User = User.Identity?.Name 
        });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginIn input)
    {
        if (string.IsNullOrWhiteSpace(input.Email) || string.IsNullOrWhiteSpace(input.Password))
            return BadRequest(new LoginOut { Result = "Email and Password are required." });

        var result = await _sender.Send(new LoginCommand(input.Email, input.Password));

        return result.Result switch
        {
            LoginResult.Success => Ok(new LoginOut
            {
                Result = "Success",
                AccessToken = result.AccessToken,
                ExpiresIn = result.ExpiresInSeconds
            }),

            LoginResult.InvalidCredentials => Unauthorized(new LoginOut
            {
                Result = "InvalidCredentials"
            }),

            _ => StatusCode(500, new LoginOut { Result = "Error" })
        };
    }

}
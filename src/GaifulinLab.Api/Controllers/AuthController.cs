using GaifulinLab.Api.Configuration;
using GaifulinLab.Contracts.Auth;
using GaifulinLab.Contracts.Common;
using GaifulinLab.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GaifulinLab.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IUserAuthenticationService authenticationService) : ControllerBase
{
    private const int MinimumLoginLength = 3;
    private const int MaximumLoginLength = 64;
    private const int MaximumPasswordLength = 128;

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(ApiRateLimitPolicies.Login)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        Response.Headers.CacheControl = "no-store";

        if (string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrEmpty(request.Password))
        {
            return InvalidCredentials();
        }

        var token = await authenticationService.AuthenticateAsync(request.Login, request.Password);
        return token is null
            ? InvalidCredentials()
            : Ok(new LoginResponse(token.Value, token.ExpiresAt));
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(ApiRateLimitPolicies.Registration)]
    [ProducesResponseType<RegisterResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<RegisterResponse>> Register(RegisterRequest request)
    {
        Response.Headers.CacheControl = "no-store";

        var login = request.Login?.Trim() ?? string.Empty;
        var validationErrors = ValidateRegistration(login, request.Password);
        if (validationErrors.Count > 0)
        {
            return BadRequest(new ApiErrorResponse(
                "invalid_registration",
                "Please correct the highlighted fields.",
                validationErrors));
        }

        var result = await authenticationService.RegisterAsync(login, request.Password);
        if (result.Succeeded)
        {
            return StatusCode(StatusCodes.Status201Created, new RegisterResponse(login));
        }

        if (result.LoginTaken)
        {
            return Conflict(new ApiErrorResponse(
                "login_taken",
                "This login is already in use."));
        }

        return BadRequest(new ApiErrorResponse(
            "invalid_registration",
            "The account could not be created.",
            new Dictionary<string, string[]> { ["Password"] = result.Errors.ToArray() }));
    }

    [HttpGet("session")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetSession() => NoContent();

    private UnauthorizedObjectResult InvalidCredentials() =>
        Unauthorized(new ApiErrorResponse("invalid_credentials", "Invalid login or password."));

    private static Dictionary<string, string[]> ValidateRegistration(string login, string? password)
    {
        var errors = new Dictionary<string, string[]>();
        if (login.Length is < MinimumLoginLength or > MaximumLoginLength)
        {
            errors["Login"] =
            [
                $"Login must be between {MinimumLoginLength} and {MaximumLoginLength} characters."
            ];
        }
        else if (login.Any(character =>
                     !char.IsAsciiLetterOrDigit(character)
                     && character is not '-' and not '.' and not '_' and not '@' and not '+'))
        {
            errors["Login"] = ["Login contains unsupported characters."];
        }

        if (string.IsNullOrEmpty(password) || password.Length > MaximumPasswordLength)
        {
            errors["Password"] =
            [
                $"Password is required and must not exceed {MaximumPasswordLength} characters."
            ];
        }

        return errors;
    }
}

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
public sealed class AuthController(IAdminAuthenticationService authenticationService) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(ApiRateLimitPolicies.Login)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public ActionResult<LoginResponse> Login(LoginRequest request)
    {
        Response.Headers.CacheControl = "no-store";

        if (string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrEmpty(request.Password))
        {
            return InvalidCredentials();
        }

        var token = authenticationService.Authenticate(request.Login, request.Password);
        return token is null
            ? InvalidCredentials()
            : Ok(new LoginResponse(token.Value, token.ExpiresAt));
    }

    [HttpGet("session")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetSession() => NoContent();

    private UnauthorizedObjectResult InvalidCredentials() =>
        Unauthorized(new ApiErrorResponse("invalid_credentials", "Invalid login or password."));
}

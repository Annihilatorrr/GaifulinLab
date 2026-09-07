using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
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
    private const int MaximumEmailLength = 254;
    private const int MinimumDisplayNameLength = 2;
    private const int MaximumDisplayNameLength = 100;
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

        var email = request.Login?.Trim() ?? string.Empty;
        var displayName = request.DisplayName?.Trim() ?? string.Empty;
        var validationErrors = ValidateRegistration(email, displayName, request.Password);
        if (validationErrors.Count > 0)
        {
            return BadRequest(new ApiErrorResponse(
                "invalid_registration",
                "Please correct the highlighted fields.",
                validationErrors));
        }

        var result = await authenticationService.RegisterAsync(email, displayName, request.Password);
        if (result.Succeeded)
        {
            return StatusCode(StatusCodes.Status201Created, new RegisterResponse(email));
        }

        if (result.LoginTaken)
        {
            return Conflict(new ApiErrorResponse(
                "login_taken",
                "This email address is already in use."));
        }

        return BadRequest(new ApiErrorResponse(
            "invalid_registration",
            "The account could not be created.",
            new Dictionary<string, string[]> { ["Password"] = result.Errors.ToArray() }));
    }

    [HttpGet("session")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetSession() => NoContent();

    [HttpGet("profile")]
    [Authorize]
    [ProducesResponseType<UserProfileResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserProfileResponse>> GetProfile()
    {
        var displayName = await authenticationService.GetDisplayNameAsync(GetCurrentUserId());
        return displayName is null ? NotFound() : Ok(new UserProfileResponse(displayName));
    }

    [HttpPut("profile")]
    [Authorize]
    [ProducesResponseType<UserProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserProfileResponse>> UpdateProfile(UpdateProfileRequest request)
    {
        var displayName = request.DisplayName?.Trim() ?? string.Empty;
        var errors = ValidateDisplayName(displayName);
        if (errors.Count > 0)
        {
            return BadRequest(new ApiErrorResponse(
                "invalid_profile",
                "Please correct the highlighted fields.",
                errors));
        }

        return await authenticationService.UpdateDisplayNameAsync(GetCurrentUserId(), displayName)
            ? Ok(new UserProfileResponse(displayName))
            : NotFound();
    }

    private UnauthorizedObjectResult InvalidCredentials() =>
        Unauthorized(new ApiErrorResponse("invalid_credentials", "Invalid login or password."));

    private static Dictionary<string, string[]> ValidateRegistration(
        string email,
        string displayName,
        string? password)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(email))
        {
            errors["Login"] = ["Email is required."];
        }
        else if (email.Length > MaximumEmailLength
                 || !new EmailAddressAttribute().IsValid(email))
        {
            errors["Login"] = ["Enter a valid email address."];
        }

        foreach (var (field, messages) in ValidateDisplayName(displayName))
        {
            errors[field] = messages;
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

    private static Dictionary<string, string[]> ValidateDisplayName(string displayName)
    {
        if (displayName.Length is >= MinimumDisplayNameLength and <= MaximumDisplayNameLength)
        {
            return [];
        }

        return new Dictionary<string, string[]>
        {
            ["DisplayName"] =
            [
                $"Display name must be between {MinimumDisplayNameLength} and {MaximumDisplayNameLength} characters."
            ]
        };
    }

    private string GetCurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Authenticated user id is missing.");
}

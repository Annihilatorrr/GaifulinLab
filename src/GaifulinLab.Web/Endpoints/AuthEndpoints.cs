using GaifulinLab.Contracts.Auth;
using GaifulinLab.Contracts.Common;
using GaifulinLab.Infrastructure.Authentication;

namespace GaifulinLab.Web.Endpoints;

public static class AuthEndpoints
{
    public const string LoginRateLimitPolicy = "auth-login";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth")
            .WithTags("Authentication");

        group.MapPost("/login", Login)
            .AllowAnonymous()
            .RequireRateLimiting(LoginRateLimitPolicy)
            .Produces<LoginResponse>()
            .Produces<ApiErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests);

        group.MapGet("/session", () => Results.NoContent())
            .RequireAuthorization(AuthorizationPolicies.Admin)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    private static IResult Login(
        LoginRequest request,
        IAdminAuthenticationService authenticationService,
        HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "no-store";

        if (string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrEmpty(request.Password))
        {
            return InvalidCredentials();
        }

        var token = authenticationService.Authenticate(request.Login, request.Password);
        return token is null
            ? InvalidCredentials()
            : Results.Ok(new LoginResponse(token.Value, token.ExpiresAt));
    }

    private static IResult InvalidCredentials() => Results.Json(
        new ApiErrorResponse("invalid_credentials", "Invalid login or password."),
        statusCode: StatusCodes.Status401Unauthorized);
}

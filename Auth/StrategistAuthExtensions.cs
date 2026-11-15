using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;

namespace Strategist.Auth;

/// <summary>
/// DI wiring for Strategist resource services. The typical consumer needs
/// exactly two lines:
/// <code>
/// var jwt = StrategistJwtOptions.Load(builder.Configuration);
/// builder.Services.AddStrategistJwtValidation(jwt).Services.AddPermissionPolicies();
/// </code>
/// after which <c>[Authorize]</c> accepts the server's bearer tokens and
/// <c>[Authorize(Policy = Permissions.X)]</c> enforces catalog permissions.
/// </summary>
public static class StrategistAuthExtensions
{
    /// <summary>The role that implicitly satisfies every permission policy.</summary>
    public const string AdminRole = "Admin";

    /// <summary>Registers JWT bearer as the default authenticate/challenge
    /// scheme, validating tokens signed by the Strategist identity server.
    /// The options instance is also registered as a singleton (the server
    /// itself resolves it to sign tokens).</summary>
    public static AuthenticationBuilder AddStrategistJwtValidation(
        this IServiceCollection services, StrategistJwtOptions options)
    {
        services.AddSingleton(options);
        return services
            .AddAuthentication(o =>
            {
                o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                o.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(o =>
                o.TokenValidationParameters = options.CreateTokenValidationParameters());
    }

    /// <summary>One authorization policy per catalog permission: satisfied by
    /// a same-named <see cref="Permissions.ClaimType"/> claim in the caller's
    /// token, or by the <see cref="AdminRole"/> role.</summary>
    public static IServiceCollection AddPermissionPolicies(this IServiceCollection services)
        => services.AddAuthorization(o =>
        {
            foreach (var permission in Permissions.All)
                o.AddPolicy(permission, p => p
                    .RequireAuthenticatedUser()
                    .RequireAssertion(ctx =>
                        ctx.User.IsInRole(AdminRole)
                        || ctx.User.HasClaim(Permissions.ClaimType, permission)));
        });
}

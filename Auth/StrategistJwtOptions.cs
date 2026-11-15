using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Strategist.Auth;

/// <summary>
/// The JWT wire contract between the Strategist identity server (which signs
/// access tokens) and every resource service (which validates them offline):
/// one shared HS256 key derived from the <c>Jwt:SigningKey</c> secret, plus
/// the issuer/audience pair. Load the same configuration in each service and
/// tokens minted by the server verify everywhere.
/// </summary>
public sealed class StrategistJwtOptions
{
    /// <summary>Default for both issuer and audience.</summary>
    public const string DefaultIssuer = "strategist-server";

    public required SymmetricSecurityKey SigningKey { get; init; }
    public required string Issuer { get; init; }
    public required string Audience { get; init; }

    /// <summary>True when <c>Jwt:SigningKey</c> was unset and a random
    /// process-lifetime key was generated instead — fine for local runs,
    /// but tokens won't verify across restarts or other services.</summary>
    public bool IsEphemeralKey { get; init; }

    /// <summary>
    /// Reads <c>Jwt:SigningKey</c> / <c>Jwt:Issuer</c> / <c>Jwt:Audience</c>
    /// (env: <c>Jwt__SigningKey</c>, ...). The signing secret may be any
    /// string: it is SHA256-hashed to the 256-bit HS256 key, which is the
    /// convention every issuer/validator must share.
    /// </summary>
    public static StrategistJwtOptions Load(IConfiguration configuration)
    {
        var configuredKey = configuration["Jwt:SigningKey"];
        var ephemeral = string.IsNullOrEmpty(configuredKey);
        return new StrategistJwtOptions
        {
            SigningKey = new SymmetricSecurityKey(ephemeral
                ? RandomNumberGenerator.GetBytes(32)
                : SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey!))),
            Issuer = configuration["Jwt:Issuer"] ?? DefaultIssuer,
            Audience = configuration["Jwt:Audience"] ?? DefaultIssuer,
            IsEphemeralKey = ephemeral,
        };
    }

    /// <summary>Validation parameters matching what the server signs.</summary>
    public TokenValidationParameters CreateTokenValidationParameters() => new()
    {
        ValidIssuer = Issuer,
        ValidAudience = Audience,
        IssuerSigningKey = SigningKey,
    };
}

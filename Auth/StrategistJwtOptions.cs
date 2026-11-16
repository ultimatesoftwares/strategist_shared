using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Strategist.Auth;

/// <summary>
/// The JWT wire contract between the Strategist identity server (which signs
/// access tokens) and every resource service (which validates them offline):
/// ES256 with an asymmetric key pair. Only the server is configured with the
/// private key (<c>Jwt:PrivateKey</c>); validators get the public key
/// (<c>Jwt:PublicKey</c>), so "only the server mints tokens" is enforced by
/// the crypto rather than by convention — compromising a validator leaks
/// nothing that can forge tokens.
/// </summary>
public sealed class StrategistJwtOptions
{
    /// <summary>Default for both issuer and audience.</summary>
    public const string DefaultIssuer = "strategist-server";

    /// <summary>Audience of the short-lived fleet tokens the server mints for
    /// the fleetd admin API (POST api/v1/fleet/token). fleetd validates with
    /// <see cref="CreateTokenValidationParameters"/> passing this audience;
    /// regular access tokens (audience <see cref="Audience"/>) don't pass.</summary>
    public const string FleetAudience = "fleetd";

    /// <summary>The ES256 key. Contains the private half only when this
    /// process is allowed to sign (<see cref="CanSign"/>); validation works
    /// either way.</summary>
    public required ECDsaSecurityKey Key { get; init; }

    public required string Issuer { get; init; }
    public required string Audience { get; init; }

    /// <summary>True when the private key is present (<c>Jwt:PrivateKey</c>
    /// or an ephemeral dev key) — i.e. this process is the token issuer.
    /// Determined at load time: ECDsaSecurityKey.PrivateKeyStatus is always
    /// Unknown, so the key object can't tell us.</summary>
    public required bool CanSign { get; init; }

    /// <summary>True when neither <c>Jwt:PrivateKey</c> nor
    /// <c>Jwt:PublicKey</c> was set and a random process-lifetime key pair
    /// was generated instead — fine for local runs, but tokens won't verify
    /// across restarts or other services.</summary>
    public bool IsEphemeralKey { get; init; }

    /// <summary>
    /// Reads <c>Jwt:PrivateKey</c> / <c>Jwt:PublicKey</c> / <c>Jwt:Issuer</c>
    /// / <c>Jwt:Audience</c> (env: <c>Jwt__PrivateKey</c>, ...). The key
    /// values are either literal PEM text or a path to a PEM file. Generate
    /// a pair with:
    /// <code>
    /// openssl ecparam -name prime256v1 -genkey -noout -out jwt_es256.pem
    /// openssl ec -in jwt_es256.pem -pubout -out jwt_es256.pub.pem
    /// </code>
    /// The identity server gets the private key; everything else gets only
    /// the public one.
    /// </summary>
    public static StrategistJwtOptions Load(IConfiguration configuration)
    {
        if (!string.IsNullOrEmpty(configuration["Jwt:SigningKey"]))
            throw new InvalidOperationException(
                "Jwt:SigningKey (shared-secret HS256) is no longer supported. Generate an ES256 key pair "
                + "(openssl ecparam -name prime256v1 -genkey -noout -out jwt_es256.pem && "
                + "openssl ec -in jwt_es256.pem -pubout -out jwt_es256.pub.pem), then set Jwt:PrivateKey "
                + "on the identity server and Jwt:PublicKey on resource services (PEM text or a file path).");

        var privateKey = configuration["Jwt:PrivateKey"];
        var publicKey = configuration["Jwt:PublicKey"];
        var ephemeral = string.IsNullOrEmpty(privateKey) && string.IsNullOrEmpty(publicKey);
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        if (!ephemeral)
            ecdsa.ImportFromPem(string.IsNullOrEmpty(privateKey)
                ? ResolvePem(publicKey!, "Jwt:PublicKey")
                : ResolvePem(privateKey!, "Jwt:PrivateKey"));
        return new StrategistJwtOptions
        {
            Key = new ECDsaSecurityKey(ecdsa),
            CanSign = ephemeral || !string.IsNullOrEmpty(privateKey),
            Issuer = configuration["Jwt:Issuer"] ?? DefaultIssuer,
            Audience = configuration["Jwt:Audience"] ?? DefaultIssuer,
            IsEphemeralKey = ephemeral,
        };
    }

    /// <summary>ES256 signing credentials — identity server only.</summary>
    public SigningCredentials CreateSigningCredentials() => CanSign
        ? new SigningCredentials(Key, SecurityAlgorithms.EcdsaSha256)
        : throw new InvalidOperationException(
            "this process holds only the public key (Jwt:PublicKey) and cannot sign tokens; "
            + "issuing is the identity server's job (Jwt:PrivateKey)");

    /// <summary>Validation parameters matching what the server signs.
    /// <paramref name="audience"/> overrides the accepted audience for
    /// scoped tokens (e.g. <see cref="FleetAudience"/>).</summary>
    public TokenValidationParameters CreateTokenValidationParameters(string? audience = null) => new()
    {
        ValidIssuer = Issuer,
        ValidAudience = audience ?? Audience,
        IssuerSigningKey = Key,
    };

    /// <summary>PEM text passes through; anything else is read as a file path.</summary>
    private static string ResolvePem(string value, string name)
    {
        if (value.Contains("-----BEGIN", StringComparison.Ordinal))
            return value;
        try
        {
            return File.ReadAllText(value);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new InvalidOperationException(
                $"{name} is neither PEM text nor a readable PEM file (got '{value}'): {e.Message}", e);
        }
    }
}

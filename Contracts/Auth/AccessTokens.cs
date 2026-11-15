using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Strategist.Contracts.Auth;

/// <summary>
/// Stateless, short-lived fleet access tokens shared between the issuer and
/// the validator. The Strategist server (or any holder of the shared fleet
/// secret) calls <see cref="Issue"/> and hands the token to clients; fleetd
/// only calls <see cref="Validate"/> on the "Authorization: Bearer ..."
/// value of every /api/v1 request - it never issues tokens and never sees
/// the secret on the wire.
///
/// Format: base64url(payload JSON) "." base64url(HMAC-SHA256(payload)).
/// The signing key is derived from the shared secret, so neither side keeps
/// token state (restarts don't invalidate tokens) and rotating the secret
/// instantly revokes everything issued before.
/// </summary>
public static class AccessTokens
{
    /// <summary>Key-derivation context: bump to invalidate all tokens on a
    /// format change.</summary>
    private static readonly byte[] KeyContext = "fleetd-access-token-v1"u8.ToArray();

    // Conventional lowercase claim names: the payload is a cross-service
    // wire contract, not an implementation detail.
    private sealed record Payload(
        [property: JsonPropertyName("iat")] long Iat,
        [property: JsonPropertyName("exp")] long Exp,
        [property: JsonPropertyName("jti")] string Jti);

    public static string Issue(string secret, TimeSpan ttl, out long expiresInSeconds)
    {
        var now = DateTimeOffset.UtcNow;
        expiresInSeconds = (long)ttl.TotalSeconds;
        var payload = JsonSerializer.SerializeToUtf8Bytes(new Payload(
            now.ToUnixTimeSeconds(),
            now.Add(ttl).ToUnixTimeSeconds(),
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8))));
        return $"{Base64Url.EncodeToString(payload)}.{Base64Url.EncodeToString(Sign(secret, payload))}";
    }

    /// <summary>True when the token was signed with (a key derived from) the
    /// given secret and has not expired.</summary>
    public static bool Validate(string secret, string token)
    {
        var dot = token.IndexOf('.');
        if (dot <= 0 || dot == token.Length - 1)
            return false;
        Span<byte> payload = stackalloc byte[Base64Url.GetMaxDecodedLength(dot)];
        Span<byte> signature = stackalloc byte[Base64Url.GetMaxDecodedLength(token.Length - dot - 1)];
        if (!Base64Url.TryDecodeFromChars(token.AsSpan(0, dot), payload, out var payloadLength)
            || !Base64Url.TryDecodeFromChars(token.AsSpan(dot + 1), signature, out var signatureLength))
            return false;
        payload = payload[..payloadLength];

        if (!CryptographicOperations.FixedTimeEquals(signature[..signatureLength], Sign(secret, payload)))
            return false;

        try
        {
            var parsed = JsonSerializer.Deserialize<Payload>(payload);
            return parsed is not null && DateTimeOffset.UtcNow.ToUnixTimeSeconds() < parsed.Exp;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static byte[] Sign(string secret, ReadOnlySpan<byte> payload)
    {
        // key = HMAC(context, secret): domain-separates the signing key from
        // any other use of the shared secret.
        var key = HMACSHA256.HashData(KeyContext, Encoding.UTF8.GetBytes(secret));
        return HMACSHA256.HashData(key, payload);
    }
}

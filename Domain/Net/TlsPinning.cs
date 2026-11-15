using System.Net.Security;
using System.Security.Cryptography;

namespace Core.Net;

/// <summary>
/// TLS pinning for self-signed endpoints. Instead of accepting every
/// certificate, callers pin the endpoint's SHA-256 certificate thumbprint:
/// when an expected thumbprint is configured only that exact certificate is
/// accepted; when none is configured the validator trusts the FIRST
/// certificate it sees (trust-on-first-use), reports its thumbprint so it
/// can be persisted/pinned, and rejects any different certificate for the
/// validator's lifetime (one validator per connection/client).
/// </summary>
public static class TlsPinning
{
    /// <summary>Uppercase hex SHA-256 over the certificate's DER bytes —
    /// the pinnable identity of a self-signed certificate.</summary>
    public static string Sha256Thumbprint(byte[] rawCertificateData) =>
        Convert.ToHexString(SHA256.HashData(rawCertificateData));

    /// <summary>Canonical thumbprint form: hex digits only (colons, spaces
    /// and dashes stripped), uppercase — accepts the formats humans paste.</summary>
    public static string Normalize(string thumbprint) =>
        string.Concat(thumbprint.Where(Uri.IsHexDigit)).ToUpperInvariant();

    /// <summary>Does the certificate match the expected SHA-256 thumbprint
    /// (any human-pasted format)?</summary>
    public static bool Matches(byte[] rawCertificateData, string expectedThumbprint) =>
        Normalize(expectedThumbprint).Length > 0
        && Sha256Thumbprint(rawCertificateData) == Normalize(expectedThumbprint);

    /// <summary>
    /// Builds a certificate validator pinned to <paramref name="expectedThumbprint"/>,
    /// or a trust-on-first-use validator when none is configured.
    /// <paramref name="onSeen"/> receives (thumbprint, accepted) for every
    /// presented certificate — callers log it so operators can pin it.
    /// </summary>
    public static RemoteCertificateValidationCallback Validator(
        string? expectedThumbprint, Action<string, bool>? onSeen = null)
    {
        var pinned = expectedThumbprint is { Length: > 0 } configured
            ? Normalize(configured) : null;
        // Trust-on-first-use state — one validator per HttpClient/handler,
        // so the pin lives exactly as long as the connection owner.
        var firstSeenLock = new object();
        string? firstSeen = null;
        return (_, certificate, _, _) =>
        {
            if (certificate is null)
            {
                onSeen?.Invoke("", false);
                return false;
            }
            var thumbprint = Sha256Thumbprint(certificate.GetRawCertData());
            bool accepted;
            if (pinned is not null)
            {
                accepted = thumbprint == pinned;
            }
            else
            {
                lock (firstSeenLock) firstSeen ??= thumbprint;
                accepted = thumbprint == firstSeen;
            }
            onSeen?.Invoke(thumbprint, accepted);
            return accepted;
        };
    }
}

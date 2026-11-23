using System.Text.Json;

namespace LSE.NET;

/// <summary>
/// A refusal from the LSE Data 3.0 server: the structured error body
/// {"code", "message", "required_plan", "retry_after"} with its HTTP status.
/// Status 0 is a client-side or WebSocket failure.
/// </summary>
public sealed class LseException : Exception
{
    /// <summary>Creates an exception with the HTTP status (or zero) and machine-readable code.</summary>
    public LseException(int status, string code, string message,
        TimeSpan? retryAfter = null, string? requiredPlanJson = null)
        : base(message)
    {
        Status = status;
        Code = code;
        RetryAfter = retryAfter;
        RequiredPlanJson = requiredPlanJson;
    }

    /// <summary>HTTP status of the refusal, or 0 for a client-side failure.</summary>
    public int Status { get; }

    /// <summary>The server's refusal code, e.g. "plan-required", "data-unavailable",
    /// "calls-exhausted"; "no-answer" or "checksum-mismatch" client-side.</summary>
    public string Code { get; }

    /// <summary>The Retry-After a 429 carried, when it did.</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>The "required_plan" object of a 403 plan-required, as raw JSON.</summary>
    public string? RequiredPlanJson { get; }

    /// <summary>The close code for a WebSocket refusal, when available.</summary>
    public System.Net.WebSockets.WebSocketCloseStatus? WebSocketCloseStatus { get; init; }

    /// <summary>Builds the exception from a refusal's status, body and
    /// Retry-After header. Tolerates non-JSON bodies.</summary>
    internal static LseException Parse(int status, string body, TimeSpan? retryAfterHeader = null, string? secret = null)
    {
        var code = $"http-{status}";
        var message = $"LSE returned HTTP {status}.";
        TimeSpan? retryAfter = retryAfterHeader < TimeSpan.Zero ? TimeSpan.Zero : retryAfterHeader;
        string? requiredPlan = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String)
                    code = c.GetString()!;
                if (root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                    message = m.GetString()!;
                if (retryAfter is null && root.TryGetProperty("retry_after", out var r)
                    && r.ValueKind == JsonValueKind.Number && r.TryGetDouble(out var seconds)
                    && seconds is >= 0 and <= int.MaxValue)
                    retryAfter = TimeSpan.FromSeconds(seconds);
                if (root.TryGetProperty("required_plan", out var p) && p.ValueKind == JsonValueKind.Object)
                    requiredPlan = p.GetRawText();
            }
        }
        catch (JsonException)
        {
            // A proxy error page is not safe to surface as an exception message.
        }
        return new LseException(status, Redact(code, secret, 128),
            Redact(message, secret, 512), retryAfter,
            requiredPlan is null ? null : Redact(requiredPlan, secret, 64 * 1024));
    }

    internal static string Redact(string value, string? secret, int limit)
    {
        if (!string.IsNullOrEmpty(secret))
            value = value.Replace(secret, "[redacted]", StringComparison.Ordinal);
        return value.Length > limit ? value[..limit] : value;
    }
}

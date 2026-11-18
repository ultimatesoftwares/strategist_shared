using System.Text.Json;

namespace MarketData.Lse;

/// <summary>
/// A refusal from the LSE Data 3.0 server: the structured error body
/// {"code", "message", "required_plan", "retry_after"} with its HTTP status.
/// Status 0 is a client-side failure (no answer, checksum mismatch).
/// </summary>
public sealed class LseException : Exception
{
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

    public override string ToString() => $"LSE {Status} {Code}: {Message}";

    /// <summary>Builds the exception from a refusal's status, body and
    /// Retry-After header. Tolerates non-JSON bodies.</summary>
    internal static LseException Parse(int status, string body, TimeSpan? retryAfterHeader = null)
    {
        var code = $"http-{status}";
        var message = body.Length > 512 ? body[..512] : body;
        TimeSpan? retryAfter = retryAfterHeader;
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
                if (root.TryGetProperty("retry_after", out var r) && r.ValueKind == JsonValueKind.Number)
                    retryAfter = TimeSpan.FromSeconds(r.GetDouble());
                if (root.TryGetProperty("required_plan", out var p) && p.ValueKind == JsonValueKind.Object)
                    requiredPlan = p.GetRawText();
            }
        }
        catch (JsonException)
        {
            // Not JSON (a proxy error page): keep the raw body as the message.
        }
        return new LseException(status, code, message, retryAfter, requiredPlan);
    }
}

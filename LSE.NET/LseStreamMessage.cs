using System.Text.Json;

namespace LSE.NET;

/// <summary>A complete live feed message, including acknowledgements and server errors.</summary>
/// <param name="Type">The vendor's message type.</param>
/// <param name="Payload">The full independent JSON value, with no disposal requirement.</param>
public sealed record LseStreamMessage(string Type, JsonElement Payload)
{
    /// <summary>Returns a typed update, or null for a non-tick message.</summary>
    public LseLiveTick? GetTick()
    {
        if (Type != "tick")
            return null;
        try
        {
            var symbol = Payload.GetProperty("symbol").GetString();
            if (string.IsNullOrWhiteSpace(symbol))
                throw new FormatException();
            var timestamp = Payload.GetProperty("ts").GetString() ?? throw new FormatException();
            return new LseLiveTick(symbol, LseParsers.ParseUtcTime(timestamp),
                Finite(Payload.GetProperty("price")), Number("bid"), Number("ask"), Number("volume"),
                Integer("ts_ns"), Integer("seq"), Integer("pair_id"), Text("event_id"), Text("name"));
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException or KeyNotFoundException)
        {
            throw new LseException(0, "invalid-tick", "The feed sent a tick with missing or invalid fields.");
        }
    }

    private bool Has(string field, out JsonElement value) =>
        Payload.TryGetProperty(field, out value) && value.ValueKind != JsonValueKind.Null;
    private double? Number(string field) => Has(field, out var value) ? Finite(value) : null;
    private static double Finite(JsonElement value) =>
        value.TryGetDouble(out var number) && double.IsFinite(number) ? number : throw new FormatException();
    private long? Integer(string field) => Has(field, out var value) ? value.GetInt64() : null;
    private string? Text(string field) => Has(field, out var value) ? value.GetString() : null;
}

/// <summary>A live market update. Optional fields vary by feed; Time is UTC at .NET tick precision.</summary>
public readonly record struct LseLiveTick(string Symbol, DateTime Time, double Price,
    double? Bid, double? Ask, double? Volume, long? TimestampNanoseconds, long? Sequence,
    long? PairId, string? EventId, string? Name);

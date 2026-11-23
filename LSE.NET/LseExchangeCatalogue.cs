using System.Globalization;
using System.Text.Json;

namespace LSE.NET;

/// <summary>CME symbol discovery on the exchange's HTTP origin, using an entitled key.</summary>
public sealed class LseExchangeCatalogue : IDisposable
{
    private readonly LseDataClient _transport;

    /// <summary>Creates a reusable client for the published CME symbol catalogue.</summary>
    public LseExchangeCatalogue(string apiKey, HttpMessageHandler? handler = null)
    {
        _transport = new(new LseOptions
        {
            ApiKey = apiKey,
            BaseUrl = "https://cme.londonstrategicedge.com",
            MaxJsonBytes = 32 * 1024 * 1024
        }, handler);
    }

    /// <summary>Returns the symbols that CME lists for the specified live depth. Dispose the result.</summary>
    public Task<JsonDocument> GetCmeSymbolsAsync(LseBookLevel level = LseBookLevel.Orders, CancellationToken ct = default)
    {
        LseExchangeStream.ValidateLevel(level);
        return _transport.ReadJsonAsync("/v1/symbols?level=" + ((int)level).ToString(CultureInfo.InvariantCulture),
            ct, "/v1/");
    }

    /// <summary>Disposes the underlying HTTP transport.</summary>
    public void Dispose() => _transport.Dispose();
}

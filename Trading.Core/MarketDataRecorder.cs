namespace Trading.Core;

/// <summary>
/// Persists every book update fed to an engine as backtest-format CSV
/// (<see cref="MarketDataFile"/>), so live sessions produce data that can be
/// replayed through the backtest engine or used to train strategy models.
///
/// Prices are written in the producing broker adapter's fixed-point scale;
/// replay with the matching price scale (sim 100, Alpaca 10 000,
/// cTrader 100 000).
///
/// Not thread-safe: <see cref="Record"/> must be called from the single
/// engine event thread (<see cref="EngineHost"/> wires this up). Updates for
/// unregistered symbol ids are ignored.
/// </summary>
public sealed class MarketDataRecorder : IDisposable
{
    private const int FlushEvery = 256;

    private readonly StreamWriter _writer;
    private readonly Dictionary<uint, string> _symbolNames = new();

    public string FilePath { get; }
    public long RecordsWritten { get; private set; }

    public MarketDataRecorder(string path)
    {
        FilePath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _writer = new StreamWriter(FilePath);
        _writer.WriteLine(MarketDataFile.Header);
    }

    /// <summary>Map an engine symbol id to the name written in the file.</summary>
    public void RegisterSymbol(string name, uint symbolId) => _symbolNames[symbolId] = name;

    public void Record(in BookUpdate update)
    {
        if (!_symbolNames.TryGetValue(update.SymbolId, out var symbol))
            return;

        MarketDataFile.WriteRecord(_writer, new MarketDataRecord(
            update.TsNanos, symbol, update.Side, update.Price, update.Qty));

        // Bounded data loss on hard crash without a syscall per update.
        if (++RecordsWritten % FlushEvery == 0)
            _writer.Flush();
    }

    public void Flush() => _writer.Flush();

    public void Dispose() => _writer.Dispose();
}

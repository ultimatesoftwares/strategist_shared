namespace Trading.Core.Engine;

/// <summary>
/// Named bar features an ML.NET model may take instead of the five-return
/// contract. Each is computed at the newest closed bar from past bars only:
/// <list type="bullet">
/// <item><c>Z20</c>, <c>Z60</c>, <c>Z240</c>: z-score of log close over 20/60/240 bars (population std; 0 when flat).</item>
/// <item><c>Rsi14</c>: 100 · gains / (gains + losses) of log returns over 14 bars (50 when flat).</item>
/// <item><c>Ret1</c>, <c>Ret5</c>, <c>Ret15</c>: log return over 1/5/15 bars, in bps.</item>
/// <item><c>VolRatio</c>: std of log returns over 30 bars / std over 240 bars.</item>
/// <item><c>VolumeRatio</c>: bar volume / mean volume over 60 bars (1 when the feed has no volume).</item>
/// <item><c>HourSin</c>, <c>HourCos</c>: UTC time of day of the bar open on the unit circle.</item>
/// </list>
/// </summary>
public static class BarFeatures
{
    /// <summary>Feature names, matched case-insensitively to model input columns.</summary>
    public static readonly IReadOnlyList<string> Names =
    [
        "Z20", "Z60", "Z240", "Rsi14", "Ret1", "Ret5", "Ret15",
        "VolRatio", "VolumeRatio", "HourSin", "HourCos",
    ];

    /// <summary>Bars of history behind the newest bar that every feature needs.</summary>
    public const int Warmup = 240;

    /// <summary>Index of <c>Z60</c> in <see cref="Names"/>.</summary>
    public const int Z60 = 1;

    /// <summary>Index of <c>VolRatio</c> in <see cref="Names"/>.</summary>
    public const int VolRatio = 7;

    /// <summary>Index of <paramref name="name"/> in <see cref="Names"/>, or -1.</summary>
    public static int IndexOf(string name)
    {
        for (var i = 0; i < Names.Count; i++)
        {
            if (string.Equals(Names[i], name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }
}

/// <summary>
/// Rolling per-symbol bar history that computes <see cref="BarFeatures"/>.
/// Bars are taken in order regardless of time gaps (as a model trained on a
/// bar array sees them); stale or duplicate bars are ignored.
/// </summary>
public sealed class BarFeatureWindow
{
    private const int Capacity = BarFeatures.Warmup + 1;
    private readonly double[] _logClose = new double[Capacity];
    private readonly double[] _volume = new double[Capacity];
    private long _lastTimeMs = long.MinValue;
    private long _timeMs;
    private int _count;

    public bool IsWarm => _count == Capacity;

    /// <summary>Closed bars folded in so far (warm at <see cref="WarmTarget"/>).</summary>
    public int Count => _count;

    /// <summary>Open time (Unix ms) of the newest folded bar; MinValue = empty.</summary>
    public long NewestBarOpenMs => _lastTimeMs;

    /// <summary>Bars needed before <see cref="IsWarm"/>: <see cref="BarFeatures.Warmup"/> + 1.</summary>
    public const int WarmTarget = Capacity;

    /// <summary>Folds in a closed bar; false when it was ignored.</summary>
    public bool Add(long openTimeMs, double close, double volume)
    {
        if (openTimeMs <= _lastTimeMs || !(close > 0) || !double.IsFinite(close))
            return false;
        _lastTimeMs = openTimeMs;
        _timeMs = openTimeMs;
        if (_count == Capacity)
        {
            Array.Copy(_logClose, 1, _logClose, 0, Capacity - 1);
            Array.Copy(_volume, 1, _volume, 0, Capacity - 1);
            _count--;
        }
        _logClose[_count] = Math.Log(close);
        _volume[_count] = double.IsFinite(volume) && volume > 0 ? volume : 0;
        _count++;
        return true;
    }

    /// <summary>Writes every feature, in <see cref="BarFeatures.Names"/> order. Requires <see cref="IsWarm"/>.</summary>
    public void Compute(Span<double> features)
    {
        if (!IsWarm)
            throw new InvalidOperationException("the feature window is not warm");
        var i = Capacity - 1;
        Span<double> ret = stackalloc double[Capacity];
        ret[0] = 0;
        for (var k = 1; k < Capacity; k++)
            ret[k] = _logClose[k] - _logClose[k - 1];

        features[0] = ZScore(_logClose, i, 20);
        features[1] = ZScore(_logClose, i, 60);
        features[2] = ZScore(_logClose, i, 240);
        features[3] = Rsi(ret, i, 14);
        features[4] = ret[i] * 1e4;
        features[5] = (_logClose[i] - _logClose[i - 5]) * 1e4;
        features[6] = (_logClose[i] - _logClose[i - 15]) * 1e4;
        features[7] = StdDev(ret, i, 30) / Math.Max(StdDev(ret, i, 240), 1e-12);
        var meanVolume = Mean(_volume, i, 60);
        features[8] = meanVolume > 0 ? _volume[i] / Math.Max(meanVolume, 1e-12) : 1.0;
        var time = DateTimeOffset.FromUnixTimeMilliseconds(_timeMs).UtcDateTime;
        var hour = time.Hour + time.Minute / 60.0;
        features[9] = Math.Sin(2 * Math.PI * hour / 24);
        features[10] = Math.Cos(2 * Math.PI * hour / 24);
    }

    /// <summary>
    /// Standard deviation of the newest <paramref name="window"/> 1-bar log
    /// returns, in bps — the short-horizon volatility behind cost-aware
    /// targets and stops. NaN until <paramref name="window"/> + 1 bars exist.
    /// </summary>
    public double SigmaBps(int window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(window, 2);
        if (_count < window + 1)
            return double.NaN;
        var i = _count - 1;
        Span<double> ret = stackalloc double[window];
        for (var k = 0; k < window; k++)
            ret[k] = _logClose[i - window + 1 + k] - _logClose[i - window + k];
        return StdDev(ret, window - 1, window) * 1e4;
    }

    /// <summary>
    /// Kaufman efficiency ratio over the newest <paramref name="window"/>
    /// bars: |net log move| / sum of |1-bar log moves|, in [0, 1]. Near 0 =
    /// choppy (mean-reverting) tape, near 1 = one-way trend. NaN until
    /// <paramref name="window"/> + 1 bars exist; 0 when the tape is flat.
    /// </summary>
    public double EfficiencyRatio(int window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(window, 2);
        if (_count < window + 1)
            return double.NaN;
        var i = _count - 1;
        double path = 0;
        for (var k = i - window + 1; k <= i; k++)
            path += Math.Abs(_logClose[k] - _logClose[k - 1]);
        return path <= 0 ? 0 : Math.Abs(_logClose[i] - _logClose[i - window]) / path;
    }

    /// <summary>
    /// Append every folded bar (oldest first) to <paramref name="into"/> for
    /// checkpointing; bar open times are reconstructed backwards from the
    /// newest bar at <paramref name="barIntervalMs"/> spacing (only the
    /// newest open time is tracked exactly). Returns the number appended.
    /// </summary>
    public int ExportBars(long barIntervalMs, List<WarmupBarSnapshot> into)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(barIntervalMs, 0);
        ArgumentNullException.ThrowIfNull(into);
        for (var k = 0; k < _count; k++)
        {
            into.Add(new WarmupBarSnapshot(
                _lastTimeMs - (long)(_count - 1 - k) * barIntervalMs,
                Math.Exp(_logClose[k]), _volume[k]));
        }
        return _count;
    }

    private static double Mean(ReadOnlySpan<double> x, int i, int w)
    {
        double s = 0;
        for (var k = i - w + 1; k <= i; k++)
            s += x[k];
        return s / w;
    }

    private static double StdDev(ReadOnlySpan<double> x, int i, int w)
    {
        double m = Mean(x, i, w), ss = 0;
        for (var k = i - w + 1; k <= i; k++)
        {
            var d = x[k] - m;
            ss += d * d;
        }
        return Math.Sqrt(ss / w);
    }

    private static double ZScore(ReadOnlySpan<double> x, int i, int w)
    {
        double m = Mean(x, i, w), sd = StdDev(x, i, w);
        return sd < 1e-9 ? 0 : (x[i] - m) / sd;
    }

    private static double Rsi(ReadOnlySpan<double> r, int i, int w)
    {
        double g = 0, l = 0;
        for (var k = i - w + 1; k <= i; k++)
        {
            if (r[k] > 0) g += r[k];
            else l -= r[k];
        }
        return g + l == 0 ? 50 : 100 * g / (g + l);
    }
}

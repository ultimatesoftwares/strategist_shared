namespace Trading.Core.Engine;

/// <summary>
/// One model inference: the predicted class (1 = up), P(up) and the raw
/// regression <c>Score</c> (NaN for classifiers).
/// </summary>
public readonly record struct ModelPrediction(long Class, float ProbabilityUp, float Score = float.NaN);

/// <summary>What a model's output means to the strategy.</summary>
public enum ModelOutputKind
{
    /// <summary>P(up) of a binary classifier (or a score whose sign is the direction).</summary>
    Probability,
    /// <summary>
    /// A regression <c>Score</c> over <see cref="BarFeatures"/>: the predicted
    /// payoff in bps of fading the <c>Z60</c> extreme.
    /// </summary>
    Edge,
}

/// <summary>
/// A predictor over either <see cref="ModelPredictor.ReturnCount"/>
/// close-to-close returns (newest first) or named <see cref="BarFeatures"/>.
/// Single-threaded by contract.
/// </summary>
public interface IModelPredictor : IDisposable
{
    /// <summary>Number of features <see cref="Predict"/> takes.</summary>
    int FeatureCount => ModelPredictor.ReturnCount;

    /// <summary>
    /// The <see cref="BarFeatures"/> index of each input, in input order; null
    /// for the close-to-close returns contract.
    /// </summary>
    IReadOnlyList<int>? FeatureIndexes => null;

    ModelOutputKind OutputKind => ModelOutputKind.Probability;

    ModelPrediction Predict(ReadOnlySpan<float> features);
}

/// <summary>Loads an ML.NET <c>model.zip</c> into a predictor.</summary>
public static class ModelPredictor
{
    /// <summary>
    /// Number of close-to-close returns in the default (returns-vector) model
    /// input contract.
    /// </summary>
    public const int ReturnCount = 5;

    /// <summary>
    /// Model features from close-to-close returns ordered oldest to newest:
    /// the model wants them newest first, as f32. Returns false when any
    /// return is non-finite (as f64 or after the f32 cast).
    /// </summary>
    public static bool TryFeaturesFromReturns(
        ReadOnlySpan<double> oldestToNewest, Span<float> newestFirst)
    {
        if (oldestToNewest.Length != ReturnCount || newestFirst.Length != ReturnCount)
            return false;
        for (var i = 0; i < ReturnCount; i++)
        {
            var value = oldestToNewest[ReturnCount - 1 - i];
            if (!double.IsFinite(value))
                return false;
            var feature = (float)value;
            if (!float.IsFinite(feature))
                return false;
            newestFirst[i] = feature;
        }
        return true;
    }

    /// <summary>ML.NET saves models as zip archives (PK\x03\x04 magic).</summary>
    private static bool IsZip(ReadOnlySpan<byte> header) =>
        header.Length >= 4 && header[0] == (byte)'P' && header[1] == (byte)'K'
            && header[2] == 3 && header[3] == 4;

    public static IModelPredictor Load(string path)
    {
        Span<byte> header = stackalloc byte[4];
        using (var stream = File.OpenRead(path))
        {
            var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
            if (!IsZip(header[..read]))
                throw new InvalidOperationException(
                    $"`{path}` is not an ML.NET model.zip; ONNX models are no longer supported");
        }
        return MlNetPredictor.Load(path);
    }
}

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
/// A predictor over either <see cref="OnnxPredictor.FeatureCount"/>
/// close-to-close returns (newest first) or named <see cref="BarFeatures"/>.
/// Single-threaded by contract.
/// </summary>
public interface IModelPredictor : IDisposable
{
    /// <summary>Number of features <see cref="Predict"/> takes.</summary>
    int FeatureCount => OnnxPredictor.FeatureCount;

    /// <summary>
    /// The <see cref="BarFeatures"/> index of each input, in input order; null
    /// for the close-to-close returns contract.
    /// </summary>
    IReadOnlyList<int>? FeatureIndexes => null;

    ModelOutputKind OutputKind => ModelOutputKind.Probability;

    ModelPrediction Predict(ReadOnlySpan<float> features);
}

/// <summary>Serialized model formats the engine can run.</summary>
public enum ModelFormat
{
    /// <summary>ONNX ModelProto (see <see cref="OnnxPredictor"/>).</summary>
    Onnx,
    /// <summary>ML.NET <c>model.zip</c> (see <see cref="MlNetPredictor"/>).</summary>
    MlNet,
}

/// <summary>Loads a model file with the predictor matching its format.</summary>
public static class ModelPredictor
{
    /// <summary>ML.NET saves models as zip archives; ONNX files are protobuf.</summary>
    public static ModelFormat Detect(ReadOnlySpan<byte> header) =>
        header.Length >= 4 && header[0] == (byte)'P' && header[1] == (byte)'K'
            && header[2] == 3 && header[3] == 4
            ? ModelFormat.MlNet
            : ModelFormat.Onnx;

    public static ModelFormat Detect(string path)
    {
        Span<byte> header = stackalloc byte[4];
        using var stream = File.OpenRead(path);
        var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        return Detect(header[..read]);
    }

    public static IModelPredictor Load(string path) => Detect(path) switch
    {
        ModelFormat.MlNet => MlNetPredictor.Load(path),
        _ => OnnxPredictor.Load(path),
    };
}

using Microsoft.ML.OnnxRuntime;

namespace Trading.Core.Engine;

/// <summary>
/// The ONNX predictor behind the engine's model-driven strategy, backed by
/// ONNX Runtime (Microsoft.ML.OnnxRuntime).
///
/// <para><b>Model contract</b>: one <c>f32[1, FeatureCount]</c> input of
/// close-to-close returns, <b>newest first</b>, and two outputs named
/// <c>label</c> (<c>i64[1]</c>, the predicted class: 1 = up) and
/// <c>probabilities</c> (<c>f32[1, 2]</c>, <c>[P(down), P(up)]</c>) — the
/// shape every sklearn/skl2onnx binary classifier exports naturally.</para>
/// </summary>
public sealed class OnnxPredictor : IDisposable
{
    public const int FeatureCount = 5;

    private static readonly string[] OutputNames = ["label", "probabilities"];
    private static readonly long[] InputShape = [1, FeatureCount];

    private readonly InferenceSession _session;
    private readonly RunOptions _runOptions = new();
    private readonly string _inputName;
    private readonly string[] _inputNames;
    // Reused per call: the engine is single-threaded by contract.
    private readonly float[] _inputBuffer = new float[FeatureCount];

    public readonly record struct Prediction(long Class, float ProbabilityUp);

    private OnnxPredictor(InferenceSession session, string inputName)
    {
        _session = session;
        _inputName = inputName;
        _inputNames = [inputName];
    }

    public static OnnxPredictor Load(string modelPath)
    {
        InferenceSession? session = null;
        try
        {
            session = new InferenceSession(modelPath);
            if (session.InputMetadata.Count != 1)
                throw new InvalidOperationException("the ONNX model must have exactly one input");
            if (!session.OutputMetadata.ContainsKey("label"))
                throw new InvalidOperationException("the ONNX model has no `label` output");
            if (!session.OutputMetadata.ContainsKey("probabilities"))
                throw new InvalidOperationException("the ONNX model has no `probabilities` output");
            var predictor = new OnnxPredictor(session, session.InputMetadata.Keys.First());
            session = null; // Owned by the predictor now.
            return predictor;
        }
        finally
        {
            session?.Dispose();
        }
    }

    /// <summary>
    /// Model features from close-to-close returns ordered oldest to newest:
    /// the model wants them newest first, as f32. Returns false when any
    /// return is non-finite (as f64 or after the f32 cast).
    /// </summary>
    public static bool TryFeaturesFromReturns(
        ReadOnlySpan<double> oldestToNewest, Span<float> newestFirst)
    {
        if (oldestToNewest.Length != FeatureCount || newestFirst.Length != FeatureCount)
            return false;
        for (var i = 0; i < FeatureCount; i++)
        {
            var value = oldestToNewest[FeatureCount - 1 - i];
            if (!double.IsFinite(value))
                return false;
            var feature = (float)value;
            if (!float.IsFinite(feature))
                return false;
            newestFirst[i] = feature;
        }
        return true;
    }

    public Prediction Predict(ReadOnlySpan<float> features)
    {
        if (features.Length != FeatureCount)
            throw new InvalidOperationException(
                $"expected {FeatureCount} features, got {features.Length}");
        foreach (var value in features)
        {
            if (!float.IsFinite(value))
                throw new InvalidOperationException("features must contain only finite numbers");
        }

        features.CopyTo(_inputBuffer);
        using var input = OrtValue.CreateTensorValueFromMemory(_inputBuffer, InputShape);
        using var outputs = _session.Run(
            _runOptions, _inputNames, [input], OutputNames);

        var labels = outputs[0].GetTensorDataAsSpan<long>();
        var probabilities = outputs[1].GetTensorDataAsSpan<float>();
        if (labels.Length != 1 || probabilities.Length != 2)
            throw new InvalidOperationException(
                "unexpected ONNX output shape for binary classification");

        var probabilityUp = probabilities[1];
        if (probabilityUp is not (>= 0.0f and <= 1.0f))
            throw new InvalidOperationException(
                "ONNX model returned an invalid positive-class probability");

        return new Prediction(labels[0], probabilityUp);
    }

    public void Dispose()
    {
        _runOptions.Dispose();
        _session.Dispose();
    }
}

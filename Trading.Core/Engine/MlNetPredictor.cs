using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers.FastTree;

namespace Trading.Core.Engine;

/// <summary>
/// The engine's model predictor: runs an ML.NET <c>model.zip</c> saved with
/// <c>MLContext.Model.Save(model, schema, path)</c>.
///
/// <para><b>Input</b>: the saved input schema (a column named <c>Label</c>
/// is ignored) holds either</para>
/// <list type="bullet">
/// <item><b>Named features</b>: up to <see cref="MaxNamedFeatures"/> <c>float</c>
/// columns whose names are all <see cref="BarFeatures"/> (e.g. <c>Z60</c>,
/// <c>Rsi14</c>), in any order; or</item>
/// <item><b>Returns</b>: <see cref="ModelPredictor.ReturnCount"/> float
/// close-to-close returns, <b>newest first</b> — one
/// <c>float[FeatureCount]</c> vector column, or that many <c>float</c>
/// columns in schema order.</item>
/// </list>
///
/// <para><b>Output</b>: a calibrated binary classifier's <c>Probability</c>
/// (P(up)), with <c>PredictedLabel</c> as the class when present; otherwise
/// a scalar <c>Score</c>. Over returns, the score's sign gives the direction
/// (P(up) is 1 above zero, 0 below and 0.5 at zero). Over named features the
/// score is the predicted payoff in bps of fading the <c>Z60</c> extreme
/// (<see cref="ModelOutputKind.Edge"/>).</para>
/// </summary>
public sealed class MlNetPredictor : IModelPredictor
{
    private const int ReturnCount = ModelPredictor.ReturnCount;

    /// <summary>Most named float input columns a model may take.</summary>
    public const int MaxNamedFeatures = 16;

    private sealed class Input
    {
        public float[] Features = new float[ReturnCount];
        public float F0, F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12, F13, F14, F15;

        public void Set(int index, float value)
        {
            switch (index)
            {
                case 0: F0 = value; break;
                case 1: F1 = value; break;
                case 2: F2 = value; break;
                case 3: F3 = value; break;
                case 4: F4 = value; break;
                case 5: F5 = value; break;
                case 6: F6 = value; break;
                case 7: F7 = value; break;
                case 8: F8 = value; break;
                case 9: F9 = value; break;
                case 10: F10 = value; break;
                case 11: F11 = value; break;
                case 12: F12 = value; break;
                case 13: F13 = value; break;
                case 14: F14 = value; break;
                case 15: F15 = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }

    private sealed class Output
    {
        public float Probability;
        public bool PredictedLabel;
        public float Score;
    }

    private readonly ITransformer _model;
    private readonly PredictionEngine<Input, Output> _engine;
    private readonly bool _vectorInput;
    private readonly bool _hasProbability;
    private readonly bool _hasLabel;
    private readonly int[]? _featureIndexes;
    // Reused per call: the engine is single-threaded by contract.
    private readonly Input _input = new();
    private Output _output = new();

    private MlNetPredictor(
        ITransformer model, PredictionEngine<Input, Output> engine,
        bool vectorInput, bool hasProbability, bool hasLabel, int[]? featureIndexes)
    {
        _featureIndexes = featureIndexes;
        _model = model;
        _engine = engine;
        _vectorInput = vectorInput;
        _hasProbability = hasProbability;
        _hasLabel = hasLabel;
    }

    public static MlNetPredictor Load(string modelPath)
    {
        using var stream = File.OpenRead(modelPath);
        return Load(stream);
    }

    /// <summary>Loads a model.zip and checks it against the contract.</summary>
    /// <exception cref="InvalidOperationException">The model does not fit the contract.</exception>
    public static MlNetPredictor Load(Stream modelZip)
    {
        var ml = new MLContext(seed: 0);
        // Tree models are a common ML.NET choice; their loaders live outside
        // the core assembly and must be registered before Model.Load.
        ml.ComponentCatalog.RegisterAssembly(typeof(FastTreeBinaryModelParameters).Assembly);

        ITransformer model;
        DataViewSchema? schema;
        model = ml.Model.Load(modelZip, out schema);
        try
        {
            if (schema is null)
                throw new InvalidOperationException(
                    "the ML.NET model was saved without an input schema; pass the training data schema to MLContext.Model.Save");

            var features = schema
                .Where(column => !column.IsHidden && !string.Equals(column.Name, "Label", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var vectorInput = features is [{ Type: VectorDataViewType { ItemType: NumberDataViewType, Size: ReturnCount } vector }]
                && vector.ItemType == NumberDataViewType.Single;
            var allFloats = features.Length > 0 && features.All(column => column.Type == NumberDataViewType.Single);
            var named = allFloats ? features.Select(column => BarFeatures.IndexOf(column.Name)).ToArray() : null;
            int[]? featureIndexes = named is not null && named.All(index => index >= 0)
                && named.Distinct().Count() == named.Length && named.Length <= MaxNamedFeatures
                ? named : null;
            if (!vectorInput && featureIndexes is null && !(allFloats && features.Length == ReturnCount))
                throw new InvalidOperationException(
                    $"the ML.NET model must take either named bar features (float columns named {string.Join(", ", BarFeatures.Names)}) "
                    + $"or {ReturnCount} float returns: one float[{ReturnCount}] vector column or {ReturnCount} float columns "
                    + $"(found: {Describe(features)})");

            var inputDefinition = SchemaDefinition.Create(typeof(Input));
            if (vectorInput)
            {
                inputDefinition.RemoveAll(column => column.MemberName != nameof(Input.Features));
                inputDefinition[0].ColumnName = features[0].Name;
                inputDefinition[0].ColumnType = new VectorDataViewType(NumberDataViewType.Single, ReturnCount);
            }
            else
            {
                inputDefinition.RemoveAll(column => column.MemberName == nameof(Input.Features)
                    || int.Parse(column.MemberName.AsSpan(1)) >= features.Length);
                for (var i = 0; i < features.Length; i++)
                    inputDefinition.Single(column => column.MemberName == $"F{i}").ColumnName = features[i].Name;
            }

            var outputs = model.GetOutputSchema(schema);
            var hasProbability = outputs.GetColumnOrNull("Probability")?.Type == NumberDataViewType.Single;
            var hasLabel = hasProbability && outputs.GetColumnOrNull("PredictedLabel")?.Type == BooleanDataViewType.Instance;
            var hasScore = outputs.GetColumnOrNull("Score")?.Type == NumberDataViewType.Single;
            if (!hasProbability && !hasScore)
                throw new InvalidOperationException(
                    "the ML.NET model has no float `Probability` (binary classifier) or scalar `Score` output");

            var outputDefinition = SchemaDefinition.Create(typeof(Output), SchemaDefinition.Direction.Write);
            outputDefinition.RemoveAll(column => column.MemberName switch
            {
                nameof(Output.Probability) => !hasProbability,
                nameof(Output.PredictedLabel) => !hasLabel,
                _ => hasProbability,
            });

            var engine = ml.Model.CreatePredictionEngine<Input, Output>(
                model, ignoreMissingColumns: false,
                inputSchemaDefinition: inputDefinition, outputSchemaDefinition: outputDefinition);
            var predictor = new MlNetPredictor(model, engine, vectorInput, hasProbability, hasLabel, featureIndexes);
            model = null!; // Owned by the predictor now.
            return predictor;
        }
        finally
        {
            (model as IDisposable)?.Dispose();
        }
    }

    public int FeatureCount => _featureIndexes?.Length ?? ReturnCount;

    public IReadOnlyList<int>? FeatureIndexes => _featureIndexes;

    public ModelOutputKind OutputKind =>
        _featureIndexes is not null && !_hasProbability ? ModelOutputKind.Edge : ModelOutputKind.Probability;

    public ModelPrediction Predict(ReadOnlySpan<float> features)
    {
        if (features.Length != FeatureCount)
            throw new InvalidOperationException(
                $"expected {FeatureCount} features, got {features.Length}");
        foreach (var value in features)
        {
            if (!float.IsFinite(value))
                throw new InvalidOperationException("features must contain only finite numbers");
        }

        if (_vectorInput)
            features.CopyTo(_input.Features);
        else
        {
            for (var i = 0; i < features.Length; i++)
                _input.Set(i, features[i]);
        }
        _engine.Predict(_input, ref _output);

        if (_hasProbability)
        {
            var probabilityUp = _output.Probability;
            if (probabilityUp is not (>= 0.0f and <= 1.0f))
                throw new InvalidOperationException(
                    "ML.NET model returned an invalid positive-class probability");
            var label = _hasLabel ? _output.PredictedLabel : probabilityUp >= 0.5f;
            return new ModelPrediction(label ? 1 : 0, probabilityUp);
        }

        var score = _output.Score;
        if (!float.IsFinite(score))
            throw new InvalidOperationException("ML.NET model returned a non-finite score");
        return new ModelPrediction(score > 0 ? 1 : 0, score > 0 ? 1.0f : score < 0 ? 0.0f : 0.5f, score);
    }

    public void Dispose()
    {
        _engine.Dispose();
        (_model as IDisposable)?.Dispose();
    }

    private static string Describe(IEnumerable<DataViewSchema.Column> columns)
    {
        var text = string.Join(", ", columns.Select(column => $"{column.Name}: {column.Type}"));
        return text.Length > 0 ? text : "none";
    }
}

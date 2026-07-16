namespace AgentLearning.App;

public sealed class EndToEndRagRegressionRunner
{
    private readonly string _artifactFilePath;
    private readonly string _baselineFilePath;
    private readonly string _embeddingModel;
    private readonly EndToEndRagEvaluator _evaluator;
    private readonly string _evaluationFilePath;
    private readonly string _model;

    public EndToEndRagRegressionRunner(
        EndToEndRagEvaluator evaluator,
        string evaluationFilePath,
        string baselineFilePath,
        string artifactFilePath,
        string model,
        string embeddingModel)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentException.ThrowIfNullOrWhiteSpace(evaluationFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(baselineFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(embeddingModel);

        _evaluator = evaluator;
        _evaluationFilePath = evaluationFilePath;
        _baselineFilePath = baselineFilePath;
        _artifactFilePath = artifactFilePath;
        _model = model;
        _embeddingModel = embeddingModel;
    }

    public async Task<EndToEndRagRegressionRunResult> RunAsync(
        CancellationToken cancellationToken = default)
    {
        EndToEndRagEvaluationReport report = await _evaluator.EvaluateAsync(
            _evaluationFilePath,
            cancellationToken);
        await EndToEndRagEvaluationArtifactStore.SaveAsync(
            _artifactFilePath,
            report,
            _model,
            _embeddingModel,
            cancellationToken);
        EndToEndRagRegressionBaseline baseline = await EndToEndRagRegressionBaseline.LoadAsync(
            _baselineFilePath,
            cancellationToken);
        EndToEndRagRegressionGateResult gate = EndToEndRagRegressionGate.Evaluate(
            report,
            baseline);
        return new EndToEndRagRegressionRunResult(report, gate, _artifactFilePath);
    }
}

public sealed record EndToEndRagRegressionRunResult(
    EndToEndRagEvaluationReport Report,
    EndToEndRagRegressionGateResult Gate,
    string ArtifactFilePath);

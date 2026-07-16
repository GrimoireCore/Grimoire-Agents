using AgentLearning.Core.Skills;
using System.Text.Json;

namespace AgentLearning.App;

/// <summary>
/// Runs retrieval-only evaluation cases through the registered knowledge search skill.
/// </summary>
public sealed class KnowledgeRetrievalEvaluator
{
    private readonly AgentSkillRegistry _skillRegistry;

    public KnowledgeRetrievalEvaluator(AgentSkillRegistry skillRegistry)
    {
        ArgumentNullException.ThrowIfNull(skillRegistry);
        _skillRegistry = skillRegistry;
    }

    public async Task<KnowledgeRetrievalEvaluationReport> EvaluateAsync(
        string evaluationFilePath,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<KnowledgeRetrievalEvaluationCase> cases = await LoadCasesAsync(
            evaluationFilePath,
            cancellationToken);
        List<KnowledgeRetrievalEvaluationResult> results = [];

        foreach (KnowledgeRetrievalEvaluationCase evaluationCase in cases)
        {
            string argumentsJson = JsonSerializer.Serialize(new { query = evaluationCase.Question });
            string toolResult = await _skillRegistry.ExecuteAsync(
                "search_knowledge",
                argumentsJson,
                AgentToolExecutionContext.CreateLocalCommand(),
                cancellationToken);
            KnowledgeRetrievalMatch[] matches = KnowledgeSearchToolResultParser.Parse(toolResult);
            results.Add(new KnowledgeRetrievalEvaluationResult(evaluationCase, matches));
        }

        return new KnowledgeRetrievalEvaluationReport(results);
    }

    private static async Task<IReadOnlyList<KnowledgeRetrievalEvaluationCase>> LoadCasesAsync(
        string evaluationFilePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(evaluationFilePath))
        {
            throw new FileNotFoundException(
                "RAG evaluation file was not found.",
                evaluationFilePath);
        }

        await using FileStream stream = File.OpenRead(evaluationFilePath);
        KnowledgeRetrievalEvaluationCase[]? cases = await JsonSerializer.DeserializeAsync<
            KnowledgeRetrievalEvaluationCase[]>(stream, cancellationToken: cancellationToken);
        if (cases is null || cases.Length == 0)
        {
            throw new InvalidOperationException("RAG evaluation file contains no cases.");
        }

        foreach (KnowledgeRetrievalEvaluationCase evaluationCase in cases)
        {
            if (string.IsNullOrWhiteSpace(evaluationCase.Id)
                || string.IsNullOrWhiteSpace(evaluationCase.Question))
            {
                throw new InvalidOperationException(
                    "Every RAG evaluation case requires a non-empty id and question.");
            }
        }

        string[] duplicateIds = cases
            .GroupBy(evaluationCase => evaluationCase.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateIds.Length > 0)
        {
            throw new InvalidOperationException(
                $"RAG evaluation case ids must be unique: {string.Join(", ", duplicateIds)}");
        }

        return cases;
    }
}

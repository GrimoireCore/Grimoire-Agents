using AgentLearning.Core.Skills;
using OpenAI.Chat;
using System.Text.Json;

namespace AgentLearning.App;

/// <summary>
/// Evaluates retrieval, grounded answer generation, citations, and faithfulness as one RAG pipeline.
/// </summary>
public sealed class EndToEndRagEvaluator
{
    private readonly IAgentChatClient _client;
    private readonly GroundednessEvaluator _groundednessEvaluator;
    private readonly AgentSkillRegistry _skillRegistry;

    public EndToEndRagEvaluator(
        AgentSkillRegistry skillRegistry,
        IAgentChatClient client)
    {
        ArgumentNullException.ThrowIfNull(skillRegistry);
        ArgumentNullException.ThrowIfNull(client);
        _skillRegistry = skillRegistry;
        _client = client;
        _groundednessEvaluator = new GroundednessEvaluator(client);
    }

    public async Task<EndToEndRagEvaluationReport> EvaluateAsync(
        string evaluationFilePath,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<KnowledgeRetrievalEvaluationCase> cases = await LoadCasesAsync(
            evaluationFilePath,
            cancellationToken);
        List<GeneratedRagCase> generatedCases = [];

        foreach (KnowledgeRetrievalEvaluationCase evaluationCase in cases)
        {
            string argumentsJson = JsonSerializer.Serialize(new { query = evaluationCase.Question });
            string rawToolResult = await _skillRegistry.ExecuteAsync(
                KnowledgeGroundingPolicy.SearchToolName,
                argumentsJson,
                AgentToolExecutionContext.CreateLocalCommand(),
                cancellationToken);
            KnowledgeRetrievalMatch[] retrievedMatches = KnowledgeSearchToolResultParser.Parse(
                rawToolResult);
            GeneratedAnswer generatedAnswer = await GenerateAnswerAsync(
                evaluationCase.Question,
                rawToolResult);
            generatedCases.Add(new GeneratedRagCase(
                evaluationCase,
                retrievedMatches,
                rawToolResult,
                generatedAnswer.Answer,
                generatedAnswer.CitationValidation,
                generatedAnswer.CitationRepairAttempted));
        }

        GroundednessEvaluationCase[] groundednessCases = generatedCases
            .Select(generatedCase => new GroundednessEvaluationCase(
                generatedCase.Case.Id,
                generatedCase.Case.Question,
                generatedCase.RawToolResult,
                generatedCase.Answer,
                ExpectedGrounded: true))
            .ToArray();
        GroundednessEvaluationReport groundednessReport = await _groundednessEvaluator.EvaluateAsync(
            groundednessCases);

        EndToEndRagEvaluationResult[] results = generatedCases
            .Select(generatedCase => new EndToEndRagEvaluationResult(
                generatedCase.Case,
                generatedCase.RetrievedMatches,
                generatedCase.Answer,
                generatedCase.CitationValidation,
                generatedCase.CitationRepairAttempted,
                groundednessReport.Results
                    .Single(result => result.Case.Id == generatedCase.Case.Id)
                    .Judgment))
            .ToArray();
        return new EndToEndRagEvaluationReport(results);
    }

    private async Task<GeneratedAnswer> GenerateAnswerAsync(
        string question,
        string rawToolResult)
    {
        KnowledgeCitationValidator citationValidator = new();
        citationValidator.RecordSearchResult(rawToolResult);
        string groundedContext = KnowledgeGroundingPolicy.PrepareToolResult(
            KnowledgeGroundingPolicy.SearchToolName,
            rawToolResult);
        List<ChatMessage> messages =
        [
            new SystemChatMessage("Answer the user using the knowledge grounding rules and reference data. Return only the answer."),
            new UserChatMessage($"Question:\n{question}\n\n{groundedContext}")
        ];

        string answer = await CompleteTextAsync(messages);
        KnowledgeCitationValidationResult validation = citationValidator.Validate(answer);
        if (validation.IsValid)
        {
            return new GeneratedAnswer(answer, validation, CitationRepairAttempted: false);
        }

        messages.Add(new AssistantChatMessage(answer));
        messages.Add(new UserChatMessage(citationValidator.BuildRepairInstruction(validation)));
        string repairedAnswer = await CompleteTextAsync(messages);
        KnowledgeCitationValidationResult repairedValidation = citationValidator.Validate(
            repairedAnswer);
        return new GeneratedAnswer(
            repairedAnswer,
            repairedValidation,
            CitationRepairAttempted: true);
    }

    private async Task<string> CompleteTextAsync(IReadOnlyList<ChatMessage> messages)
    {
        ChatCompletion completion = await _client.CompleteChatAsync(messages);
        if (completion.ToolCalls.Count > 0 || completion.FinishReason != ChatFinishReason.Stop)
        {
            throw new InvalidOperationException(
                $"RAG answer generator returned unsupported finish reason: {completion.FinishReason}.");
        }

        string answer = string.Concat(completion.Content.Select(part => part.Text));
        if (string.IsNullOrWhiteSpace(answer))
        {
            throw new InvalidOperationException("RAG answer generator returned no text.");
        }

        return answer.Trim();
    }

    private static async Task<IReadOnlyList<KnowledgeRetrievalEvaluationCase>> LoadCasesAsync(
        string evaluationFilePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(evaluationFilePath))
        {
            throw new FileNotFoundException(
                "End-to-end RAG evaluation file was not found.",
                evaluationFilePath);
        }

        await using FileStream stream = File.OpenRead(evaluationFilePath);
        KnowledgeRetrievalEvaluationCase[]? cases = await JsonSerializer.DeserializeAsync<
            KnowledgeRetrievalEvaluationCase[]>(stream, cancellationToken: cancellationToken);
        if (cases is null || cases.Length == 0)
        {
            throw new InvalidOperationException("End-to-end RAG evaluation file contains no cases.");
        }

        foreach (KnowledgeRetrievalEvaluationCase evaluationCase in cases)
        {
            if (string.IsNullOrWhiteSpace(evaluationCase.Id)
                || string.IsNullOrWhiteSpace(evaluationCase.Question))
            {
                throw new InvalidOperationException(
                    "Every end-to-end RAG evaluation case requires an id and question.");
            }
        }

        if (cases.Select(evaluationCase => evaluationCase.Id)
            .Distinct(StringComparer.Ordinal)
            .Count() != cases.Length)
        {
            throw new InvalidOperationException("End-to-end RAG evaluation case ids must be unique.");
        }

        return cases;
    }

    private sealed record GeneratedAnswer(
        string Answer,
        KnowledgeCitationValidationResult CitationValidation,
        bool CitationRepairAttempted);

    private sealed record GeneratedRagCase(
        KnowledgeRetrievalEvaluationCase Case,
        IReadOnlyList<KnowledgeRetrievalMatch> RetrievedMatches,
        string RawToolResult,
        string Answer,
        KnowledgeCitationValidationResult CitationValidation,
        bool CitationRepairAttempted);
}

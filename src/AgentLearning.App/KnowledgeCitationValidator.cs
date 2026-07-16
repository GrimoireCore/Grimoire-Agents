using System.Text.RegularExpressions;

namespace AgentLearning.App;

/// <summary>
/// Verifies that model citations refer to chunks returned during the current run.
/// </summary>
public sealed class KnowledgeCitationValidator
{
    private static readonly Regex CitationPattern = new(
        @"\[source: (?<source>[^,\]\r\n]+), chunk (?<chunk>\d+)\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly HashSet<KnowledgeSourceReference> _retrievedSources = [];

    public bool SearchWasUsed { get; private set; }

    public void RecordSearchResult(string rawToolResult)
    {
        SearchWasUsed = true;
        foreach (KnowledgeRetrievalMatch match in KnowledgeSearchToolResultParser.Parse(rawToolResult))
        {
            _retrievedSources.Add(new KnowledgeSourceReference(
                match.SourcePath,
                match.ChunkNumber));
        }
    }

    public KnowledgeCitationValidationResult Validate(string answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        if (!SearchWasUsed)
        {
            return KnowledgeCitationValidationResult.Success();
        }

        MatchCollection citationMatches = CitationPattern.Matches(answer);
        int citationStartCount = answer.Split("[source:", StringSplitOptions.None).Length - 1;
        if (citationStartCount != citationMatches.Count)
        {
            return KnowledgeCitationValidationResult.Failure(
                "The answer contains a malformed source citation.");
        }

        if (_retrievedSources.Count == 0)
        {
            return citationMatches.Count == 0
                ? KnowledgeCitationValidationResult.Success()
                : KnowledgeCitationValidationResult.Failure(
                    "The answer cited a source even though knowledge retrieval returned no result.");
        }

        if (citationMatches.Count == 0)
        {
            return KnowledgeCitationValidationResult.Failure(
                "The answer used retrieved knowledge but did not include a source citation.");
        }

        foreach (Match citationMatch in citationMatches)
        {
            KnowledgeSourceReference citation = new(
                citationMatch.Groups["source"].Value,
                int.Parse(citationMatch.Groups["chunk"].Value));
            if (!_retrievedSources.Contains(citation))
            {
                return KnowledgeCitationValidationResult.Failure(
                    $"The answer cited a chunk that was not retrieved: {citation.ToCitation()}.");
            }
        }

        return KnowledgeCitationValidationResult.Success();
    }

    public string BuildRepairInstruction(KnowledgeCitationValidationResult validation)
    {
        ArgumentNullException.ThrowIfNull(validation);
        if (validation.IsValid)
        {
            throw new InvalidOperationException("A valid answer does not need citation repair.");
        }

        string allowedCitations = _retrievedSources.Count == 0
            ? "- No source citation is allowed because retrieval returned no result."
            : string.Join(
                Environment.NewLine,
                _retrievedSources
                    .OrderBy(source => source.SourcePath, StringComparer.Ordinal)
                    .ThenBy(source => source.ChunkNumber)
                    .Select(source => $"- {source.ToCitation()}"));

        return $"""
        HARNESS CITATION VALIDATION FAILED
        Reason: {validation.Error}

        Rewrite your previous answer.
        Use only citations from this allowed list:
        {allowedCitations}
        Return the corrected answer only.
        """;
    }

    private sealed record KnowledgeSourceReference(string SourcePath, int ChunkNumber)
    {
        public string ToCitation() => $"[source: {SourcePath}, chunk {ChunkNumber}]";
    }
}

public sealed record KnowledgeCitationValidationResult(bool IsValid, string? Error)
{
    public static KnowledgeCitationValidationResult Success() => new(true, null);

    public static KnowledgeCitationValidationResult Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new(false, error.Trim());
    }
}

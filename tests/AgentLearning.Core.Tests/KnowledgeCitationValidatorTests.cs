using AgentLearning.App;

namespace AgentLearning.Core.Tests;

public sealed class KnowledgeCitationValidatorTests
{
    [Fact]
    public void Validate_accepts_a_citation_returned_by_the_search_tool()
    {
        KnowledgeCitationValidator validator = CreateValidatorWithResult();

        KnowledgeCitationValidationResult result = validator.Validate(
            "Harness controls execution. [source: agent-harness.md, chunk 1]");

        Assert.True(result.IsValid);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Validate_rejects_missing_and_unretrieved_citations()
    {
        KnowledgeCitationValidator validator = CreateValidatorWithResult();

        KnowledgeCitationValidationResult missing = validator.Validate(
            "Harness controls execution.");
        KnowledgeCitationValidationResult invented = validator.Validate(
            "Harness controls execution. [source: invented.md, chunk 9]");

        Assert.False(missing.IsValid);
        Assert.Contains("did not include", missing.Error, StringComparison.Ordinal);
        Assert.False(invented.IsValid);
        Assert.Contains("was not retrieved", invented.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_rejects_malformed_citation_syntax()
    {
        KnowledgeCitationValidator validator = CreateValidatorWithResult();

        KnowledgeCitationValidationResult result = validator.Validate(
            "Harness controls execution. [source: agent-harness.md chunk 1]");

        Assert.False(result.IsValid);
        Assert.Contains("malformed", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_allows_no_citation_for_no_result_but_rejects_an_invented_one()
    {
        KnowledgeCitationValidator validator = new();
        validator.RecordSearchResult(KnowledgeSearchToolResultParser.NoResultsMessage);

        KnowledgeCitationValidationResult withoutCitation = validator.Validate(
            "The current knowledge base does not contain this answer.");
        KnowledgeCitationValidationResult withCitation = validator.Validate(
            "No answer. [source: invented.md, chunk 1]");

        Assert.True(withoutCitation.IsValid);
        Assert.False(withCitation.IsValid);
        Assert.Contains("no result", withCitation.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRepairInstruction_lists_only_retrieved_citations()
    {
        KnowledgeCitationValidator validator = CreateValidatorWithResult();
        KnowledgeCitationValidationResult validation = validator.Validate("Missing citation.");

        string instruction = validator.BuildRepairInstruction(validation);

        Assert.Contains("HARNESS CITATION VALIDATION FAILED", instruction, StringComparison.Ordinal);
        Assert.Contains("[source: agent-harness.md, chunk 1]", instruction, StringComparison.Ordinal);
    }

    private static KnowledgeCitationValidator CreateValidatorWithResult()
    {
        KnowledgeCitationValidator validator = new();
        validator.RecordSearchResult("""
            Knowledge search results:

            [1] Source: agent-harness.md (chunk 1)
            Scores: combined=0.754, vector=0.734, keyword=0.800
            Harness controls execution.
            """);
        return validator;
    }
}

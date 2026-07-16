using System.Globalization;
using System.Text.RegularExpressions;

namespace AgentLearning.App;

/// <summary>
/// Parses the stable text contract returned by the knowledge search MCP tool.
/// </summary>
public static class KnowledgeSearchToolResultParser
{
    public const string NoResultsMessage = "No relevant knowledge was found.";

    private static readonly Regex ResultPattern = new(
        @"^\[(?<rank>\d+)\] Source: (?<source>.+) \(chunk (?<chunk>\d+)\)\r?\n" +
        @"Scores: combined=(?<combined>-?\d+(?:\.\d+)?), " +
        @"vector=(?<vector>-?\d+(?:\.\d+)?), " +
        @"keyword=(?<keyword>-?\d+(?:\.\d+)?)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    public static KnowledgeRetrievalMatch[] Parse(string toolResult)
    {
        ArgumentNullException.ThrowIfNull(toolResult);
        if (IsNoResult(toolResult))
        {
            return [];
        }

        KnowledgeRetrievalMatch[] matches = ResultPattern
            .Matches(toolResult)
            .Select(match => new KnowledgeRetrievalMatch(
                ParseInt(match, "rank"),
                match.Groups["source"].Value,
                ParseInt(match, "chunk"),
                ParseDouble(match, "combined"),
                ParseDouble(match, "vector"),
                ParseDouble(match, "keyword")))
            .ToArray();

        if (matches.Length == 0)
        {
            throw new InvalidOperationException(
                "Knowledge search output did not contain any parseable ranked results.");
        }

        for (int index = 0; index < matches.Length; index++)
        {
            int expectedRank = index + 1;
            if (matches[index].Rank != expectedRank)
            {
                throw new InvalidOperationException(
                    $"Knowledge search result rank {matches[index].Rank} was out of sequence; expected {expectedRank}.");
            }
        }

        return matches;
    }

    public static bool IsNoResult(string toolResult)
    {
        ArgumentNullException.ThrowIfNull(toolResult);
        return toolResult.Trim().Equals(NoResultsMessage, StringComparison.Ordinal);
    }

    private static int ParseInt(Match match, string groupName)
    {
        return int.Parse(match.Groups[groupName].Value, CultureInfo.InvariantCulture);
    }

    private static double ParseDouble(Match match, string groupName)
    {
        return double.Parse(match.Groups[groupName].Value, CultureInfo.InvariantCulture);
    }
}

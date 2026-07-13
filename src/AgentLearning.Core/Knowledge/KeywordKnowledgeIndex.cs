using System.Text;

namespace AgentLearning.Core.Knowledge;

/// <summary>
/// Loads Markdown documents, splits them into chunks, and performs keyword retrieval.
/// </summary>
public sealed class KeywordKnowledgeIndex
{
    private const int MaximumResultCount = 10;

    private readonly IReadOnlyList<KnowledgeChunk> _chunks;

    private KeywordKnowledgeIndex(IReadOnlyList<KnowledgeChunk> chunks)
    {
        _chunks = chunks;
    }

    public int ChunkCount => _chunks.Count;

    public static async Task<KeywordKnowledgeIndex> LoadFromDirectoryAsync(
        string directoryPath,
        int chunkSize = MarkdownKnowledgeLoader.DefaultChunkSize,
        int chunkOverlap = MarkdownKnowledgeLoader.DefaultChunkOverlap,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<KnowledgeChunk> chunks = await MarkdownKnowledgeLoader.LoadFromDirectoryAsync(
            directoryPath,
            chunkSize,
            chunkOverlap,
            cancellationToken);
        return new KeywordKnowledgeIndex(chunks);
    }

    public IReadOnlyList<KnowledgeSearchResult> Search(string query, int maxResults = 3)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (maxResults is < 1 or > MaximumResultCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxResults),
                $"Result count must be between 1 and {MaximumResultCount}.");
        }

        HashSet<string> queryTokens = Tokenize(query);
        if (queryTokens.Count == 0)
        {
            throw new InvalidOperationException("The knowledge query contains no searchable words.");
        }

        string compactQuery = RemoveWhitespace(query);
        return _chunks
            .Select(chunk => CreateSearchResult(chunk, queryTokens, compactQuery))
            .Where(result => result.Score > 0)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Chunk.SourcePath, StringComparer.Ordinal)
            .ThenBy(result => result.Chunk.ChunkNumber)
            .Take(maxResults)
            .ToArray();
    }

    private static KnowledgeSearchResult CreateSearchResult(
        KnowledgeChunk chunk,
        IReadOnlySet<string> queryTokens,
        string compactQuery)
    {
        HashSet<string> chunkTokens = Tokenize($"{chunk.SourcePath}\n{chunk.Content}");
        int matchingTokenCount = queryTokens.Count(chunkTokens.Contains);
        if (matchingTokenCount == 0)
        {
            return new KnowledgeSearchResult(chunk, 0);
        }

        double coverageScore = matchingTokenCount / (double)queryTokens.Count;
        double exactPhraseBonus = RemoveWhitespace(chunk.Content)
            .Contains(compactQuery, StringComparison.OrdinalIgnoreCase)
            ? 1
            : 0;

        return new KnowledgeSearchResult(
            chunk,
            matchingTokenCount + coverageScore + exactPhraseBonus);
    }

    private static HashSet<string> Tokenize(string text)
    {
        HashSet<string> tokens = new(StringComparer.Ordinal);
        StringBuilder latinWord = new();
        StringBuilder chineseSequence = new();

        foreach (char character in text)
        {
            if (char.IsAsciiLetterOrDigit(character) || character == '_')
            {
                FlushChineseSequence(chineseSequence, tokens);
                latinWord.Append(char.ToLowerInvariant(character));
                continue;
            }

            FlushLatinWord(latinWord, tokens);
            if (IsChineseCharacter(character))
            {
                chineseSequence.Append(character);
            }
            else
            {
                FlushChineseSequence(chineseSequence, tokens);
            }
        }

        FlushLatinWord(latinWord, tokens);
        FlushChineseSequence(chineseSequence, tokens);
        return tokens;
    }

    private static void FlushLatinWord(StringBuilder word, ISet<string> tokens)
    {
        if (word.Length > 0)
        {
            tokens.Add(word.ToString());
            word.Clear();
        }
    }

    private static void FlushChineseSequence(StringBuilder sequence, ISet<string> tokens)
    {
        if (sequence.Length == 1)
        {
            tokens.Add(sequence.ToString());
        }
        else
        {
            for (int index = 0; index < sequence.Length - 1; index++)
            {
                tokens.Add(sequence.ToString(index, 2));
            }
        }

        sequence.Clear();
    }

    private static bool IsChineseCharacter(char character)
    {
        return character is >= '\u4e00' and <= '\u9fff';
    }

    private static string RemoveWhitespace(string text)
    {
        return string.Concat(text.Where(character => !char.IsWhiteSpace(character)));
    }

}

namespace Api.Infrastructure.AI;

/// <summary>
/// Splits a long text into overlapping chunks before embedding. Shared by both
/// document ingestion and web crawling (KnowledgeIngestionService calls this
/// for both) — one chunker, one chunk size, one overlap policy, regardless of
/// where the text came from.
///
/// Token count is approximated by whitespace word count (no tokenizer library
/// is part of this stack — CohereEmbeddingProvider takes raw text, not token
/// ids). English prose averages ~0.75 words per token, so a 500-token target
/// is approximated as ~375 words; we use a slightly more conservative 400
/// words per chunk with an 80-word overlap so we never meaningfully exceed the
/// ~500-token target chunk size the spec calls for.
/// </summary>
public static class TextChunker
{
    public const int DefaultWordsPerChunk = 400;   // ≈500 tokens
    public const int DefaultWordOverlap = 80;      // ≈50-100 tokens, per spec's "50-token overlap"

    /// <summary>
    /// Splits <paramref name="text"/> into chunks of ~<paramref name="wordsPerChunk"/>
    /// words with <paramref name="wordOverlap"/> words repeated at the start of the
    /// next chunk, so a sentence that falls on a chunk boundary is captured in both.
    /// Returns a single chunk (even if empty) for short text — never an empty list
    /// for non-empty input.
    /// </summary>
    public static List<string> Chunk(
        string text,
        int wordsPerChunk = DefaultWordsPerChunk,
        int wordOverlap = DefaultWordOverlap)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return [];

        if (words.Length <= wordsPerChunk)
            return [text.Trim()];

        var chunks = new List<string>();
        var step = Math.Max(1, wordsPerChunk - wordOverlap);

        for (var start = 0; start < words.Length; start += step)
        {
            var length = Math.Min(wordsPerChunk, words.Length - start);
            chunks.Add(string.Join(' ', words.Skip(start).Take(length)));

            if (start + length >= words.Length) break;
        }

        return chunks;
    }
}

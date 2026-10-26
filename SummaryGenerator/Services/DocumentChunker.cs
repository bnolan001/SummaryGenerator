namespace SummaryGenerator.Services
{
    /// <summary>
    /// Pure, token-budget-aware helpers for splitting a document into map-reduce chunks
    /// and for grouping partial summaries into prompt-budget-sized batches during the
    /// reduce step. Token counting is injected, so this is fully unit testable.
    /// </summary>
    public static class DocumentChunker
    {
        public static List<string> SplitIntoChunks(string text, int maxChunkChars)
        {
            var chunks = new List<string>();
            var i = 0;

            while (i < text.Length)
            {
                var length = Math.Min(maxChunkChars, text.Length - i);

                if (i + length < text.Length)
                {
                    var searchStartIndex = i + length - 1;
                    var lastDoubleNewline = text.LastIndexOf("\n\n", searchStartIndex, length, StringComparison.Ordinal);

                    if (lastDoubleNewline > i)
                    {
                        length = lastDoubleNewline - i + 2;
                    }
                    else
                    {
                        var lastPeriod = text.LastIndexOf(". ", searchStartIndex, length, StringComparison.Ordinal);
                        if (lastPeriod > i)
                        {
                            length = lastPeriod - i + 1;
                        }
                    }
                }

                chunks.Add(text.Substring(i, length));
                i += length;
            }

            return chunks;
        }

        public static List<string> SplitToTokenBudget(string text, Func<string, int> countTokens, int maxTokens)
        {
            var tokens = countTokens(text);
            if (tokens <= maxTokens)
            {
                return [text];
            }

            if (text.Length <= 1)
            {
                throw new InvalidOperationException(
                    $"A single character exceeds the {maxTokens}-token budget; the prompt template leaves no room for content.");
            }

            // Cut the text into small units (~1/32 of a chunk) and pack them greedily, so each chunk
            // fills its budget instead of leaving a small leftover fragment that costs a full model call.
            // Additive unit counts can drift from the real tokenizer at unit boundaries, so each packed
            // chunk is verified and, if over budget, gives its last units back to the next chunk.
            var overhead = countTokens(string.Empty);
            var unitChars = (int)Math.Max(1, (long)text.Length * maxTokens / tokens / 32);
            var units = SplitIntoChunks(text, unitChars);
            var unitTokens = units.Select(unit => countTokens(unit) - overhead).ToList();
            var contentBudget = maxTokens - overhead;

            var result = new List<string>();
            var start = 0;
            while (start < units.Count)
            {
                var end = start;
                var estimate = 0;
                while (end < units.Count && (end == start || estimate + unitTokens[end] <= contentBudget))
                {
                    estimate += unitTokens[end];
                    end++;
                }

                while (true)
                {
                    var chunk = string.Concat(units.Skip(start).Take(end - start));
                    if (countTokens(chunk) <= maxTokens)
                    {
                        result.Add(chunk);
                        break;
                    }

                    if (end - start == 1)
                    {
                        result.AddRange(SplitToTokenBudget(chunk, countTokens, maxTokens));
                        break;
                    }

                    end--;
                }

                start = end;
            }

            return result;
        }

        public static List<List<T>> GroupByBudget<T>(IReadOnlyList<T> items, Func<T, int> sizeSelector, int maxSizePerGroup)
        {
            var groups = new List<List<T>>();
            var currentGroup = new List<T>();
            var currentSize = 0;

            foreach (var item in items)
            {
                var itemSize = sizeSelector(item);

                if (currentGroup.Count > 0 && currentSize + itemSize > maxSizePerGroup)
                {
                    groups.Add(currentGroup);
                    currentGroup = [];
                    currentSize = 0;
                }

                currentGroup.Add(item);
                currentSize += itemSize;
            }

            if (currentGroup.Count > 0)
            {
                groups.Add(currentGroup);
            }

            return groups;
        }
    }
}

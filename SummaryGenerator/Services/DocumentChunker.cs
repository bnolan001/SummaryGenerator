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
            var overhead = countTokens(string.Empty);
            var unitChars = (int)Math.Max(1, (long)text.Length * maxTokens / tokens / 32);
            var units = SplitIntoChunks(text, unitChars);
            var groups = GroupByBudget(units, unit => countTokens(unit) - overhead, maxTokens - overhead);

            var result = new List<string>();
            foreach (var group in groups)
            {
                AddWithinBudget(group, countTokens, maxTokens, result);
            }

            return result;
        }

        // Additive unit counts can drift slightly from the real tokenizer at unit boundaries, so each
        // packed chunk is verified; an over-budget group is halved (always smaller, so this terminates).
        private static void AddWithinBudget(List<string> group, Func<string, int> countTokens, int maxTokens, List<string> result)
        {
            var chunk = string.Concat(group);
            if (countTokens(chunk) <= maxTokens)
            {
                result.Add(chunk);
            }
            else if (group.Count == 1)
            {
                result.AddRange(SplitToTokenBudget(chunk, countTokens, maxTokens));
            }
            else
            {
                var half = group.Count / 2;
                AddWithinBudget(group[..half], countTokens, maxTokens, result);
                AddWithinBudget(group[half..], countTokens, maxTokens, result);
            }
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

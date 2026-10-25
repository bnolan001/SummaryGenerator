namespace SummaryGenerator.Services
{
    /// <summary>
    /// Pure, context-size-aware helpers for splitting a document into map-reduce chunks
    /// and for grouping partial summaries into prompt-budget-sized batches during the
    /// reduce step. No native/LLM calls happen here, so this is fully unit testable.
    /// </summary>
    public static class DocumentChunker
    {
        private const double CharsPerToken = 3.3;

        public static int EstimateCharBudget(uint contextSize, uint reserveTokens, int minimumChars = 4000)
        {
            var availableTokens = (long)contextSize - reserveTokens;
            if (availableTokens <= 0)
            {
                return minimumChars;
            }

            var budget = (int)(availableTokens * CharsPerToken);
            return Math.Max(budget, minimumChars);
        }

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

        public static List<List<T>> GroupByCharBudget<T>(IReadOnlyList<T> items, Func<T, int> lengthSelector, int maxCharsPerGroup)
        {
            var groups = new List<List<T>>();
            var currentGroup = new List<T>();
            var currentLength = 0;

            foreach (var item in items)
            {
                var itemLength = lengthSelector(item);

                if (currentGroup.Count > 0 && currentLength + itemLength > maxCharsPerGroup)
                {
                    groups.Add(currentGroup);
                    currentGroup = [];
                    currentLength = 0;
                }

                currentGroup.Add(item);
                currentLength += itemLength;
            }

            if (currentGroup.Count > 0)
            {
                groups.Add(currentGroup);
            }

            return groups;
        }
    }
}

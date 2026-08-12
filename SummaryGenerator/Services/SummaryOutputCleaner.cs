namespace SummaryGenerator.Services
{
    public static class SummaryOutputCleaner
    {
        public static string Clean(string rawOutput, IEnumerable<string> stopPhrases)
        {
            if (string.IsNullOrWhiteSpace(rawOutput))
            {
                return string.Empty;
            }

            var cleaned = rawOutput.Trim();
            var stopIndex = FindFirstStopPhraseIndex(cleaned, stopPhrases);
            if (stopIndex >= 0)
            {
                cleaned = cleaned[..stopIndex].TrimEnd();
            }

            return cleaned.Trim();
        }

        private static int FindFirstStopPhraseIndex(string text, IEnumerable<string> stopPhrases)
        {
            var firstIndex = -1;

            foreach (var phrase in stopPhrases.Where(phrase => !string.IsNullOrWhiteSpace(phrase)))
            {
                var index = text.IndexOf(phrase, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    continue;
                }

                if (firstIndex < 0 || index < firstIndex)
                {
                    firstIndex = index;
                }
            }

            return firstIndex;
        }
    }
}

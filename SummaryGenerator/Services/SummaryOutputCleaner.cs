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

            var cleaned = RemoveLeadingModelControlLines(rawOutput.Trim());
            var stopIndex = FindFirstStopPhraseIndex(cleaned, stopPhrases);
            if (stopIndex >= 0)
            {
                cleaned = cleaned[..stopIndex].TrimEnd();
            }

            return cleaned.Trim();
        }

        private static string RemoveLeadingModelControlLines(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var normalized = text.Replace("\r\n", "\n");
            var lines = normalized.Split('\n');
            var index = 0;

            while (index < lines.Length && IsModelControlLine(lines[index]))
            {
                index++;
            }

            while (index < lines.Length && string.IsNullOrWhiteSpace(lines[index]))
            {
                index++;
            }

            return index == 0
                ? text
                : string.Join('\n', lines[index..]);
        }

        private static bool IsModelControlLine(string line)
        {
            var trimmed = line.TrimStart();
            return trimmed.StartsWith("<|", StringComparison.Ordinal);
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

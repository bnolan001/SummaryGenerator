using System.Text.RegularExpressions;

namespace SummaryGenerator.Services
{
    public static partial class SummaryOutputCleaner
    {
        public static string Clean(string rawOutput, IEnumerable<string> stopPhrases)
        {
            if (string.IsNullOrWhiteSpace(rawOutput))
            {
                return string.Empty;
            }

            var cleaned = RemoveReasoningArtifacts(rawOutput);
            cleaned = RemoveLeadingModelControlLines(cleaned.Trim());
            var stopIndex = FindFirstStopPhraseIndex(cleaned, stopPhrases);
            if (stopIndex >= 0)
            {
                cleaned = cleaned[..stopIndex].TrimEnd();
            }

            return cleaned.Trim();
        }

        // The prompt asks for a <scratchpad> to improve reasoning, reasoning models (Phi-4 Mini)
        // emit <think> blocks, and Gemma 4 can emit thought-channel tokens; none belong in the saved
        // notes. Never delete a Markdown heading line: a closed block is removed only when it
        // contains no heading.
        private static string RemoveReasoningArtifacts(string text)
        {
            var cleaned = GemmaThoughtChannelRegex().Replace(text, string.Empty);
            cleaned = GemmaChannelTokenRegex().Replace(cleaned, string.Empty);
            cleaned = ReasoningBlockRegex().Replace(cleaned, string.Empty);
            cleaned = UnclosedReasoningBeforeHeadingRegex().Replace(cleaned, string.Empty);
            return ReasoningTagRegex().Replace(cleaned, string.Empty);
        }

        [GeneratedRegex(@"<\|channel>.*?<channel\|>", RegexOptions.Singleline)]
        private static partial Regex GemmaThoughtChannelRegex();

        [GeneratedRegex(@"<\|channel>|<channel\|>")]
        private static partial Regex GemmaChannelTokenRegex();

        [GeneratedRegex(@"<(scratchpad|think)>(?:(?!^[ \t]*#).)*?</\1>", RegexOptions.Singleline | RegexOptions.Multiline | RegexOptions.IgnoreCase)]
        private static partial Regex ReasoningBlockRegex();

        [GeneratedRegex(@"<(?:scratchpad|think)>.*?(?=^[ \t]*#)", RegexOptions.Singleline | RegexOptions.Multiline | RegexOptions.IgnoreCase)]
        private static partial Regex UnclosedReasoningBeforeHeadingRegex();

        [GeneratedRegex(@"</?(?:scratchpad|think)>", RegexOptions.IgnoreCase)]
        private static partial Regex ReasoningTagRegex();

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

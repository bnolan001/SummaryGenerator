using SummaryGenerator.Services;

namespace SummaryGenerator.Tests.Unit
{
    public class SummaryOutputCleanerTests
    {
        [Fact]
        public void Clean_RemovesTrailingContentFromFirstStopPhrase()
        {
            var raw = "## Executive Summary\nBody\nThe final answer is: extra text";
            var cleaned = SummaryOutputCleaner.Clean(raw, ["The final answer is:", "User:"]);

            Assert.Equal("## Executive Summary\nBody", cleaned);
        }

        [Fact]
        public void Clean_MatchesStopPhraseCaseInsensitively()
        {
            var raw = "Summary content\ni HoPe ThiS MeEtS YoUr ReQuIrEmEnTs and more";
            var cleaned = SummaryOutputCleaner.Clean(raw, ["I hope this meets your requirements"]);

            Assert.Equal("Summary content", cleaned);
        }

        [Fact]
        public void Clean_ReturnsTrimmedTextWhenNoStopPhraseFound()
        {
            var raw = "   Summary content only   ";
            var cleaned = SummaryOutputCleaner.Clean(raw, ["The final answer is:"]);

            Assert.Equal("Summary content only", cleaned);
        }
    }
}

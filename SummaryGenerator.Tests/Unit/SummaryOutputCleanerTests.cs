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

        [Fact]
        public void Clean_RemovesLeadingModelControlLine()
        {
            var raw = "<|channel|>analysis<|channel|>\n\n## Executive Summary\nBody";
            var cleaned = SummaryOutputCleaner.Clean(raw, []);

            Assert.Equal("## Executive Summary\nBody", cleaned);
        }

        [Fact]
        public void Clean_RemovesScratchpadBlockAndGemmaChannelToken()
        {
            var raw = "<channel|><scratchpad>\n- Theme 1: x\n- Theme 2: y\n</scratchpad>\n\n# 1. Executive Summary\nBody";
            var cleaned = SummaryOutputCleaner.Clean(raw, []);

            Assert.Equal("# 1. Executive Summary\nBody", cleaned);
        }

        [Fact]
        public void Clean_RemovesScratchpadBlockCaseInsensitively()
        {
            var raw = "<Scratchpad>notes</SCRATCHPAD>\n# 1. Executive Summary\nBody";
            var cleaned = SummaryOutputCleaner.Clean(raw, []);

            Assert.Equal("# 1. Executive Summary\nBody", cleaned);
        }

        [Fact]
        public void Clean_RemovesUnclosedScratchpadUpToFirstHeading()
        {
            var raw = "<scratchpad>\n- Theme 1: x\n\n# 1. Executive Summary\nBody";
            var cleaned = SummaryOutputCleaner.Clean(raw, []);

            Assert.Equal("# 1. Executive Summary\nBody", cleaned);
        }

        [Fact]
        public void Clean_KeepsUnclosedScratchpadContentWhenNoHeadingFollows()
        {
            var raw = "<scratchpad>\nOnly content the model produced";
            var cleaned = SummaryOutputCleaner.Clean(raw, []);

            Assert.Equal("Only content the model produced", cleaned);
        }

        [Fact]
        public void Clean_RemovesGemmaThoughtChannelBlock()
        {
            var raw = "<|channel>thought\nprivate reasoning<channel|># 1. Executive Summary\nBody";
            var cleaned = SummaryOutputCleaner.Clean(raw, []);

            Assert.Equal("# 1. Executive Summary\nBody", cleaned);
        }
    }
}

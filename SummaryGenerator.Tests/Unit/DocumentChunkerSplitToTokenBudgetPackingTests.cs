using SummaryGenerator.Services;

namespace SummaryGenerator.Tests.Unit
{
    public class DocumentChunkerSplitToTokenBudgetPackingTests
    {
        [Fact]
        public void SplitToTokenBudget_WithTemplateOverhead_ProducesNearMinimalChunkCount()
        {
            const int overhead = 300;
            const int maxTokens = 14272;
            var paragraph = string.Concat(Enumerable.Repeat("The battalion crossed the river at dawn under fire. ", 60)) + "\n\n";
            var text = string.Concat(Enumerable.Repeat(paragraph, 400_000 / paragraph.Length));

            var chunks = DocumentChunker.SplitToTokenBudget(text, s => overhead + s.Length, maxTokens);

            var minimalChunkCount = (int)Math.Ceiling(text.Length / (double)(maxTokens - overhead));
            Assert.All(chunks, chunk => Assert.True(overhead + chunk.Length <= maxTokens));
            Assert.True(chunks.Count <= minimalChunkCount + 1, $"{chunks.Count} chunks; minimal is {minimalChunkCount}");
        }

        [Fact]
        public void SplitToTokenBudget_UnitCountsUnderestimateWholeChunk_StillFitsBudget()
        {
            const int maxTokens = 14272;
            var text = string.Concat(Enumerable.Repeat("The battalion crossed the river at dawn under fire. ", 8_000));

            // Concatenations cost more than the sum of their parts, so packed groups overflow and must be halved.
            static int Count(string s) => 300 + s.Length + (s.Length > 2000 ? 400 : 0);

            var chunks = DocumentChunker.SplitToTokenBudget(text, Count, maxTokens);

            Assert.All(chunks, chunk => Assert.True(Count(chunk) <= maxTokens));
            Assert.Equal(text, string.Concat(chunks));

            // An over-budget group must give back only its last units, not be cut in half.
            var minimalChunkCount = (int)Math.Ceiling(text.Length / (double)(maxTokens - 300 - 400));
            Assert.True(chunks.Count <= minimalChunkCount + 1, $"{chunks.Count} chunks; minimal is {minimalChunkCount}");
        }
    }
}

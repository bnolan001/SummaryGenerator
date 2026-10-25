using SummaryGenerator.Services;

namespace SummaryGenerator.Tests.Unit
{
    public class DocumentChunkerEstimateCharBudgetTests
    {
        [Fact]
        public void EstimateCharBudget_SubtractsReserveThenScalesByCharsPerToken()
        {
            // (16384 - 2048) tokens available * 3.3 chars/token
            var budget = DocumentChunker.EstimateCharBudget(contextSize: 16384, reserveTokens: 2048);

            Assert.Equal((int)((16384 - 2048) * 3.3), budget);
        }

        [Fact]
        public void EstimateCharBudget_NeverGoesBelowMinimumChars()
        {
            var budget = DocumentChunker.EstimateCharBudget(contextSize: 4096, reserveTokens: 3900, minimumChars: 4000);

            Assert.Equal(4000, budget);
        }

        [Fact]
        public void EstimateCharBudget_ReserveLargerThanContext_ClampsToMinimumChars()
        {
            var budget = DocumentChunker.EstimateCharBudget(contextSize: 2048, reserveTokens: 4096, minimumChars: 4000);

            Assert.Equal(4000, budget);
        }
    }
}

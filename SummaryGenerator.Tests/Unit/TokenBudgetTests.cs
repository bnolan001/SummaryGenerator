using SummaryGenerator.Services;

namespace SummaryGenerator.Tests.Unit
{
    public class TokenBudgetTests
    {
        [Fact]
        public void Compute_MinimumContext_ClampsOutputsSoTwoSummariesFit()
        {
            var budget = TokenBudget.Compute(contextSize: 4096, maxOutputTokens: 2048, promptOverheadTokens: 200);

            Assert.Equal(1916, budget.FinalOutputTokens);
            Assert.Equal(1269, budget.IntermediateOutputTokens);
            Assert.Equal(2116, budget.MaxFinalPromptTokens);
            Assert.Equal(2763, budget.MaxIntermediatePromptTokens);
        }

        [Fact]
        public void Compute_LargeContext_KeepsConfiguredMaxOutput()
        {
            var budget = TokenBudget.Compute(contextSize: 16384, maxOutputTokens: 2048, promptOverheadTokens: 200);

            Assert.Equal(2048, budget.FinalOutputTokens);
            Assert.Equal(2048, budget.IntermediateOutputTokens);
        }

        [Theory]
        [InlineData(4096u)]
        [InlineData(8192u)]
        [InlineData(16384u)]
        [InlineData(32768u)]
        public void Compute_Invariants_HoldForAllTiers(uint contextSize)
        {
            const int overhead = 300;

            var budget = TokenBudget.Compute(contextSize, maxOutputTokens: 2048, promptOverheadTokens: overhead);

            Assert.True(overhead + (2 * budget.IntermediateOutputTokens) + 16 <= budget.MaxIntermediatePromptTokens);
            Assert.True(overhead + budget.IntermediateOutputTokens <= budget.MaxFinalPromptTokens);
        }

        [Fact]
        public void Compute_ContextTooSmallForTemplate_Throws()
        {
            Assert.Throws<InvalidOperationException>(
                () => TokenBudget.Compute(contextSize: 512, maxOutputTokens: 2048, promptOverheadTokens: 300));
        }
    }
}

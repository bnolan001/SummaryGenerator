using SummaryGenerator.Services;

namespace SummaryGenerator.Tests.Unit
{
    public class DocumentChunkerGroupByBudgetTests
    {
        [Fact]
        public void GroupByBudget_AllItemsFitTogether_ReturnsSingleGroup()
        {
            var items = new[] { "aaa", "bbb", "ccc" };

            var groups = DocumentChunker.GroupByBudget(items, s => s.Length, maxSizePerGroup: 100);

            var expected = new List<List<string>> { items.ToList() };
            Assert.Equal(expected, groups);
        }

        [Fact]
        public void GroupByBudget_SplitsIntoMultipleGroups_WhenBudgetExceeded()
        {
            // Each item is 5 chars; budget of 12 fits 2 per group, never 3.
            var items = new[] { "aaaaa", "bbbbb", "ccccc", "ddddd", "eeeee" };

            var groups = DocumentChunker.GroupByBudget(items, s => s.Length, maxSizePerGroup: 12);

            Assert.All(groups, group => Assert.True(group.Sum(s => s.Length) <= 12));
            // No regression possible: every item must still appear, in order.
            Assert.Equal(items, groups.SelectMany(g => g));
        }

        [Fact]
        public void GroupByBudget_ProducesFewerGroupsThanItems_WhenAtLeastTwoFitTogether()
        {
            var items = new[] { "aaaaa", "bbbbb", "ccccc", "ddddd", "eeeee" };

            var groups = DocumentChunker.GroupByBudget(items, s => s.Length, maxSizePerGroup: 12);

            Assert.True(groups.Count < items.Length, "Grouping made no progress toward reducing item count.");
        }

        [Fact]
        public void GroupByBudget_SingleOversizedItem_BecomesItsOwnGroup()
        {
            var items = new[] { "short", new string('x', 50) };

            var groups = DocumentChunker.GroupByBudget(items, s => s.Length, maxSizePerGroup: 12);

            Assert.Contains(groups, g => g.Count == 1 && g[0] == new string('x', 50));
        }
    }
}

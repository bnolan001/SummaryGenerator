using SummaryGenerator.Models;
using SummaryGenerator.Services;

namespace SummaryGenerator.Tests.Unit
{
    public class ModelRecommenderTests
    {
        private const long Gb = 1024L * 1024 * 1024;

        private static readonly ModelDetails[] Catalog =
        [
            new() { Name = "Phi-4 Mini (3.8B)", ApproxSizeGb = 2.3 },
            new() { Name = "Gemma 4 (8B)", ApproxSizeGb = 4.6 },
            new() { Name = "Gemma 4 (12B)", ApproxSizeGb = 6.6 },
            new() { Name = "Qwen 2.5 (14B)", ApproxSizeGb = 8.4 },
            new() { Name = "Gemma 4 (26B MoE)", ApproxSizeGb = 15.8 }
        ];

        [Fact]
        public void Recommend_PicksLargestModelWithinRamShare()
        {
            // 32 GB * 40% = 12.8 GB
            var model = ModelRecommender.Recommend(Catalog, 32 * Gb, maxModelPercentOfRam: 40);

            Assert.Equal("Qwen 2.5 (14B)", model?.Name);
        }

        [Fact]
        public void Recommend_SmallerMachine_PicksSmallerModel()
        {
            // 16 GB * 40% = 6.4 GB
            var model = ModelRecommender.Recommend(Catalog, 16 * Gb, maxModelPercentOfRam: 40);

            Assert.Equal("Gemma 4 (8B)", model?.Name);
        }

        [Fact]
        public void Recommend_NothingFits_ReturnsNull()
        {
            var model = ModelRecommender.Recommend(Catalog, 4 * Gb, maxModelPercentOfRam: 40);

            Assert.Null(model);
        }

        [Fact]
        public void Recommend_IgnoresModelsWithoutSize()
        {
            ModelDetails[] models = [new() { Name = "Unknown size" }, new() { Name = "Small", ApproxSizeGb = 1.0 }];

            var model = ModelRecommender.Recommend(models, 32 * Gb, maxModelPercentOfRam: 40);

            Assert.Equal("Small", model?.Name);
        }
    }
}

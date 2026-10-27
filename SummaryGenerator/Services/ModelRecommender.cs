using SummaryGenerator.Models;

namespace SummaryGenerator.Services
{
    /// <summary>
    /// Picks the largest catalog model that fits comfortably in this machine's RAM: the same
    /// share auto-tune uses to decide whether a model runs fully on a modern integrated GPU.
    /// </summary>
    public static class ModelRecommender
    {
        private const double BytesPerGb = 1024.0 * 1024 * 1024;

        public static ModelDetails? Recommend(IEnumerable<ModelDetails> models, long totalMemoryBytes, int maxModelPercentOfRam)
        {
            var limitGb = totalMemoryBytes / BytesPerGb * maxModelPercentOfRam / 100;

            return models
                .Where(model => model.ApproxSizeGb is > 0 && model.ApproxSizeGb <= limitGb)
                .MaxBy(model => model.ApproxSizeGb);
        }
    }
}

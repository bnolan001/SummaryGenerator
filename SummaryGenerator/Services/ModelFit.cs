using SummaryGenerator.Models;

namespace SummaryGenerator.Services
{
    /// <summary>
    /// Whether a catalog model fits comfortably in this machine's RAM: the same share auto-tune
    /// uses to decide whether a model runs fully on a modern integrated GPU.
    /// </summary>
    public static class ModelFit
    {
        private const double BytesPerGb = 1024.0 * 1024 * 1024;

        public static bool? FitsThisPc(ModelDetails model, long totalMemoryBytes, int maxModelPercentOfRam)
        {
            if (model.ApproxSizeGb is not > 0)
            {
                return null;
            }

            return model.ApproxSizeGb <= totalMemoryBytes / BytesPerGb * maxModelPercentOfRam / 100;
        }

        // Only on a modern integrated GPU does "too large" mean auto-tune keeps the model on the CPU.
        public static string TooLargeLabel(GpuTier gpuTier) =>
            gpuTier == GpuTier.ModernIntegrated ? "Too large – CPU only" : "Too large for this PC";
    }
}

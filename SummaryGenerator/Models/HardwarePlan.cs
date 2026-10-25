namespace SummaryGenerator.Models
{
    /// <summary>
    /// Detected hardware characteristics and the inference parameters recommended for them.
    /// </summary>
    public record HardwarePlan(
        int LogicalCores,
        long TotalMemoryBytes,
        GpuTier GpuTier,
        int RecommendedThreads,
        int RecommendedGpuLayerCount,
        uint RecommendedContextSize,
        int RecommendedBatchSize);
}

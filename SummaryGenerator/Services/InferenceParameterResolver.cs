using SummaryGenerator.Models;

namespace SummaryGenerator.Services
{
    public static class InferenceParameterResolver
    {
        public static int ResolveThreads(int configuredThreads, HardwarePlan plan)
        {
            return configuredThreads > 0 ? configuredThreads : plan.RecommendedThreads;
        }

        public static int ResolveGpuLayerCount(int configuredGpuLayerCount, bool hardwareAutoTune, HardwarePlan plan)
        {
            if (configuredGpuLayerCount >= 0)
            {
                return configuredGpuLayerCount;
            }

            return hardwareAutoTune ? plan.RecommendedGpuLayerCount : 0;
        }

        public static int ResolveBatchSize(int configuredBatchSize, bool hardwareAutoTune, HardwarePlan plan)
        {
            if (configuredBatchSize > 0)
            {
                return configuredBatchSize;
            }

            return hardwareAutoTune ? plan.RecommendedBatchSize : 0;
        }

        private const uint MaxSafeContextSize = 32768;

        public static uint ResolveContextSize(SummarizationOptions options, ModelDetails modelDetails, bool hardwareAutoTune, HardwarePlan plan)
        {
            var desired = modelDetails.PreferredContextSize.GetValueOrDefault(options.ContextSize);

            if (hardwareAutoTune)
            {
                desired = Math.Min(desired, plan.RecommendedContextSize);
            }

            if (desired < options.MinimumContextSize)
            {
                desired = options.MinimumContextSize;
            }

            // Hard cap to prevent native crashes due to VRAM/RAM exhaustion
            if (desired > MaxSafeContextSize)
            {
                desired = MaxSafeContextSize;
            }

            if (modelDetails.MaxContextSize is > 0)
            {
                var maxAllowed = modelDetails.MaxContextSize.Value;
                if (options.ContextSafetyReserveTokens > 0 && options.ContextSafetyReserveTokens < maxAllowed)
                {
                    maxAllowed -= options.ContextSafetyReserveTokens;
                }

                if (maxAllowed > 0 && desired > maxAllowed)
                {
                    desired = maxAllowed;
                }
            }

            return desired;
        }
    }
}

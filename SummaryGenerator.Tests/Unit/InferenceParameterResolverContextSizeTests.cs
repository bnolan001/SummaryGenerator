using SummaryGenerator.Models;
using SummaryGenerator.Services;

namespace SummaryGenerator.Tests.Unit
{
    public class InferenceParameterResolverContextSizeTests
    {
        private static HardwarePlan PlanWithRecommendedContextSize(uint recommendedContextSize) => new(
            LogicalCores: 8,
            TotalMemoryBytes: 8L * 1024 * 1024 * 1024,
            GpuTier: GpuTier.None,
            RecommendedThreads: 6,
            RecommendedGpuLayerCount: 0,
            RecommendedContextSize: recommendedContextSize,
            RecommendedBatchSize: 256);

        private static SummarizationOptions DefaultOptions() => new()
        {
            ContextSize = 8192,
            MinimumContextSize = 4096,
            ContextSafetyReserveTokens = 2048
        };

        [Fact]
        public void ResolveContextSize_AutoTuneOff_IgnoresHardwareRecommendation()
        {
            var options = DefaultOptions();
            var modelDetails = new ModelDetails { PreferredContextSize = 16384 };
            var plan = PlanWithRecommendedContextSize(recommendedContextSize: 4096);

            var result = InferenceParameterResolver.ResolveContextSize(options, modelDetails, hardwareAutoTune: false, plan);

            Assert.Equal(16384u, result);
        }

        [Fact]
        public void ResolveContextSize_AutoTuneOn_CapsPreferredSizeWithRecommendation()
        {
            var options = DefaultOptions();
            var modelDetails = new ModelDetails { PreferredContextSize = 32768 };
            var plan = PlanWithRecommendedContextSize(recommendedContextSize: 4096);

            var result = InferenceParameterResolver.ResolveContextSize(options, modelDetails, hardwareAutoTune: true, plan);

            Assert.Equal(4096u, result);
        }

        [Fact]
        public void ResolveContextSize_HardwareCapBelowMinimum_MinimumContextSizeWins()
        {
            var options = DefaultOptions();
            var modelDetails = new ModelDetails { PreferredContextSize = 32768 };
            var plan = PlanWithRecommendedContextSize(recommendedContextSize: 2048);

            var result = InferenceParameterResolver.ResolveContextSize(options, modelDetails, hardwareAutoTune: true, plan);

            Assert.Equal(4096u, result);
        }

        [Fact]
        public void ResolveContextSize_NoPreferredContextSize_FallsBackToOptionsContextSize()
        {
            var options = DefaultOptions();
            var modelDetails = new ModelDetails { PreferredContextSize = null };
            var plan = PlanWithRecommendedContextSize(recommendedContextSize: 32768);

            var result = InferenceParameterResolver.ResolveContextSize(options, modelDetails, hardwareAutoTune: false, plan);

            Assert.Equal(8192u, result);
        }

        [Fact]
        public void ResolveContextSize_ModelMaxContextSizeReserve_StillClampsAfterHardwareCap()
        {
            var options = DefaultOptions();
            var modelDetails = new ModelDetails { PreferredContextSize = 32768, MaxContextSize = 8000 };
            var plan = PlanWithRecommendedContextSize(recommendedContextSize: 16384);

            var result = InferenceParameterResolver.ResolveContextSize(options, modelDetails, hardwareAutoTune: true, plan);

            // maxAllowed = 8000 - ContextSafetyReserveTokens(2048) = 5952
            Assert.Equal(5952u, result);
        }
    }
}

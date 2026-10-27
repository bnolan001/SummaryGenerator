using SummaryGenerator.Models;
using SummaryGenerator.Services;

namespace SummaryGenerator.Tests.Unit
{
    public class InferenceParameterResolverSentinelTests
    {
        private static readonly HardwarePlan Plan = new(
            LogicalCores: 8,
            TotalMemoryBytes: 32L * 1024 * 1024 * 1024,
            GpuTier: GpuTier.Discrete,
            RecommendedThreads: 6,
            RecommendedGpuLayerCount: 32,
            RecommendedContextSize: 16384,
            RecommendedBatchSize: 512);

        private const long Gb = 1024L * 1024 * 1024;
        private const long SmallModelBytes = 3 * Gb;
        private const int MaxModelPercentOfRam = 40;

        private static readonly HardwarePlan ModernIntegratedPlan = Plan with
        {
            GpuTier = GpuTier.ModernIntegrated,
            RecommendedGpuLayerCount = HardwareProfileService.AllGpuLayers
        };

        [Fact]
        public void ResolveGpuLayerCount_ModernIntegrated_ModelWithinGuard_OffloadsAllLayers()
        {
            // 32 GB RAM * 40% = 12.8 GB; a 7 GB model fits.
            var result = InferenceParameterResolver.ResolveGpuLayerCount(-1, true, ModernIntegratedPlan, 7 * Gb, MaxModelPercentOfRam);

            Assert.Equal(HardwareProfileService.AllGpuLayers, result);
        }

        [Fact]
        public void ResolveGpuLayerCount_ModernIntegrated_ModelExceedsGuard_StaysOnCpu()
        {
            var result = InferenceParameterResolver.ResolveGpuLayerCount(-1, true, ModernIntegratedPlan, 16 * Gb, MaxModelPercentOfRam);

            Assert.Equal(0, result);
        }

        [Fact]
        public void ResolveGpuLayerCount_ModernIntegrated_ExplicitConfig_IgnoresGuard()
        {
            var result = InferenceParameterResolver.ResolveGpuLayerCount(20, true, ModernIntegratedPlan, 16 * Gb, MaxModelPercentOfRam);

            Assert.Equal(20, result);
        }

        [Fact]
        public void ResolveGpuLayerCount_Discrete_LargeModel_GuardDoesNotApply()
        {
            var result = InferenceParameterResolver.ResolveGpuLayerCount(-1, true, Plan, 16 * Gb, MaxModelPercentOfRam);

            Assert.Equal(Plan.RecommendedGpuLayerCount, result);
        }

        [Fact]
        public void ResolveThreads_ConfiguredPositive_ReturnsConfiguredValue()
        {
            var result = InferenceParameterResolver.ResolveThreads(4, Plan);

            Assert.Equal(4, result);
        }

        [Fact]
        public void ResolveThreads_ConfiguredZero_ReturnsRecommendedThreads()
        {
            var result = InferenceParameterResolver.ResolveThreads(0, Plan);

            Assert.Equal(Plan.RecommendedThreads, result);
        }

        [Fact]
        public void ResolveGpuLayerCount_ConfiguredNonNegative_AlwaysWinsRegardlessOfAutoTune()
        {
            var result = InferenceParameterResolver.ResolveGpuLayerCount(configuredGpuLayerCount: 10, hardwareAutoTune: true, Plan, SmallModelBytes, MaxModelPercentOfRam);

            Assert.Equal(10, result);
        }

        [Fact]
        public void ResolveGpuLayerCount_ConfiguredExplicitZero_IsHonoredEvenWithAutoTune()
        {
            // 0 is an explicit "force CPU-only" value, distinct from the -1 auto sentinel.
            var result = InferenceParameterResolver.ResolveGpuLayerCount(configuredGpuLayerCount: 0, hardwareAutoTune: true, Plan, SmallModelBytes, MaxModelPercentOfRam);

            Assert.Equal(0, result);
        }

        [Fact]
        public void ResolveGpuLayerCount_SentinelWithAutoTuneOn_ReturnsRecommended()
        {
            var result = InferenceParameterResolver.ResolveGpuLayerCount(configuredGpuLayerCount: -1, hardwareAutoTune: true, Plan, SmallModelBytes, MaxModelPercentOfRam);

            Assert.Equal(Plan.RecommendedGpuLayerCount, result);
        }

        [Fact]
        public void ResolveGpuLayerCount_SentinelWithAutoTuneOff_ReturnsZero()
        {
            var result = InferenceParameterResolver.ResolveGpuLayerCount(configuredGpuLayerCount: -1, hardwareAutoTune: false, Plan, SmallModelBytes, MaxModelPercentOfRam);

            Assert.Equal(0, result);
        }

        [Fact]
        public void ResolveBatchSize_ConfiguredPositive_AlwaysWins()
        {
            var result = InferenceParameterResolver.ResolveBatchSize(configuredBatchSize: 128, hardwareAutoTune: true, Plan);

            Assert.Equal(128, result);
        }

        [Fact]
        public void ResolveBatchSize_SentinelWithAutoTuneOn_ReturnsRecommended()
        {
            var result = InferenceParameterResolver.ResolveBatchSize(configuredBatchSize: 0, hardwareAutoTune: true, Plan);

            Assert.Equal(Plan.RecommendedBatchSize, result);
        }

        [Fact]
        public void ResolveBatchSize_SentinelWithAutoTuneOff_ReturnsZero()
        {
            var result = InferenceParameterResolver.ResolveBatchSize(configuredBatchSize: 0, hardwareAutoTune: false, Plan);

            Assert.Equal(0, result);
        }
    }
}

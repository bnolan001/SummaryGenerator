using SummaryGenerator.Models;
using SummaryGenerator.Services;

namespace SummaryGenerator.Tests.Unit
{
    public class HardwareProfileServiceComputePlanTests
    {
        private const long OneGb = 1024L * 1024 * 1024;

        [Theory]
        [InlineData(8, 6)]
        [InlineData(4, 2)]
        [InlineData(2, 1)] // clamped to a minimum of 1 thread
        [InlineData(1, 1)]
        public void ComputePlan_RecommendedThreads_IsCoresMinusTwoClampedToOne(int logicalCores, int expectedThreads)
        {
            var plan = HardwareProfileService.ComputePlan(logicalCores, 32 * OneGb, GpuTier.None);

            Assert.Equal(expectedThreads, plan.RecommendedThreads);
        }

        [Theory]
        [InlineData(GpuTier.None, 0)]
        [InlineData(GpuTier.Integrated, 0)]
        [InlineData(GpuTier.ModernIntegrated, HardwareProfileService.AllGpuLayers)]
        [InlineData(GpuTier.Discrete, 32)]
        public void ComputePlan_RecommendedGpuLayerCount_DependsOnGpuTier(GpuTier gpuTier, int expectedLayers)
        {
            var plan = HardwareProfileService.ComputePlan(8, 32 * OneGb, gpuTier);

            Assert.Equal(expectedLayers, plan.RecommendedGpuLayerCount);
        }

        [Theory]
        [InlineData(8, 4096u)]
        [InlineData(16, 8192u)]
        [InlineData(32, 16384u)]
        [InlineData(64, 32768u)]
        public void ComputePlan_RecommendedContextSize_ScalesWithTotalMemory(int totalMemoryGb, uint expectedContextSize)
        {
            var plan = HardwareProfileService.ComputePlan(8, totalMemoryGb * OneGb, GpuTier.None);

            Assert.Equal(expectedContextSize, plan.RecommendedContextSize);
        }

        [Theory]
        [InlineData(GpuTier.None, 256)]
        [InlineData(GpuTier.Integrated, 512)]
        [InlineData(GpuTier.ModernIntegrated, 512)]
        [InlineData(GpuTier.Discrete, 512)]
        public void ComputePlan_RecommendedBatchSize_IsHigherWithAnyGpu(GpuTier gpuTier, int expectedBatchSize)
        {
            var plan = HardwareProfileService.ComputePlan(8, 32 * OneGb, gpuTier);

            Assert.Equal(expectedBatchSize, plan.RecommendedBatchSize);
        }

        [Fact]
        public void ComputePlan_PassesThroughRawHardwareFacts()
        {
            var plan = HardwareProfileService.ComputePlan(8, 32 * OneGb, GpuTier.Discrete);

            Assert.Equal(8, plan.LogicalCores);
            Assert.Equal(32 * OneGb, plan.TotalMemoryBytes);
            Assert.Equal(GpuTier.Discrete, plan.GpuTier);
        }
    }
}

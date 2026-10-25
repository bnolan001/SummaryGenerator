using SummaryGenerator.Models;
using SummaryGenerator.Services;

namespace SummaryGenerator.Tests.Unit
{
    public class HardwareProfileServiceClassifyGpuTests
    {
        [Fact]
        public void ClassifyGpu_NoAdapters_ReturnsNone()
        {
            var tier = HardwareProfileService.ClassifyGpu([]);

            Assert.Equal(GpuTier.None, tier);
        }

        [Fact]
        public void ClassifyGpu_OnlyBasicRenderDriver_ReturnsNone()
        {
            var tier = HardwareProfileService.ClassifyGpu(["Microsoft Basic Render Driver"]);

            Assert.Equal(GpuTier.None, tier);
        }

        [Fact]
        public void ClassifyGpu_IntegratedAdapterOnly_ReturnsIntegrated()
        {
            var tier = HardwareProfileService.ClassifyGpu(["AMD Radeon Graphics"]);

            Assert.Equal(GpuTier.Integrated, tier);
        }

        [Fact]
        public void ClassifyGpu_DiscreteAdapterOnly_ReturnsDiscrete()
        {
            var tier = HardwareProfileService.ClassifyGpu(["NVIDIA GeForce RTX 4070 Laptop GPU"]);

            Assert.Equal(GpuTier.Discrete, tier);
        }

        [Fact]
        public void ClassifyGpu_IntegratedListedBeforeDiscrete_ReturnsDiscrete()
        {
            // Hybrid laptops (Intel iGPU + NVIDIA dGPU) commonly report the integrated
            // adapter first. The discrete adapter must still win.
            var tier = HardwareProfileService.ClassifyGpu(["Intel(R) Iris(R) Xe Graphics", "NVIDIA GeForce RTX 4070 Laptop GPU"]);

            Assert.Equal(GpuTier.Discrete, tier);
        }
    }
}

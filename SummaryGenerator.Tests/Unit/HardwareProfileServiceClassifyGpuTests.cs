using SummaryGenerator.Models;
using SummaryGenerator.Services;

namespace SummaryGenerator.Tests.Unit
{
    public class HardwareProfileServiceClassifyGpuTests
    {
        [Theory]
        [InlineData("Intel(R) Arc(TM) A770 Graphics")]
        [InlineData("Intel(R) Arc(TM) A370M Graphics")]
        [InlineData("Intel(R) Arc(TM) B580 Graphics")]
        [InlineData("Intel(R) Arc(TM) Pro A60 Graphics")]
        public void ClassifyGpu_IntelArcDiscreteCard_ReturnsDiscrete(string name)
        {
            Assert.Equal(GpuTier.Discrete, HardwareProfileService.ClassifyGpu([name]));
        }

        [Theory]
        [InlineData("Intel(R) Arc(TM) 140V GPU (16GB)")]
        [InlineData("Intel(R) Arc(TM) 130V GPU")]
        [InlineData("Intel(R) Arc(TM) 140T GPU")]
        [InlineData("Intel(R) Arc(TM) Graphics")]
        [InlineData("Intel(R) Arc(TM) B390 GPU")]
        [InlineData("Intel(R) Arc(TM) B370 GPU")]
        [InlineData("AMD Radeon(TM) 780M Graphics")]
        [InlineData("AMD Radeon(TM) 760M Graphics")]
        [InlineData("AMD Radeon 890M Graphics")]
        [InlineData("AMD Radeon(TM) 8060S Graphics")]
        public void ClassifyGpu_ModernIntegratedGpu_ReturnsModernIntegrated(string name)
        {
            Assert.Equal(GpuTier.ModernIntegrated, HardwareProfileService.ClassifyGpu([name]));
        }

        [Theory]
        [InlineData("Intel(R) UHD Graphics 620")]
        [InlineData("Intel(R) Iris(R) Xe Graphics")]
        [InlineData("AMD Radeon(TM) Vega 8 Graphics")]
        public void ClassifyGpu_OlderIntegratedGpu_ReturnsIntegrated(string name)
        {
            Assert.Equal(GpuTier.Integrated, HardwareProfileService.ClassifyGpu([name]));
        }

        [Fact]
        public void ClassifyGpu_ModernIntegratedAlongsideDiscrete_ReturnsDiscrete()
        {
            var tier = HardwareProfileService.ClassifyGpu(["AMD Radeon(TM) 780M Graphics", "NVIDIA GeForce RTX 4070 Laptop GPU"]);

            Assert.Equal(GpuTier.Discrete, tier);
        }

        [Fact]
        public void ClassifyGpu_OnlyMicrosoftVirtualAdapters_ReturnsNone()
        {
            var tier = HardwareProfileService.ClassifyGpu(
                ["Microsoft Remote Display Adapter", "Microsoft Hyper-V Video", "Microsoft Basic Display Adapter"]);

            Assert.Equal(GpuTier.None, tier);
        }

        [Fact]
        public void ClassifyGpu_MicrosoftAdapterAlongsideIntegrated_ReturnsIntegrated()
        {
            var tier = HardwareProfileService.ClassifyGpu(
                ["Microsoft Remote Display Adapter", "Intel(R) UHD Graphics 620"]);

            Assert.Equal(GpuTier.Integrated, tier);
        }

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

using SummaryGenerator.Models;
using SummaryGenerator.Services;

namespace SummaryGenerator.Tests.Unit
{
    public class ModelFitTests
    {
        private const long Gb = 1024L * 1024 * 1024;

        [Theory]
        [InlineData(2.3)]
        [InlineData(6.6)]
        [InlineData(8.4)]
        public void FitsThisPc_ModelWithinRamShare_ReturnsTrue(double sizeGb)
        {
            // 32 GB * 40% = 12.8 GB
            var fits = ModelFit.FitsThisPc(new ModelDetails { ApproxSizeGb = sizeGb }, 32 * Gb, maxModelPercentOfRam: 40);

            Assert.True(fits);
        }

        [Fact]
        public void FitsThisPc_ModelOverRamShare_ReturnsFalse()
        {
            var fits = ModelFit.FitsThisPc(new ModelDetails { ApproxSizeGb = 15.8 }, 32 * Gb, maxModelPercentOfRam: 40);

            Assert.False(fits);
        }

        [Fact]
        public void FitsThisPc_UnknownSize_ReturnsNull()
        {
            var fits = ModelFit.FitsThisPc(new ModelDetails(), 32 * Gb, maxModelPercentOfRam: 40);

            Assert.Null(fits);
        }

        [Fact]
        public void TooLargeLabel_ModernIntegratedGpu_SaysCpuOnly()
        {
            Assert.Equal("Too large – CPU only", ModelFit.TooLargeLabel(GpuTier.ModernIntegrated));
        }

        [Theory]
        [InlineData(GpuTier.None)]
        [InlineData(GpuTier.Integrated)]
        [InlineData(GpuTier.Discrete)]
        public void TooLargeLabel_OtherHardware_SaysTooLargeForThisPc(GpuTier tier)
        {
            Assert.Equal("Too large for this PC", ModelFit.TooLargeLabel(tier));
        }
    }
}

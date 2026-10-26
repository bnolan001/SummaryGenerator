namespace SummaryGenerator.Models
{
    /// <summary>
    /// Coarse classification of the GPU hardware detected on the host machine.
    /// </summary>
    public enum GpuTier
    {
        /// <summary>No usable GPU adapter was detected.</summary>
        None,

        /// <summary>Only integrated graphics (shares system RAM) was detected.</summary>
        Integrated,

        /// <summary>At least one discrete GPU (dedicated VRAM) was detected.</summary>
        Discrete,

        /// <summary>
        /// Recent integrated graphics (Intel Arc, AMD Radeon 7xxM/8xxM/8050S/8060S) that run inference
        /// faster than the CPU. Shares system RAM, so offload is subject to a model-size guard.
        /// </summary>
        ModernIntegrated
    }
}

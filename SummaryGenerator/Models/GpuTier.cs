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
        Discrete
    }
}

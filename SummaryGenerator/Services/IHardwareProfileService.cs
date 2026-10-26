using SummaryGenerator.Models;

namespace SummaryGenerator.Services
{
    public interface IHardwareProfileService
    {
        /// <summary>
        /// Gets the hardware plan computed for this machine. Computed once and cached
        /// for the process lifetime.
        /// </summary>
        HardwarePlan GetPlan();
    }
}

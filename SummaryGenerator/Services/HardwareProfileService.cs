using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SummaryGenerator.Models;

namespace SummaryGenerator.Services
{
    public partial class HardwareProfileService : IHardwareProfileService
    {
        private readonly Lazy<HardwarePlan> _lazyPlan;

        public HardwareProfileService(ILogger<HardwareProfileService> logger)
        {
            _lazyPlan = new Lazy<HardwarePlan>(() => ComputePlanFromEnvironment(logger));
        }

        public HardwarePlan GetPlan() => _lazyPlan.Value;

        private static readonly string[] DiscretePatterns =
        [
            "NVIDIA",
            "Radeon RX",
            "Radeon Pro"
        ];

        // Microsoft-named adapters are software/virtual (Basic Render/Display, Remote Display, Hyper-V).
        private const string IgnoredAdapterPattern = "Microsoft";

        // Intel Arc discrete cards carry an A/B model number (A770, A370M, B580, Pro A60);
        // integrated Arc names don't (Arc 140V, Arc 130V, Arc 140T, Arc Graphics).
        [GeneratedRegex(@"\bArc(?:\(TM\))?\s+(?:Pro\s+)?[AB]\d{2,3}", RegexOptions.IgnoreCase)]
        private static partial Regex IntelArcDiscreteRegex();

        /// <summary>
        /// Classifies the overall GPU tier from the names of every detected video adapter.
        /// Scans all adapters (not just the first) so a discrete GPU is recognized even when
        /// it's reported alongside integrated graphics (common on hybrid laptops).
        /// </summary>
        public static GpuTier ClassifyGpu(IEnumerable<string> adapterNames)
        {
            var names = adapterNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Where(name => !name.Contains(IgnoredAdapterPattern, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (names.Count == 0)
            {
                return GpuTier.None;
            }

            var anyDiscrete = names.Any(name =>
                IntelArcDiscreteRegex().IsMatch(name) ||
                DiscretePatterns.Any(pattern => name.Contains(pattern, StringComparison.OrdinalIgnoreCase)));

            return anyDiscrete ? GpuTier.Discrete : GpuTier.Integrated;
        }

        private const double BytesPerGb = 1024.0 * 1024 * 1024;

        /// <summary>
        /// Computes recommended inference parameters from raw hardware facts. Pure and
        /// side-effect free so it can be unit tested without real hardware.
        /// </summary>
        public static HardwarePlan ComputePlan(int logicalCores, long totalMemoryBytes, GpuTier gpuTier)
        {
            var recommendedThreads = Math.Max(1, logicalCores - 2);

            var recommendedGpuLayerCount = gpuTier == GpuTier.Discrete ? 32 : 0;

            var totalMemoryGb = totalMemoryBytes / BytesPerGb;
            var recommendedContextSize = totalMemoryGb switch
            {
                <= 8 => 4096u,
                <= 16 => 8192u,
                <= 32 => 16384u,
                _ => 32768u
            };

            var recommendedBatchSize = gpuTier == GpuTier.None ? 256 : 512;

            return new HardwarePlan(
                logicalCores,
                totalMemoryBytes,
                gpuTier,
                recommendedThreads,
                recommendedGpuLayerCount,
                recommendedContextSize,
                recommendedBatchSize);
        }

        private static HardwarePlan ComputePlanFromEnvironment(ILogger logger)
        {
            var logicalCores = Environment.ProcessorCount;
            var totalMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            var gpuTier = DetectGpuTier(logger);

            var plan = ComputePlan(logicalCores, totalMemoryBytes, gpuTier);

            logger.LogInformation(
                "Hardware profile detected: Cores={Cores}, TotalMemoryBytes={TotalMemoryBytes}, GpuTier={GpuTier}. " +
                "Recommended: Threads={Threads}, GpuLayerCount={GpuLayerCount}, ContextSize={ContextSize}, BatchSize={BatchSize}",
                plan.LogicalCores, plan.TotalMemoryBytes, plan.GpuTier,
                plan.RecommendedThreads, plan.RecommendedGpuLayerCount, plan.RecommendedContextSize, plan.RecommendedBatchSize);

            return plan;
        }

        private static GpuTier DetectGpuTier(ILogger logger)
        {
            if (!OperatingSystem.IsWindows())
            {
                return GpuTier.None;
            }

            try
            {
                return ClassifyGpu(QueryWindowsVideoControllerNames());
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "GPU detection failed; assuming no GPU is available.");
                return GpuTier.None;
            }
        }

        [SupportedOSPlatform("windows")]
        private static IEnumerable<string> QueryWindowsVideoControllerNames()
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            using var results = searcher.Get();

            var names = new List<string>();
            foreach (var item in results)
            {
                using (item)
                {
                    if (item["Name"]?.ToString() is { } name)
                    {
                        names.Add(name);
                    }
                }
            }

            return names;
        }
    }
}

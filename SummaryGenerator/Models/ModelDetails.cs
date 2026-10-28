namespace SummaryGenerator.Models
{
    public class ModelDetails
    {
        public string Name { get; set; } = string.Empty;
        public string Repository { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public uint? PreferredContextSize { get; set; }
        public uint? MaxContextSize { get; set; }

        /// <summary>Approximate quantized file size, used to recommend a model that fits this machine.</summary>
        public double? ApproxSizeGb { get; set; }

        public bool Default { get; set; } = false;
    }
}

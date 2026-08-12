namespace SummaryGenerator.Models
{
    public class ModelDetails
    {
        public string Name { get; set; } = string.Empty;
        public string Repository { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public uint? PreferredContextSize { get; set; }
        public uint? MaxContextSize { get; set; }

        public bool Default { get; set; } = false;
    }
}

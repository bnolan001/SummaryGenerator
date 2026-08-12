namespace SummaryGenerator.Models
{
    public class StorageOptions
    {
        public const string SectionName = "Storage";

        public string ModelsPath { get; set; } = "Downloads";

        public string OutputPath { get; set; } = "Output";
    }
}

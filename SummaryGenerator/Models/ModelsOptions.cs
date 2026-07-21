namespace SummaryGenerator.Models
{
    public class ModelsOptions
    {
        public const string SectionName = "Models";

        public string DefaultModel { get; set; } = string.Empty;

        public List<ModelDetails> Choices { get; set; } = new();
    }
}

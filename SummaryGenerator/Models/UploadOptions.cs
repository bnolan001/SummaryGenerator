namespace SummaryGenerator.Models
{
    public class UploadOptions
    {
        public const string SectionName = "Uploads";

        public string TempPath { get; set; } = "Uploads";

        public long MaxFileSizeBytes { get; set; } = 50 * 1024 * 1024;

        public List<string> AllowedExtensions { get; set; } = [".pdf"];
    }
}

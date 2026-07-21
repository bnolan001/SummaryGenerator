namespace SummaryGenerator.Models
{
    public class SummaryResult
    {
        public required Guid TaskId { get; init; }

        public required string SourcePdfPath { get; init; }

        public required string OutputMarkdownPath { get; init; }

        public required string ModelName { get; init; }

        public required DateTimeOffset CompletedAtUtc { get; init; }
    }
}

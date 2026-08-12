namespace SummaryGenerator.Models
{
    public class SummaryResult
    {
        public required Guid TaskId { get; init; }

        public string? SourcePdfPath { get; init; }

        public string? OutputMarkdownPath { get; init; }

        public string? OutputAudioPath { get; init; }

        public string? ModelName { get; init; }

        public required DateTimeOffset CompletedAtUtc { get; init; }
    }
}

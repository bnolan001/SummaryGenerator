namespace SummaryGenerator.Models
{
    public class ProcessingTask
    {
        public Guid Id { get; init; } = Guid.NewGuid();

        public required string SourcePdfPath { get; init; }

        public required ModelDetails Model { get; init; }

        public required string PromptProfileId { get; init; }

        public required string PromptProfileName { get; init; }

        public required string SystemPrompt { get; init; }

        public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

        public DateTimeOffset? StartedAtUtc { get; set; }

        public DateTimeOffset? CompletedAtUtc { get; set; }

        public ProcessingTaskStatus Status { get; set; } = ProcessingTaskStatus.Queued;

        public string? ErrorMessage { get; set; }

        public SummaryResult? Result { get; set; }
    }
}

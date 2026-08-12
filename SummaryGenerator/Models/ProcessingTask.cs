namespace SummaryGenerator.Models
{
    public class ProcessingTask
    {
        public Guid Id { get; init; } = Guid.NewGuid();

        public ProcessingTaskType TaskType { get; init; } = ProcessingTaskType.Summary;

        public string? SourcePdfPath { get; init; }

        public string? SourceMarkdownPath { get; init; }

        public string? SelectedVoice { get; init; }

        public ModelDetails? Model { get; init; }

        public string? PromptProfileId { get; init; }

        public string? PromptProfileName { get; init; }

        public string? SystemPrompt { get; init; }

        public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

        public DateTimeOffset? StartedAtUtc { get; set; }

        public DateTimeOffset? CompletedAtUtc { get; set; }

        public ProcessingTaskStatus Status { get; set; } = ProcessingTaskStatus.Queued;

        public string? ErrorMessage { get; set; }

        public SummaryResult? Result { get; set; }
    }
}

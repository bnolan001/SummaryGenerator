namespace SummaryGenerator.Models
{
    public enum ProcessingTaskStatus
    {
        Queued = 0,
        DownloadingModel = 1,
        ExtractingText = 2,
        Summarizing = 3,
        WritingOutput = 4,
        Completed = 5,
        Failed = 6,
        GeneratingAudio = 7
    }
}

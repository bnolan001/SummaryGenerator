namespace SummaryGenerator.Models
{
    public class QueueOptions
    {
        public const string SectionName = "Queue";

        public int MaxQueuedTasks { get; set; } = 25;

        public int WorkerCount { get; set; } = 1;
    }
}

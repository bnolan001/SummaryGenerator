using SummaryGenerator.Models;

namespace SummaryGenerator.Services
{
    public interface IProcessingQueue
    {
        ProcessingTask Enqueue(string sourcePdfPath, ModelDetails model, PromptProfile promptProfile);

        ProcessingTask EnqueueAudio(string sourceMarkdownPath, string? selectedVoice = null);

        ProcessingTask EnqueueAudioFromSummary(Guid summaryTaskId, string? selectedVoice = null);

        bool TryGetTask(Guid taskId, out ProcessingTask? task);

        IReadOnlyCollection<ProcessingTask> GetAllTasks();
    }
}

using SummaryGenerator.Models;

namespace SummaryGenerator.Services
{
    public interface IProcessingQueue
    {
        ProcessingTask Enqueue(string sourcePdfPath, ModelDetails model, PromptProfile promptProfile);

        bool TryGetTask(Guid taskId, out ProcessingTask? task);

        IReadOnlyCollection<ProcessingTask> GetAllTasks();
    }
}

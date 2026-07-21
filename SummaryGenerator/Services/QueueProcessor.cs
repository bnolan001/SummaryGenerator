using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using SummaryGenerator.Models;
using SummaryGenerator.Repositories.HuggingFace;

namespace SummaryGenerator.Services
{
    public class QueueProcessor : BackgroundService, IProcessingQueue
    {
        private readonly ConcurrentQueue<Guid> _queue = new();
        private readonly ConcurrentDictionary<Guid, ProcessingTask> _tasks = new();
        private readonly SemaphoreSlim _signal = new(0);
        private readonly IPdfTextExtractor _pdfTextExtractor;
        private readonly ILlamaSharpSummarizer _summarizer;
        private readonly IMarkdownOutputWriter _markdownWriter;
        private readonly IHuggingFaceRepository _huggingFaceRepository;
        private readonly IWebHostEnvironment _environment;
        private readonly StorageOptions _storageOptions;
        private readonly QueueOptions _queueOptions;
        private readonly ILogger<QueueProcessor> _logger;

        public QueueProcessor(
            IPdfTextExtractor pdfTextExtractor,
            ILlamaSharpSummarizer summarizer,
            IMarkdownOutputWriter markdownWriter,
            IHuggingFaceRepository huggingFaceRepository,
            IWebHostEnvironment environment,
            IOptions<StorageOptions> storageOptions,
            IOptions<QueueOptions> queueOptions,
            ILogger<QueueProcessor> logger)
        {
            _pdfTextExtractor = pdfTextExtractor;
            _summarizer = summarizer;
            _markdownWriter = markdownWriter;
            _huggingFaceRepository = huggingFaceRepository;
            _environment = environment;
            _storageOptions = storageOptions.Value;
            _queueOptions = queueOptions.Value;
            _logger = logger;

            Directory.CreateDirectory(ResolveStorageRoot(_storageOptions.ModelsPath));
            Directory.CreateDirectory(ResolveStorageRoot(_storageOptions.OutputPath));
        }

        public ProcessingTask Enqueue(string sourcePdfPath, ModelDetails model, PromptProfile promptProfile)
        {
            if (string.IsNullOrWhiteSpace(sourcePdfPath))
            {
                throw new ArgumentException("Source PDF path is required.", nameof(sourcePdfPath));
            }

            if (model is null)
            {
                throw new ArgumentNullException(nameof(model));
            }
            if (promptProfile is null)
            {
                throw new ArgumentNullException(nameof(promptProfile));
            }
            if (string.IsNullOrWhiteSpace(promptProfile.Prompt))
            {
                throw new ArgumentException("Prompt profile prompt is required.", nameof(promptProfile));
            }

            if (!File.Exists(sourcePdfPath))
            {
                throw new FileNotFoundException("Source PDF was not found.", sourcePdfPath);
            }

            if (_tasks.Count(task => task.Value.Status is ProcessingTaskStatus.Queued or ProcessingTaskStatus.DownloadingModel or ProcessingTaskStatus.ExtractingText or ProcessingTaskStatus.Summarizing or ProcessingTaskStatus.WritingOutput) >= _queueOptions.MaxQueuedTasks)
            {
                throw new InvalidOperationException($"Queue limit reached ({_queueOptions.MaxQueuedTasks}).");
            }

            var task = new ProcessingTask
            {
                SourcePdfPath = sourcePdfPath,
                Model = model,
                PromptProfileId = promptProfile.Id,
                PromptProfileName = promptProfile.Name,
                SystemPrompt = promptProfile.Prompt
            };

            _tasks[task.Id] = task;
            _queue.Enqueue(task.Id);
            _signal.Release();
            _logger.LogInformation("Queued processing task {TaskId} for {FilePath}.", task.Id, task.SourcePdfPath);

            return task;
        }

        public bool TryGetTask(Guid taskId, out ProcessingTask? task)
        {
            var found = _tasks.TryGetValue(taskId, out var localTask);
            task = localTask;
            return found;
        }

        public IReadOnlyCollection<ProcessingTask> GetAllTasks() =>
            _tasks.Values
                .OrderByDescending(task => task.CreatedAtUtc)
                .ToList();

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await _signal.WaitAsync(stoppingToken);

                if (_queueOptions.WorkerCount <= 1)
                {
                    if (_queue.TryDequeue(out var taskId))
                    {
                        await ProcessTaskAsync(taskId, stoppingToken);
                    }

                    continue;
                }

                var workers = new List<Task>();
                for (var i = 0; i < _queueOptions.WorkerCount; i++)
                {
                    if (!_queue.TryDequeue(out var taskId))
                    {
                        break;
                    }

                    workers.Add(ProcessTaskAsync(taskId, stoppingToken));
                }

                await Task.WhenAll(workers);
            }
        }

        private async Task ProcessTaskAsync(Guid taskId, CancellationToken cancellationToken)
        {
            if (!_tasks.TryGetValue(taskId, out var task))
            {
                return;
            }

            try
            {
                task.StartedAtUtc = DateTimeOffset.UtcNow;
                var modelPath = Path.Combine(ResolveStorageRoot(_storageOptions.ModelsPath), task.Model.FileName);

                if (!File.Exists(modelPath))
                {
                    task.Status = ProcessingTaskStatus.DownloadingModel;
                    _logger.LogInformation("Model missing for task {TaskId}. Downloading {Repository}/{FileName}.", task.Id, task.Model.Repository, task.Model.FileName);
                    var downloadResult = await _huggingFaceRepository.DownloadModelAsync(
                        task.Model.Repository,
                        task.Model.FileName,
                        modelPath,
                        progress: null,
                        cancellationToken);

                    if (!downloadResult.Succeeded)
                    {
                        throw new InvalidOperationException($"Model download failed: {downloadResult.ErrorMessage ?? "Unknown error"}");
                    }
                }

                task.Status = ProcessingTaskStatus.ExtractingText;
                var pdfText = await _pdfTextExtractor.ExtractTextAsync(task.SourcePdfPath, cancellationToken);
                if (string.IsNullOrWhiteSpace(pdfText))
                {
                    throw new InvalidDataException("No extractable text was found in the PDF.");
                }

                task.Status = ProcessingTaskStatus.Summarizing;
                var markdown = await _summarizer.SummarizeAsync(
                    pdfText,
                    modelPath,
                    task.Model,
                    task.SystemPrompt,
                    cancellationToken);
                if (string.IsNullOrWhiteSpace(markdown))
                {
                    throw new InvalidOperationException("Summarization produced empty output.");
                }

                task.Status = ProcessingTaskStatus.WritingOutput;
                var outputPath = await _markdownWriter.WriteAsync(
                    task.SourcePdfPath,
                    task.Model.Name,
                    markdown,
                    cancellationToken);

                task.Result = new SummaryResult
                {
                    TaskId = task.Id,
                    SourcePdfPath = task.SourcePdfPath,
                    OutputMarkdownPath = outputPath,
                    ModelName = task.Model.Name,
                    CompletedAtUtc = DateTimeOffset.UtcNow
                };
                task.Status = ProcessingTaskStatus.Completed;
                task.CompletedAtUtc = DateTimeOffset.UtcNow;
                _logger.LogInformation("Completed processing task {TaskId}. Output: {OutputPath}", task.Id, outputPath);
            }
            catch (FileNotFoundException ex)
            {
                SetFailure(task, $"Required file not found: {ex.Message}");
                _logger.LogWarning(ex, "File dependency missing for task {TaskId}.", task.Id);
            }
            catch (InvalidDataException ex)
            {
                SetFailure(task, $"PDF extraction failed: {ex.Message}");
                _logger.LogWarning(ex, "PDF extraction failed for task {TaskId}.", task.Id);
            }
            catch (IOException ex)
            {
                SetFailure(task, $"Output write failed: {ex.Message}");
                _logger.LogError(ex, "I/O failure for task {TaskId}.", task.Id);
            }
            catch (InvalidOperationException ex)
            {
                SetFailure(task, ex.Message);
                _logger.LogError(ex, "Processing error for task {TaskId}.", task.Id);
            }
            catch (Exception ex)
            {
                SetFailure(task, $"Unexpected processing error: {ex.Message}");
                _logger.LogError(ex, "Failed processing task {TaskId}.", task.Id);
            }
        }

        private static void SetFailure(ProcessingTask task, string message)
        {
            task.Status = ProcessingTaskStatus.Failed;
            task.ErrorMessage = message;
            task.CompletedAtUtc = DateTimeOffset.UtcNow;
        }

        private string ResolveStorageRoot(string configuredPath)
        {
            if (Path.IsPathRooted(configuredPath))
            {
                return configuredPath;
            }

            return Path.Combine(_environment.ContentRootPath, configuredPath);
        }
    }
}

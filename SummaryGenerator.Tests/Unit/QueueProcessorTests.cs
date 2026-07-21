using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SummaryGenerator.Models;
using SummaryGenerator.Repositories.HuggingFace;
using SummaryGenerator.Services;
using SummaryGenerator.Tests.Support;

namespace SummaryGenerator.Tests.Unit
{
    public class QueueProcessorTests
    {
        [Fact]
        public async Task ProcessesTaskThroughStatesAndCompletes()
        {
            var tempRoot = CreateTempDirectory();
            try
            {
                var model = new ModelDetails
                {
                    Name = "Test Model",
                    Repository = "repo/model",
                    FileName = "model.gguf"
                };

                var sourcePdfPath = CreatePlaceholderPdf(tempRoot, "doc-1.pdf");
                var outputPath = Path.Combine(tempRoot, "Output", "doc-1-Test Model.md");

                var extractorGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var extractor = new GatedPdfTextExtractor(extractorGate);
                var summarizer = new StaticSummarizer("# Summary");
                var writer = new StaticWriter(outputPath);
                var downloader = new NoopDownloader(Path.Combine(tempRoot, "Downloads", model.FileName));

                var processor = CreateProcessor(tempRoot, extractor, summarizer, writer, downloader);
                await processor.StartAsync(CancellationToken.None);

                try
                {
                    var promptProfile = new PromptProfile
                    {
                        Id = "pme",
                        Name = "PME",
                        Prompt = "Prompt",
                        IsBuiltIn = true
                    };
                    var task = processor.Enqueue(sourcePdfPath, model, promptProfile);

                    await extractor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Assert.True(processor.TryGetTask(task.Id, out var inProgress));
                    Assert.NotNull(inProgress);
                    Assert.Equal(ProcessingTaskStatus.ExtractingText, inProgress!.Status);

                    extractorGate.TrySetResult(true);

                    await WaitForAsync(
                        () => processor.TryGetTask(task.Id, out var completedTask) &&
                              completedTask is not null &&
                              completedTask.Status == ProcessingTaskStatus.Completed,
                        TimeSpan.FromSeconds(5));

                    Assert.True(processor.TryGetTask(task.Id, out var done));
                    Assert.NotNull(done);
                    Assert.NotNull(done!.Result);
                    Assert.Equal(ProcessingTaskStatus.Completed, done.Status);
                    Assert.Equal(outputPath, done.Result!.OutputMarkdownPath);
                }
                finally
                {
                    await processor.StopAsync(CancellationToken.None);
                    processor.Dispose();
                }
            }
            finally
            {
                Directory.Delete(tempRoot, true);
            }
        }

        [Fact]
        public async Task WorkerCountOne_ProcessesQueuedTasksSequentially()
        {
            var tempRoot = CreateTempDirectory();
            try
            {
                var model = new ModelDetails
                {
                    Name = "Test Model",
                    Repository = "repo/model",
                    FileName = "model.gguf"
                };

                var sourceA = CreatePlaceholderPdf(tempRoot, "a.pdf");
                var sourceB = CreatePlaceholderPdf(tempRoot, "b.pdf");
                var sourceC = CreatePlaceholderPdf(tempRoot, "c.pdf");

                var extractor = new ImmediatePdfTextExtractor();
                var summarizer = new TrackingSummarizer();
                var writer = new TimestampWriter(tempRoot);
                var downloader = new NoopDownloader(Path.Combine(tempRoot, "Downloads", model.FileName));
                var processor = CreateProcessor(tempRoot, extractor, summarizer, writer, downloader);
                await processor.StartAsync(CancellationToken.None);

                try
                {
                    var promptProfile = new PromptProfile
                    {
                        Id = "pme",
                        Name = "PME",
                        Prompt = "Prompt",
                        IsBuiltIn = true
                    };
                    var taskA = processor.Enqueue(sourceA, model, promptProfile);
                    var taskB = processor.Enqueue(sourceB, model, promptProfile);
                    var taskC = processor.Enqueue(sourceC, model, promptProfile);

                    await WaitForAsync(
                        () =>
                        {
                            var all = processor.GetAllTasks();
                            return all.Count == 3 && all.All(task => task.Status == ProcessingTaskStatus.Completed);
                        },
                        TimeSpan.FromSeconds(10));

                    Assert.Equal(1, summarizer.MaxConcurrency);
                    Assert.All(new[] { taskA, taskB, taskC }, task =>
                    {
                        Assert.True(processor.TryGetTask(task.Id, out var storedTask));
                        Assert.Equal(ProcessingTaskStatus.Completed, storedTask!.Status);
                    });
                }
                finally
                {
                    await processor.StopAsync(CancellationToken.None);
                    processor.Dispose();
                }
            }
            finally
            {
                Directory.Delete(tempRoot, true);
            }
        }

        private static QueueProcessor CreateProcessor(
            string contentRoot,
            IPdfTextExtractor extractor,
            ILlamaSharpSummarizer summarizer,
            IMarkdownOutputWriter writer,
            IHuggingFaceRepository downloader)
        {
            var environment = new TestWebHostEnvironment
            {
                ContentRootPath = contentRoot
            };
            var storageOptions = Options.Create(new StorageOptions
            {
                ModelsPath = "Downloads",
                OutputPath = "Output"
            });
            var queueOptions = Options.Create(new QueueOptions
            {
                MaxQueuedTasks = 25,
                WorkerCount = 1
            });

            return new QueueProcessor(
                extractor,
                summarizer,
                writer,
                downloader,
                environment,
                storageOptions,
                queueOptions,
                NullLogger<QueueProcessor>.Instance);
        }

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), $"summary-generator-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return path;
        }

        private static string CreatePlaceholderPdf(string directory, string fileName)
        {
            var path = Path.Combine(directory, fileName);
            File.WriteAllBytes(path, "%PDF-1.7\n".Select(ch => (byte)ch).ToArray());
            return path;
        }

        private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return;
                }

                await Task.Delay(25);
            }

            throw new TimeoutException("Condition was not met before timeout.");
        }

        private sealed class GatedPdfTextExtractor(TaskCompletionSource<bool> gate) : IPdfTextExtractor
        {
            public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public async Task<string> ExtractTextAsync(string pdfPath, CancellationToken cancellationToken = default)
            {
                Started.TrySetResult(true);
                await gate.Task.WaitAsync(cancellationToken);
                return "Document text";
            }
        }

        private sealed class ImmediatePdfTextExtractor : IPdfTextExtractor
        {
            public Task<string> ExtractTextAsync(string pdfPath, CancellationToken cancellationToken = default)
            {
                return Task.FromResult("Document text");
            }
        }

        private sealed class StaticSummarizer(string output) : ILlamaSharpSummarizer
        {
            public Task<string> SummarizeAsync(string documentText, string modelPath, ModelDetails modelDetails, string systemPrompt, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(output);
            }
        }

        private sealed class TrackingSummarizer : ILlamaSharpSummarizer
        {
            private int _currentConcurrency;

            public int MaxConcurrency { get; private set; }

            public async Task<string> SummarizeAsync(string documentText, string modelPath, ModelDetails modelDetails, string systemPrompt, CancellationToken cancellationToken = default)
            {
                var current = Interlocked.Increment(ref _currentConcurrency);
                MaxConcurrency = Math.Max(MaxConcurrency, current);
                try
                {
                    await Task.Delay(120, cancellationToken);
                    return "# Summary";
                }
                finally
                {
                    Interlocked.Decrement(ref _currentConcurrency);
                }
            }
        }

        private sealed class StaticWriter(string outputPath) : IMarkdownOutputWriter
        {
            public Task<string> WriteAsync(string sourcePdfPath, string modelName, string markdownContent, CancellationToken cancellationToken = default)
            {
                var directory = Path.GetDirectoryName(outputPath)!;
                Directory.CreateDirectory(directory);
                File.WriteAllText(outputPath, markdownContent);
                return Task.FromResult(outputPath);
            }
        }

        private sealed class TimestampWriter(string tempRoot) : IMarkdownOutputWriter
        {
            public Task<string> WriteAsync(string sourcePdfPath, string modelName, string markdownContent, CancellationToken cancellationToken = default)
            {
                var outputPath = Path.Combine(tempRoot, "Output", $"{Path.GetFileNameWithoutExtension(sourcePdfPath)}-{DateTime.UtcNow:HHmmssfff}.md");
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                File.WriteAllText(outputPath, markdownContent);
                return Task.FromResult(outputPath);
            }
        }

        private sealed class NoopDownloader(string modelPath) : IHuggingFaceRepository
        {
            public Task<ModelDownloadResult> DownloadModelAsync(string repository, string fileName, string destinationPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
                if (!File.Exists(modelPath))
                {
                    File.WriteAllText(modelPath, "model");
                }

                return Task.FromResult(ModelDownloadResult.Success(modelPath, 5));
            }
        }
    }
}

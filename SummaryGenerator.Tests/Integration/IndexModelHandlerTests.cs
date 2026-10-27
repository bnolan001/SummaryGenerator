using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SummaryGenerator.Models;
using SummaryGenerator.Pages;
using SummaryGenerator.Repositories.HuggingFace;
using SummaryGenerator.Services;
using SummaryGenerator.Tests.Support;

namespace SummaryGenerator.Tests.Integration
{
    public class IndexModelHandlerTests
    {
        [Fact]
        public async Task OnPostEnqueueAsync_ValidPdf_QueuesTaskAndRedirects()
        {
            var tempRoot = CreateTempDirectory();
            try
            {
                var queue = new InMemoryQueue();
                var promptStore = new InMemoryPromptStore();
                var pageModel = CreatePageModel(tempRoot, queue, promptStore);

                pageModel.SelectedModelName = "Llama 3.3 (8B)";
                pageModel.SelectedPromptProfileId = "pme-mid-senior";
                pageModel.UploadedPdf = CreatePdfUpload("lesson.pdf");

                var result = await pageModel.OnPostEnqueueAsync(CancellationToken.None);

                Assert.IsType<RedirectToPageResult>(result);
                Assert.Single(queue.Tasks);
                var task = queue.Tasks.Values.Single();
                Assert.Equal("Llama 3.3 (8B)", task.Model.Name);
                Assert.Equal("PME Student (Mid-Senior)", task.PromptProfileName);
                Assert.True(File.Exists(task.SourcePdfPath));
            }
            finally
            {
                Directory.Delete(tempRoot, true);
            }
        }

        [Fact]
        public void OnGetStatus_ReturnsQueuedTaskStatusPayload()
        {
            var tempRoot = CreateTempDirectory();
            try
            {
                var queue = new InMemoryQueue();
                var promptStore = new InMemoryPromptStore();
                var pageModel = CreatePageModel(tempRoot, queue, promptStore);

                var outputPath = Path.Combine(tempRoot, "Output", "doc-model.md");
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                File.WriteAllText(outputPath, "# summary");

                var queuedTask = new ProcessingTask
                {
                    SourcePdfPath = "C:\\docs\\doc.pdf",
                    Model = new ModelDetails { Name = "Llama 3.3 (8B)", Repository = "repo", FileName = "model.gguf" },
                    PromptProfileId = "pme-mid-senior",
                    PromptProfileName = "PME Student (Mid-Senior)",
                    SystemPrompt = "Prompt",
                    StartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-10),
                    CompletedAtUtc = DateTimeOffset.UtcNow,
                    Status = ProcessingTaskStatus.Completed,
                    Result = new SummaryResult
                    {
                        TaskId = Guid.NewGuid(),
                        SourcePdfPath = "C:\\docs\\doc.pdf",
                        OutputMarkdownPath = outputPath,
                        ModelName = "Llama 3.3 (8B)",
                        CompletedAtUtc = DateTimeOffset.UtcNow
                    }
                };
                queue.AddTask(queuedTask);

                var result = pageModel.OnGetStatus();

                var json = Assert.IsType<JsonResult>(result);
                var rows = Assert.IsAssignableFrom<IEnumerable<object>>(json.Value).ToList();
                Assert.Single(rows);

                var row = rows[0];
                var status = row.GetType().GetProperty("Status")!.GetValue(row);
                var modelName = row.GetType().GetProperty("ModelName")!.GetValue(row);
                var hasOutput = row.GetType().GetProperty("HasOutput")!.GetValue(row);
                Assert.Equal("Completed", status);
                Assert.Equal("Llama 3.3 (8B)", modelName);
                Assert.Equal(true, hasOutput);
            }
            finally
            {
                Directory.Delete(tempRoot, true);
            }
        }

        [Fact]
        public void OnGetDownload_ExistingOutput_ReturnsFile()
        {
            var tempRoot = CreateTempDirectory();
            try
            {
                var queue = new InMemoryQueue();
                var promptStore = new InMemoryPromptStore();
                var pageModel = CreatePageModel(tempRoot, queue, promptStore);

                var outputPath = Path.Combine(tempRoot, "Output", "summary.md");
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                File.WriteAllText(outputPath, "# summary");

                var task = new ProcessingTask
                {
                    SourcePdfPath = "C:\\docs\\doc.pdf",
                    Model = new ModelDetails { Name = "Llama 3.3 (8B)", Repository = "repo", FileName = "model.gguf" },
                    PromptProfileId = "pme-mid-senior",
                    PromptProfileName = "PME Student (Mid-Senior)",
                    SystemPrompt = "Prompt",
                    Status = ProcessingTaskStatus.Completed,
                    Result = new SummaryResult
                    {
                        TaskId = Guid.NewGuid(),
                        SourcePdfPath = "C:\\docs\\doc.pdf",
                        OutputMarkdownPath = outputPath,
                        ModelName = "Llama 3.3 (8B)",
                        CompletedAtUtc = DateTimeOffset.UtcNow
                    }
                };
                queue.AddTask(task);

                var result = pageModel.OnGetDownload(task.Id);

                var fileResult = Assert.IsType<FileStreamResult>(result);
                Assert.Equal("text/markdown", fileResult.ContentType);
                Assert.Equal("summary.md", fileResult.FileDownloadName);
                fileResult.FileStream.Dispose();
            }
            finally
            {
                Directory.Delete(tempRoot, true);
            }
        }

        [Fact]
        public void OnGet_MarksRecommendedModelWithoutChangingSelection()
        {
            var tempRoot = CreateTempDirectory();
            try
            {
                // 8 GB * 40% = 3.2 GB, so only Phi-4 Mini (2.3 GB) fits; Llama stays the default selection.
                var pageModel = CreatePageModel(tempRoot, new InMemoryQueue(), new InMemoryPromptStore());

                pageModel.OnGet();

                Assert.Equal("Llama 3.3 (8B)", pageModel.SelectedModelName);
                Assert.Equal("Phi-4 Mini (3.8B) (recommended for this PC)", pageModel.ModelChoices.Single(c => c.Value == "Phi-4 Mini (3.8B)").Text);
                Assert.Equal("Llama 3.3 (8B)", pageModel.ModelChoices.Single(c => c.Value == "Llama 3.3 (8B)").Text);
                Assert.True(pageModel.ModelStatuses.Single(s => s.Name == "Phi-4 Mini (3.8B)").IsRecommended);
                Assert.False(pageModel.ModelStatuses.Single(s => s.Name == "Llama 3.3 (8B)").IsRecommended);
            }
            finally
            {
                Directory.Delete(tempRoot, true);
            }
        }

        private static IndexModel CreatePageModel(string contentRoot, InMemoryQueue queue, InMemoryPromptStore promptStore)
        {
            var modelsOptions = Options.Create(new ModelsOptions
            {
                DefaultModel = "Llama 3.3 (8B)",
                Choices =
                [
                    new ModelDetails
                    {
                        Name = "Phi-4 Mini (3.8B)",
                        Repository = "repo/phi",
                        FileName = "phi.gguf",
                        ApproxSizeGb = 2.3
                    },
                    new ModelDetails
                    {
                        Name = "Llama 3.3 (8B)",
                        Repository = "repo/llama",
                        FileName = "llama.gguf",
                        ApproxSizeGb = 4.6,
                        Default = true
                    }
                ]
            });
            var storageOptions = Options.Create(new StorageOptions
            {
                ModelsPath = "Downloads",
                OutputPath = "Output"
            });
            var uploadOptions = Options.Create(new UploadOptions
            {
                TempPath = "Uploads",
                MaxFileSizeBytes = 5 * 1024 * 1024,
                AllowedExtensions = [".pdf"]
            });
            var environment = new TestWebHostEnvironment
            {
                ContentRootPath = contentRoot
            };

            return new IndexModel(
                modelsOptions,
                storageOptions,
                uploadOptions,
                environment,
                queue,
                promptStore,
                new NoopHuggingFaceRepository(),
                new NoopAudioGenerator(),
                new FixedHardwareProfileService(totalMemoryBytes: 8L * 1024 * 1024 * 1024),
                Options.Create(new SummarizationOptions { IntegratedGpuMaxModelPercentOfRam = 40 }),
                NullLogger<IndexModel>.Instance);
        }

        private sealed class FixedHardwareProfileService(long totalMemoryBytes) : IHardwareProfileService
        {
            public HardwarePlan GetPlan() => HardwareProfileService.ComputePlan(8, totalMemoryBytes, GpuTier.ModernIntegrated);
        }

        private static IFormFile CreatePdfUpload(string fileName)
        {
            var content = "%PDF-1.7\nSample";
            var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
            return new FormFile(stream, 0, stream.Length, "UploadedPdf", fileName);
        }

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), $"summary-generator-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return path;
        }

        private sealed class NoopHuggingFaceRepository : IHuggingFaceRepository
        {
            public Task<ModelDownloadResult> DownloadModelAsync(string repository, string fileName, string destinationPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(ModelDownloadResult.Success(destinationPath, 0));
            }
        }

        private sealed class NoopAudioGenerator : IAudioGenerator
        {
            public IReadOnlyList<string> GetAvailableVoices() => Array.Empty<string>();

            public Task<string> GenerateAudioAsync(string markdownContent, string outputPath, string? selectedVoice = null, CancellationToken cancellationToken = default)
            {
                // Optional: create an empty file to simulate generated audio output
                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(outputPath, string.Empty);

                return Task.FromResult(outputPath);
            }
        }

        private sealed class InMemoryPromptStore : IPromptProfileStore
        {
            private readonly List<PromptProfile> _profiles =
            [
                new PromptProfile
                {
                    Id = "pme-mid-senior",
                    Name = "PME Student (Mid-Senior)",
                    Prompt = "Prompt",
                    IsBuiltIn = true
                }
            ];

            public string DefaultProfileId => "pme-mid-senior";

            public IReadOnlyList<PromptProfile> GetAll() => _profiles;

            public PromptProfile? GetById(string profileId)
            {
                return _profiles.FirstOrDefault(profile => string.Equals(profile.Id, profileId, StringComparison.OrdinalIgnoreCase));
            }

            public PromptProfile SaveCustom(string name, string prompt)
            {
                var profile = new PromptProfile
                {
                    Id = "custom-profile",
                    Name = name,
                    Prompt = prompt,
                    IsBuiltIn = false
                };
                _profiles.Add(profile);
                return profile;
            }
        }

        private sealed class InMemoryQueue : IProcessingQueue
        {
            public Dictionary<Guid, ProcessingTask> Tasks { get; } = new Dictionary<Guid, ProcessingTask>();

            public ProcessingTask Enqueue(string sourcePdfPath, ModelDetails model, PromptProfile promptProfile)
            {
                var task = new ProcessingTask
                {
                    SourcePdfPath = sourcePdfPath,
                    Model = model,
                    PromptProfileId = promptProfile.Id,
                    PromptProfileName = promptProfile.Name,
                    SystemPrompt = promptProfile.Prompt
                };
                Tasks[task.Id] = task;
                return task;
            }

            // Implemented to satisfy IProcessingQueue
            public ProcessingTask EnqueueAudio(string sourceMarkdownPath, string? selectedVoice = null)
            {
                var task = new ProcessingTask
                {
                    SourceMarkdownPath = sourceMarkdownPath,
                    SelectedVoice = selectedVoice
                };
                Tasks[task.Id] = task;
                return task;
            }

            // Implemented to satisfy IProcessingQueue
            public ProcessingTask EnqueueAudioFromSummary(Guid summaryTaskId, string? selectedVoice = null)
            {
                var task = new ProcessingTask
                {
                    // Link to the originating summary task id could be set in a real implementation.
                    SelectedVoice = selectedVoice
                };
                Tasks[task.Id] = task;
                return task;
            }

            public bool TryGetTask(Guid taskId, out ProcessingTask? task)
            {
                var found = Tasks.TryGetValue(taskId, out var localTask);
                task = localTask;
                return found;
            }

            public IReadOnlyCollection<ProcessingTask> GetAllTasks()
            {
                return Tasks.Values.ToList();
            }

            public void AddTask(ProcessingTask task)
            {
                Tasks[task.Id] = task;
            }
        }
    }
}

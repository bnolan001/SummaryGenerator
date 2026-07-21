using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Options;
using SummaryGenerator.Models;
using SummaryGenerator.Repositories.HuggingFace;
using SummaryGenerator.Services;

namespace SummaryGenerator.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ModelsOptions _modelsOptions;
        private readonly StorageOptions _storageOptions;
        private readonly UploadOptions _uploadOptions;
        private readonly IWebHostEnvironment _environment;
        private readonly IProcessingQueue _processingQueue;
        private readonly IPromptProfileStore _promptProfileStore;
        private readonly IHuggingFaceRepository _huggingFaceRepository;
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(
            IOptions<ModelsOptions> modelsOptions,
            IOptions<StorageOptions> storageOptions,
            IOptions<UploadOptions> uploadOptions,
            IWebHostEnvironment environment,
            IProcessingQueue processingQueue,
            IPromptProfileStore promptProfileStore,
            IHuggingFaceRepository huggingFaceRepository,
            ILogger<IndexModel> logger)
        {
            _modelsOptions = modelsOptions.Value;
            _storageOptions = storageOptions.Value;
            _uploadOptions = uploadOptions.Value;
            _environment = environment;
            _processingQueue = processingQueue;
            _promptProfileStore = promptProfileStore;
            _huggingFaceRepository = huggingFaceRepository;
            _logger = logger;
        }

        [BindProperty]
        public string SelectedModelName { get; set; } = string.Empty;

        [BindProperty]
        public string SelectedPromptProfileId { get; set; } = string.Empty;

        [BindProperty]
        public string? CustomPromptName { get; set; }

        [BindProperty]
        public string? CustomPromptText { get; set; }

        [BindProperty]
        public IFormFile? UploadedPdf { get; set; }

        public List<SelectListItem> ModelChoices { get; private set; } = [];

        public List<SelectListItem> PromptChoices { get; private set; } = [];

        public List<TaskSummaryViewModel> TaskSummaries { get; private set; } = [];

        public List<ModelDownloadStatusViewModel> ModelStatuses { get; private set; } = [];

        [TempData]
        public string? StatusMessage { get; set; }

        [TempData]
        public string? ErrorMessage { get; set; }

        public void OnGet()
        {
            LoadPageData();
        }

        public async Task<IActionResult> OnPostEnqueueAsync(CancellationToken cancellationToken)
        {
            LoadPageData();
            var model = ResolveModel(SelectedModelName);
            PromptProfile? selectedPromptProfile = null;

            if (!string.IsNullOrWhiteSpace(CustomPromptText))
            {
                if (string.IsNullOrWhiteSpace(CustomPromptName))
                {
                    ModelState.AddModelError(nameof(CustomPromptName), "Provide a name to save the custom prompt.");
                }
                else
                {
                    try
                    {
                        selectedPromptProfile = _promptProfileStore.SaveCustom(CustomPromptName, CustomPromptText);
                        SelectedPromptProfileId = selectedPromptProfile.Id;
                    }
                    catch (Exception ex) when (ex is ArgumentException or IOException)
                    {
                        _logger.LogError(ex, "Failed to save custom prompt profile {PromptName}.", CustomPromptName);
                        ModelState.AddModelError(nameof(CustomPromptText), $"Failed to save custom prompt: {ex.Message}");
                    }
                }
            }

            selectedPromptProfile ??= _promptProfileStore.GetById(SelectedPromptProfileId);
            if (selectedPromptProfile is null)
            {
                ModelState.AddModelError(nameof(SelectedPromptProfileId), "Select a valid summary persona or prompt.");
            }

            if (model is null)
            {
                ModelState.AddModelError(nameof(SelectedModelName), "Select a valid model.");
            }

            if (UploadedPdf is null || UploadedPdf.Length == 0)
            {
                ModelState.AddModelError(nameof(UploadedPdf), "Select a PDF file to upload.");
            }
            else if (UploadedPdf.Length > _uploadOptions.MaxFileSizeBytes)
            {
                ModelState.AddModelError(
                    nameof(UploadedPdf),
                    $"File exceeds max size of {_uploadOptions.MaxFileSizeBytes / (1024 * 1024)} MB.");
            }
            else if (!IsAllowedExtension(UploadedPdf.FileName))
            {
                ModelState.AddModelError(nameof(UploadedPdf), "Only PDF files are allowed.");
            }
            else if (!await HasPdfSignatureAsync(UploadedPdf, cancellationToken))
            {
                ModelState.AddModelError(nameof(UploadedPdf), "Uploaded file does not appear to be a valid PDF.");
            }

            if (!ModelState.IsValid || model is null || UploadedPdf is null || selectedPromptProfile is null)
            {
                return Page();
            }

            var uploadsRoot = ResolveStorageRoot(_uploadOptions.TempPath);
            Directory.CreateDirectory(uploadsRoot);

            var fileName = $"{Guid.NewGuid():N}-{Path.GetFileName(UploadedPdf.FileName)}";
            var uploadedPdfPath = Path.Combine(uploadsRoot, fileName);

            try
            {
                await using (var stream = new FileStream(uploadedPdfPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await UploadedPdf.CopyToAsync(stream, cancellationToken);
                }

                var task = _processingQueue.Enqueue(uploadedPdfPath, model, selectedPromptProfile);
                StatusMessage = $"Queued file '{UploadedPdf.FileName}' with task ID {task.Id} using prompt '{selectedPromptProfile.Name}'.";
                _logger.LogInformation("Queued PDF upload {FileName} as task {TaskId}.", UploadedPdf.FileName, task.Id);
                return RedirectToPage();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to queue uploaded file {FileName}.", UploadedPdf.FileName);

                if (System.IO.File.Exists(uploadedPdfPath))
                {
                    try
                    {
                        System.IO.File.Delete(uploadedPdfPath);
                    }
                    catch (Exception cleanupEx)
                    {
                        _logger.LogWarning(cleanupEx, "Failed to cleanup upload file {UploadPath}.", uploadedPdfPath);
                    }
                }

                ErrorMessage = $"Failed to queue file '{UploadedPdf.FileName}': {ex.Message}";
                return RedirectToPage();
            }
        }

        public async Task<IActionResult> OnPostDownloadModelAsync(string? modelName, CancellationToken cancellationToken)
        {
            LoadPageData();
            var resolvedModelName = string.IsNullOrWhiteSpace(modelName) ? SelectedModelName : modelName;
            var model = ResolveModel(resolvedModelName);
            if (model is null)
            {
                ModelState.AddModelError(nameof(SelectedModelName), "Select a valid model.");
                return Page();
            }

            var modelPath = GetModelPath(model);
            if (System.IO.File.Exists(modelPath))
            {
                StatusMessage = $"Model '{model.Name}' is already downloaded and will be reused.";
                return RedirectToPage();
            }

            ModelDownloadResult result;
            try
            {
                result = await _huggingFaceRepository.DownloadModelAsync(
                    model.Repository,
                    model.FileName,
                    modelPath,
                    progress: null,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Model download request failed for {ModelName}.", model.Name);
                ErrorMessage = $"Model download failed for '{model.Name}': {ex.Message}";
                return RedirectToPage();
            }

            if (!result.Succeeded)
            {
                _logger.LogWarning(
                    "Model download failed for {ModelName}: {ErrorMessage}",
                    model.Name,
                    result.ErrorMessage);
                ErrorMessage = result.ErrorMessage ?? "Model download failed.";
                return RedirectToPage();
            }

            StatusMessage = $"Downloaded model '{model.Name}'.";
            _logger.LogInformation("Model {ModelName} downloaded to {ModelPath}.", model.Name, modelPath);
            return RedirectToPage();
        }

        public IActionResult OnGetStatus()
        {
            var statuses = _processingQueue.GetAllTasks()
                .Select(task => new
                {
                    task.Id,
                    task.SourcePdfPath,
                    ModelName = task.Model.Name,
                    PromptProfileName = task.PromptProfileName,
                    Status = task.Status.ToString(),
                    task.CreatedAtUtc,
                    task.StartedAtUtc,
                    task.CompletedAtUtc,
                    ElapsedSeconds = CalculateElapsedSeconds(task.StartedAtUtc, task.CompletedAtUtc),
                    task.ErrorMessage,
                    HasOutput = task.Result is not null && System.IO.File.Exists(task.Result.OutputMarkdownPath)
                });

            return new JsonResult(statuses);
        }

        public IActionResult OnGetDownload(Guid taskId)
        {
            if (!_processingQueue.TryGetTask(taskId, out var task) ||
                task?.Result is null ||
                !System.IO.File.Exists(task.Result.OutputMarkdownPath))
            {
                return NotFound();
            }

            var fileName = Path.GetFileName(task.Result.OutputMarkdownPath);
            var stream = new FileStream(task.Result.OutputMarkdownPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return File(stream, "text/markdown", fileName);
        }

        private List<ModelDetails> GetModels()
        {
            return _modelsOptions.Choices ?? new List<ModelDetails>();
        }

        private void LoadPageData()
        {
            var models = GetModels();
            var resolvedDefaultModelName = models.FirstOrDefault(model => model.Default)?.Name
                ?? models.FirstOrDefault(model =>
                    string.Equals(model.Name, _modelsOptions.DefaultModel, StringComparison.OrdinalIgnoreCase))
                ?.Name;
            var hasValidSelection = models.Any(model =>
                string.Equals(model.Name, SelectedModelName, StringComparison.OrdinalIgnoreCase));

            if (!hasValidSelection)
            {
                SelectedModelName = resolvedDefaultModelName
                    ?? models.FirstOrDefault()?.Name
                    ?? string.Empty;
            }
            if (string.IsNullOrWhiteSpace(SelectedPromptProfileId))
            {
                SelectedPromptProfileId = _promptProfileStore.DefaultProfileId;
            }

            ModelChoices = models
                .Select(model => new SelectListItem
                {
                    Text = model.Name,
                    Value = model.Name,
                    Selected = string.Equals(model.Name, SelectedModelName, StringComparison.OrdinalIgnoreCase)
                })
                .ToList();

            PromptChoices = _promptProfileStore.GetAll()
                .Select(promptProfile => new SelectListItem
                {
                    Text = promptProfile.IsBuiltIn ? $"{promptProfile.Name} (Built-in)" : $"{promptProfile.Name} (Custom)",
                    Value = promptProfile.Id,
                    Selected = string.Equals(promptProfile.Id, SelectedPromptProfileId, StringComparison.OrdinalIgnoreCase)
                })
                .ToList();

            ModelStatuses = models
                .Select(model =>
                {
                    var modelPath = GetModelPath(model);
                    var exists = System.IO.File.Exists(modelPath);
                    long? sizeBytes = null;

                    if (exists)
                    {
                        sizeBytes = new FileInfo(modelPath).Length;
                    }

                    return new ModelDownloadStatusViewModel
                    {
                        Name = model.Name,
                        Repository = model.Repository,
                        FileName = model.FileName,
                        IsDownloaded = exists,
                        LocalPath = modelPath,
                        SizeBytes = sizeBytes
                    };
                })
                .ToList();

            TaskSummaries = _processingQueue.GetAllTasks()
                .Select(task => new TaskSummaryViewModel
                {
                    Id = task.Id,
                    SourceFileName = Path.GetFileName(task.SourcePdfPath),
                    ModelName = task.Model.Name,
                    PromptProfileName = task.PromptProfileName,
                    Status = task.Status.ToString(),
                    CreatedAtUtc = task.CreatedAtUtc,
                    StartedAtUtc = task.StartedAtUtc,
                    CompletedAtUtc = task.CompletedAtUtc,
                    ElapsedSeconds = CalculateElapsedSeconds(task.StartedAtUtc, task.CompletedAtUtc),
                    ErrorMessage = task.ErrorMessage,
                    HasOutput = task.Result is not null && System.IO.File.Exists(task.Result.OutputMarkdownPath)
                })
                .ToList();
        }

        private ModelDetails? ResolveModel(string modelName) =>
            GetModels().FirstOrDefault(model =>
                string.Equals(model.Name, modelName, StringComparison.Ordinal));

        private string ResolveStorageRoot(string configuredPath)
        {
            if (Path.IsPathRooted(configuredPath))
            {
                return configuredPath;
            }

            return Path.Combine(_environment.ContentRootPath, configuredPath);
        }

        private string GetModelPath(ModelDetails model) =>
            Path.Combine(ResolveStorageRoot(_storageOptions.ModelsPath), model.FileName);

        private bool IsAllowedExtension(string fileName)
        {
            var extension = Path.GetExtension(fileName);
            return _uploadOptions.AllowedExtensions.Any(
                allowed => string.Equals(allowed, extension, StringComparison.OrdinalIgnoreCase));
        }

        private static async Task<bool> HasPdfSignatureAsync(IFormFile file, CancellationToken cancellationToken)
        {
            await using var stream = file.OpenReadStream();
            var header = new byte[5];
            var read = await stream.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);
            if (read < 5)
            {
                return false;
            }

            return header[0] == '%' &&
                   header[1] == 'P' &&
                   header[2] == 'D' &&
                   header[3] == 'F' &&
                   header[4] == '-';
        }

        private static long? CalculateElapsedSeconds(DateTimeOffset? startedAtUtc, DateTimeOffset? completedAtUtc)
        {
            if (!startedAtUtc.HasValue)
            {
                return null;
            }

            var end = completedAtUtc ?? DateTimeOffset.UtcNow;
            var elapsed = end - startedAtUtc.Value;
            return elapsed.TotalSeconds < 0 ? 0 : (long)elapsed.TotalSeconds;
        }

        public class TaskSummaryViewModel
        {
            public required Guid Id { get; init; }

            public required string SourceFileName { get; init; }

            public required string ModelName { get; init; }

            public required string PromptProfileName { get; init; }

            public required string Status { get; init; }

            public required DateTimeOffset CreatedAtUtc { get; init; }

            public DateTimeOffset? StartedAtUtc { get; init; }

            public DateTimeOffset? CompletedAtUtc { get; init; }

            public long? ElapsedSeconds { get; init; }

            public string? ErrorMessage { get; init; }

            public bool HasOutput { get; init; }
        }

        public class ModelDownloadStatusViewModel
        {
            public required string Name { get; init; }

            public required string Repository { get; init; }

            public required string FileName { get; init; }

            public required bool IsDownloaded { get; init; }

            public required string LocalPath { get; init; }

            public long? SizeBytes { get; init; }
        }
    }
}

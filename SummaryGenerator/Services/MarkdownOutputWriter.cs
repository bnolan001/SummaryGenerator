using System.Text;
using Microsoft.Extensions.Options;
using SummaryGenerator.Models;

namespace SummaryGenerator.Services
{
    public class MarkdownOutputWriter(
        IOptions<StorageOptions> options,
        IWebHostEnvironment environment,
        ILogger<MarkdownOutputWriter> logger) : IMarkdownOutputWriter
    {
        private readonly StorageOptions _options = options.Value;
        private readonly IWebHostEnvironment _environment = environment;
        private readonly ILogger<MarkdownOutputWriter> _logger = logger;

        public async Task<string> WriteAsync(
            string sourcePdfPath,
            string modelName,
            string markdownContent,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sourcePdfPath))
            {
                throw new ArgumentException("Source PDF path is required.", nameof(sourcePdfPath));
            }
            if (string.IsNullOrWhiteSpace(modelName))
            {
                throw new ArgumentException("Model name is required.", nameof(modelName));
            }

            if (string.IsNullOrWhiteSpace(markdownContent))
            {
                throw new ArgumentException("Markdown content is required.", nameof(markdownContent));
            }

            var outputRoot = ResolveStorageRoot(_options.OutputPath);
            Directory.CreateDirectory(outputRoot);

            var sourceName = Path.GetFileNameWithoutExtension(sourcePdfPath);
            var safeBaseName = MakeSafeFileName(sourceName);
            var safeModelName = MakeSafeFileName(modelName);
            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
            var outputFileName = $"{safeBaseName}-{safeModelName}-{timestamp}.md";
            var outputPath = Path.Combine(outputRoot, outputFileName);

            try
            {
                await File.WriteAllTextAsync(outputPath, markdownContent, Encoding.UTF8, cancellationToken);
                _logger.LogInformation("Wrote markdown output to {OutputPath}.", outputPath);
                return outputPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed writing markdown output to {OutputPath}.", outputPath);
                throw new IOException($"Unable to write markdown output to '{outputPath}'.", ex);
            }
        }

        private static string MakeSafeFileName(string value)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var cleaned = value
                .Where(ch => !invalidChars.Contains(ch))
                .ToArray();

            var safe = new string(cleaned).Trim();
            return string.IsNullOrWhiteSpace(safe) ? "summary" : safe;
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

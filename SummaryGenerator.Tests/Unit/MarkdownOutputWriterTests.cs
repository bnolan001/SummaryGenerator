using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SummaryGenerator.Models;
using SummaryGenerator.Services;
using SummaryGenerator.Tests.Support;

namespace SummaryGenerator.Tests.Unit
{
    public class MarkdownOutputWriterTests
    {
        [Fact]
        public async Task WriteAsync_IncludesModelNameInOutputFileName()
        {
            var tempRoot = CreateTempDirectory();
            try
            {
                var options = Options.Create(new StorageOptions
                {
                    OutputPath = "Output"
                });
                var environment = new TestWebHostEnvironment
                {
                    ContentRootPath = tempRoot
                };

                var writer = new MarkdownOutputWriter(options, environment, NullLogger<MarkdownOutputWriter>.Instance);
                var outputPath = await writer.WriteAsync(
                    "C:\\docs\\module-1.pdf",
                    "Llama 3.3 (8B)",
                    "# Summary");

                var fileName = Path.GetFileName(outputPath);
                Assert.Contains("module-1", fileName, StringComparison.Ordinal);
                Assert.Contains("Llama 3.3 (8B)", fileName, StringComparison.Ordinal);
                Assert.EndsWith(".md", fileName, StringComparison.Ordinal);
                Assert.True(File.Exists(outputPath));
            }
            finally
            {
                Directory.Delete(tempRoot, true);
            }
        }

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), $"summary-generator-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return path;
        }
    }
}

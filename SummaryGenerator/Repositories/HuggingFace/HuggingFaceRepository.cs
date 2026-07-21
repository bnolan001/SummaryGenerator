using Microsoft.Extensions.Options;
using SummaryGenerator.Models;

namespace SummaryGenerator.Repositories.HuggingFace
{
    public interface IHuggingFaceRepository
    {
        Task<ModelDownloadResult> DownloadModelAsync(
            string repository,
            string fileName,
            string destinationPath,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default);
    }

    public class HuggingFaceRepository(
        HttpClient httpClient,
        IOptions<HuggingFaceOptions> options,
        ILogger<HuggingFaceRepository> logger) : IHuggingFaceRepository
    {
        private readonly HuggingFaceOptions _options = options.Value;

        public async Task<ModelDownloadResult> DownloadModelAsync(
            string repository,
            string fileName,
            string destinationPath,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(repository))
            {
                throw new ArgumentException("Repository is required.", nameof(repository));
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("File name is required.", nameof(fileName));
            }

            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                throw new ArgumentException("Destination path is required.", nameof(destinationPath));
            }

            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            var url = $"https://huggingface.co/{repository}/resolve/main/{fileName}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            if (!string.IsNullOrWhiteSpace(_options.AccessToken))
            {
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.AccessToken);
            }

            try
            {
                using var response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                response.EnsureSuccessStatusCode();

                var fileSize = response.Content.Headers.ContentLength ?? 0;
                await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var fileStream = new FileStream(
                    destinationPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    true);

                var buffer = new byte[81920];
                long totalReadBytes = 0;
                int readBytes;

                while ((readBytes = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, readBytes), cancellationToken);
                    totalReadBytes += readBytes;

                    if (fileSize > 0)
                    {
                        var percentage = (double)totalReadBytes / fileSize * 100;
                        progress?.Report(percentage);
                    }
                }

                progress?.Report(100);
                logger.LogInformation("Downloaded model {Repository}/{FileName} to {DestinationPath}", repository, fileName, destinationPath);
                return ModelDownloadResult.Success(destinationPath, totalReadBytes);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to download model {Repository}/{FileName}", repository, fileName);
                return ModelDownloadResult.Failure(destinationPath, ex.Message);
            }
        }
    }
}

namespace SummaryGenerator.Models
{
    public class ModelDownloadResult
    {
        public bool Succeeded { get; init; }

        public required string DestinationPath { get; init; }

        public long BytesDownloaded { get; init; }

        public string? ErrorMessage { get; init; }

        public static ModelDownloadResult Success(string destinationPath, long bytesDownloaded) =>
            new()
            {
                Succeeded = true,
                DestinationPath = destinationPath,
                BytesDownloaded = bytesDownloaded
            };

        public static ModelDownloadResult Failure(string destinationPath, string errorMessage) =>
            new()
            {
                Succeeded = false,
                DestinationPath = destinationPath,
                ErrorMessage = errorMessage
            };
    }
}

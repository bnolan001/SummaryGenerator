namespace SummaryGenerator.Services
{
    public interface IMarkdownOutputWriter
    {
        Task<string> WriteAsync(
            string sourcePdfPath,
            string modelName,
            string markdownContent,
            CancellationToken cancellationToken = default);
    }
}

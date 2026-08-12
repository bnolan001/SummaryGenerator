using SummaryGenerator.Models;

namespace SummaryGenerator.Services
{
    public interface ILlamaSharpSummarizer
    {
        Task<string> SummarizeAsync(
            string documentText,
            string modelPath,
            ModelDetails modelDetails,
            string systemPrompt,
            CancellationToken cancellationToken = default);
    }
}

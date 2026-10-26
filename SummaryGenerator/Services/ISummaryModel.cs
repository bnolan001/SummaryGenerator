namespace SummaryGenerator.Services
{
    /// <summary>
    /// The model operations the map-reduce pipeline needs: counting tokens of a fully templated
    /// prompt and generating cleaned text. Lets the pipeline be tested without a native model.
    /// </summary>
    public interface ISummaryModel
    {
        int CountPromptTokens(string instruction, string content);

        Task<string> GenerateAsync(string instruction, string content, int maxOutputTokens, CancellationToken cancellationToken);
    }
}

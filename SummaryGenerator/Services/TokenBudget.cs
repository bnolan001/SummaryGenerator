namespace SummaryGenerator.Services
{
    /// <summary>
    /// Token limits for one summarization run. Output caps are chosen so any two intermediate
    /// summaries always fit in one combine prompt, and any single summary fits the final prompt,
    /// which guarantees every reduce level makes progress.
    /// </summary>
    public sealed record TokenBudget(
        uint ContextSize,
        int FinalOutputTokens,
        int IntermediateOutputTokens,
        int MaxFinalPromptTokens,
        int MaxIntermediatePromptTokens)
    {
        public const int SafetyMarginTokens = 64;
        public const int MinimumUsableTokens = 256;

        public static TokenBudget Compute(uint contextSize, int maxOutputTokens, int promptOverheadTokens)
        {
            var usable = (int)contextSize - promptOverheadTokens - SafetyMarginTokens;
            if (usable < MinimumUsableTokens)
            {
                throw new InvalidOperationException(
                    $"Context size {contextSize} is too small for the prompt template ({promptOverheadTokens} tokens).");
            }

            var finalOutputTokens = Math.Min(maxOutputTokens, usable / 2);
            var intermediateOutputTokens = Math.Min(maxOutputTokens, (usable / 3) - 8);

            return new TokenBudget(
                contextSize,
                finalOutputTokens,
                intermediateOutputTokens,
                (int)contextSize - finalOutputTokens - SafetyMarginTokens,
                (int)contextSize - intermediateOutputTokens - SafetyMarginTokens);
        }
    }
}

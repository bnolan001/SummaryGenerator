namespace SummaryGenerator.Services
{
    /// <summary>
    /// Summarizes a document of any length within a fixed context window: one call when it fits,
    /// otherwise token-bounded chunk summaries combined level by level until the final prompt fits.
    /// </summary>
    public sealed class MapReduceSummarizer(ISummaryModel model, ILogger logger)
    {
        public const string ChunkInstruction =
            "Summarize the following section of the document, focusing on key operational and strategic points:";

        public const string CombineInstruction =
            "Combine the following partial summaries into a single consolidated summary, preserving all key points:";

        public static readonly string SummarySeparator = Environment.NewLine + "---" + Environment.NewLine;

        public async Task<string> SummarizeAsync(
            string documentText,
            string systemPrompt,
            uint contextSize,
            int maxOutputTokens,
            CancellationToken cancellationToken)
        {
            var overhead = new[] { systemPrompt, ChunkInstruction, CombineInstruction }
                .Max(instruction => model.CountPromptTokens(instruction, string.Empty));
            var budget = TokenBudget.Compute(contextSize, maxOutputTokens, overhead);

            if (model.CountPromptTokens(systemPrompt, documentText) <= budget.MaxFinalPromptTokens)
            {
                return await model.GenerateAsync(systemPrompt, documentText, budget.FinalOutputTokens, cancellationToken);
            }

            var chunks = DocumentChunker.SplitToTokenBudget(
                documentText,
                text => model.CountPromptTokens(ChunkInstruction, text),
                budget.MaxIntermediatePromptTokens);

            logger.LogInformation(
                "Document exceeds the {Budget}-token prompt budget. Summarizing {Count} chunks.",
                budget.MaxFinalPromptTokens, chunks.Count);

            var summaries = new List<string>(chunks.Count);
            foreach (var chunk in chunks)
            {
                summaries.Add(await model.GenerateAsync(ChunkInstruction, chunk, budget.IntermediateOutputTokens, cancellationToken));
            }

            return await ReduceAsync(summaries, systemPrompt, budget, cancellationToken);
        }

        private async Task<string> ReduceAsync(
            List<string> summaries,
            string systemPrompt,
            TokenBudget budget,
            CancellationToken cancellationToken)
        {
            var combineOverhead = model.CountPromptTokens(CombineInstruction, string.Empty);
            var separatorTokens = model.CountPromptTokens(CombineInstruction, SummarySeparator) - combineOverhead;
            var level = 0;

            while (true)
            {
                var combined = string.Join(SummarySeparator, summaries);
                var combinedTokens = model.CountPromptTokens(systemPrompt, combined);

                if (combinedTokens <= budget.MaxFinalPromptTokens)
                {
                    return await model.GenerateAsync(systemPrompt, combined, budget.FinalOutputTokens, cancellationToken);
                }

                level++;
                logger.LogInformation(
                    "Reduce level {Level}: {Count} summaries ({Tokens} tokens) exceed the {Budget}-token final prompt budget. Combining in groups.",
                    level, summaries.Count, combinedTokens, budget.MaxFinalPromptTokens);

                var groups = DocumentChunker.GroupByBudget(
                    summaries,
                    summary => model.CountPromptTokens(CombineInstruction, summary) - combineOverhead + separatorTokens,
                    budget.MaxIntermediatePromptTokens - combineOverhead);

                var nextLevel = new List<string>(groups.Count);
                foreach (var group in groups)
                {
                    nextLevel.Add(group.Count == 1
                        ? group[0]
                        : await model.GenerateAsync(
                            CombineInstruction,
                            string.Join(SummarySeparator, group),
                            budget.IntermediateOutputTokens,
                            cancellationToken));
                }

                if (nextLevel.Count >= summaries.Count)
                {
                    throw new InvalidOperationException(
                        $"Reduce step made no progress: {summaries.Count} summaries could not be combined within the {budget.MaxIntermediatePromptTokens}-token prompt budget.");
                }

                summaries = nextLevel;
            }
        }
    }
}

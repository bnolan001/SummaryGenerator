using System.Text;
using LLama;
using LLama.Common;
using LLama.Sampling;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SummaryGenerator.Models;

namespace SummaryGenerator.Services
{
    public class LlamaSharpSummarizer(
        IOptions<SummarizationOptions> options,
        IHardwareProfileService hardwareProfileService,
        ILogger<LlamaSharpSummarizer> logger) : ILlamaSharpSummarizer
    {
        private readonly SummarizationOptions _options = options.Value;

        public async Task<string> SummarizeAsync(
            string documentText,
            string modelPath,
            ModelDetails modelDetails,
            string systemPrompt,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(documentText))
            {
                throw new ArgumentException("Document text is required.", nameof(documentText));
            }

            if (string.IsNullOrWhiteSpace(modelPath))
            {
                throw new ArgumentException("Model path is required.", nameof(modelPath));
            }

            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException("Model file was not found.", modelPath);
            }
            if (modelDetails is null)
            {
                throw new ArgumentNullException(nameof(modelDetails));
            }
            if (string.IsNullOrWhiteSpace(systemPrompt))
            {
                throw new ArgumentException("System prompt is required.", nameof(systemPrompt));
            }

            try
            {
                var hardwarePlan = hardwareProfileService.GetPlan();
                var resolvedContextSize = InferenceParameterResolver.ResolveContextSize(_options, modelDetails, _options.HardwareAutoTune, hardwarePlan);
                var resolvedThreads = InferenceParameterResolver.ResolveThreads(_options.Threads, hardwarePlan);
                var resolvedGpuLayerCount = InferenceParameterResolver.ResolveGpuLayerCount(_options.GpuLayerCount, _options.HardwareAutoTune, hardwarePlan);
                var resolvedBatchSize = InferenceParameterResolver.ResolveBatchSize(_options.BatchSize, _options.HardwareAutoTune, hardwarePlan);

                var modelParams = new ModelParams(modelPath)
                {
                    ContextSize = resolvedContextSize,
                    Threads = resolvedThreads,
                    GpuLayerCount = resolvedGpuLayerCount,
                    FlashAttention = _options.FlashAttention
                };

                if (resolvedBatchSize > 0)
                {
                    modelParams.BatchSize = (uint)resolvedBatchSize;
                    modelParams.UBatchSize = (uint)resolvedBatchSize;
                }

                logger.LogInformation(
                    "Resolved inference parameters: ContextSize={ContextSize}, Threads={Threads}, GpuLayerCount={GpuLayerCount}, BatchSize={BatchSize}, HardwareAutoTune={HardwareAutoTune}, GpuTier={GpuTier}",
                    resolvedContextSize, resolvedThreads, resolvedGpuLayerCount, resolvedBatchSize, _options.HardwareAutoTune, hardwarePlan.GpuTier);

                using var weights = LLamaWeights.LoadFromFile(modelParams);
                var executor = new StatelessExecutor(weights, modelParams);
                var samplingPipeline = new DefaultSamplingPipeline
                {
                    Temperature = _options.Temperature,
                    TopP = _options.TopP,
                    RepeatPenalty = _options.RepeatPenalty
                };
                var inferenceParams = new InferenceParams
                {
                    MaxTokens = _options.MaxTokens,
                    AntiPrompts = _options.StopPhrases,
                    SamplingPipeline = samplingPipeline,
                    OverflowStrategy = LLama.Common.ContextOverflowStrategy.TruncateAndReprefill
                };

                int charBudget = DocumentChunker.EstimateCharBudget(resolvedContextSize, _options.ContextSafetyReserveTokens);

                if (documentText.Length <= charBudget)
                {
                    var prompt = BuildPrompt(modelDetails.Name, systemPrompt, documentText);
                    var summary = CleanSummary(await RunInferenceAsync(executor, prompt, inferenceParams, cancellationToken));

                    logger.LogInformation(
                        "Summarization completed using model {ModelPath}. ContextSize={ContextSize}. Output chars: {CharacterCount}",
                        modelPath,
                        resolvedContextSize,
                        summary.Length);
                    return summary;
                }

                logger.LogInformation(
                    "Document is large ({Length} chars, budget {Budget} chars). Using map-reduce chunking.",
                    documentText.Length,
                    charBudget);

                var chunks = DocumentChunker.SplitIntoChunks(documentText, charBudget);
                var chunkSummaries = new List<string>();

                foreach (var chunkText in chunks)
                {
                    var chunkPrompt = BuildPrompt(
                        modelDetails.Name,
                        "Summarize the following section of the document, focusing on key operational and strategic points:",
                        chunkText);

                    chunkSummaries.Add(CleanSummary(await RunInferenceAsync(executor, chunkPrompt, inferenceParams, cancellationToken)));
                }

                logger.LogInformation("Chunking complete. Performing map-reduce summarization on {Count} chunks.", chunkSummaries.Count);

                var finalSummary = await ReduceSummariesAsync(
                    executor,
                    chunkSummaries,
                    modelDetails.Name,
                    systemPrompt,
                    inferenceParams,
                    charBudget,
                    cancellationToken);

                logger.LogInformation(
                    "Summarization completed map-reduce using model {ModelPath}. ContextSize={ContextSize}. Output chars: {CharacterCount}",
                    modelPath,
                    resolvedContextSize,
                    finalSummary.Length);
                return finalSummary;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Summarization failed using model {ModelPath}.", modelPath);
                throw new InvalidOperationException("LLM summarization failed.", ex);
            }
        }

        private static string BuildPrompt(string modelName, string systemPrompt, string documentText)
        {
            var name = modelName?.ToLowerInvariant() ?? string.Empty;

            if (name.Contains("gemma"))
            {
                return $"<start_of_turn>user\n{systemPrompt.Trim()}\n\n{documentText}<end_of_turn>\n<start_of_turn>model\n";
            }
            if (name.Contains("qwen") || name.Contains("phi"))
            {
                return $"<|im_start|>system\n{systemPrompt.Trim()}<|im_end|>\n<|im_start|>user\n{documentText}<|im_end|>\n<|im_start|>assistant\n";
            }
            if (name.Contains("llama"))
            {
                return $"<|begin_of_text|><|start_header_id|>system<|end_header_id|>\n\n{systemPrompt.Trim()}<|eot_id|><|start_header_id|>user<|end_header_id|>\n\n{documentText}<|eot_id|><|start_header_id|>assistant<|end_header_id|>\n\n";
            }

            return $"### Instruction:\n{systemPrompt.Trim()}\n\n### Input:\n{documentText}\n\n### Response:\n";
        }

        private const int MaxReduceDepth = 8;

        /// <summary>
        /// Combines chunk summaries into one final summary, guaranteeing every prompt sent
        /// to the model fits within <paramref name="charBudget"/>. When the combined text of
        /// all summaries is too large for a single prompt, summaries are grouped into
        /// budget-sized batches, each batch is combined with one inference call, and the
        /// process recurses on the resulting (strictly fewer) summaries until one final
        /// prompt fits.
        /// </summary>
        private async Task<string> ReduceSummariesAsync(
            StatelessExecutor executor,
            IReadOnlyList<string> summaries,
            string modelName,
            string systemPrompt,
            InferenceParams inferenceParams,
            int charBudget,
            CancellationToken cancellationToken,
            int depth = 0)
        {
            var combined = string.Join(Environment.NewLine + "---" + Environment.NewLine, summaries);

            if (summaries.Count == 1 || combined.Length <= charBudget)
            {
                var finalPrompt = BuildPrompt(modelName, systemPrompt, combined);
                return CleanSummary(await RunInferenceAsync(executor, finalPrompt, inferenceParams, cancellationToken));
            }

            if (depth >= MaxReduceDepth)
            {
                throw new InvalidOperationException(
                    $"Document could not be summarized: the reduce step exceeded {MaxReduceDepth} levels without fitting the model's context window.");
            }

            logger.LogInformation(
                "Reduce step {Depth}: {Count} summaries ({CombinedLength} chars) exceed the {Budget} char budget. Grouping and combining.",
                depth, summaries.Count, combined.Length, charBudget);

            var groups = DocumentChunker.GroupByBudget(summaries, s => s.Length, charBudget);
            var nextLevelSummaries = new List<string>();

            foreach (var group in groups)
            {
                if (group.Count == 1)
                {
                    // Oversized singleton: pass it through unchanged rather than spending an
                    // extra inference call "combining" a group of one.
                    nextLevelSummaries.Add(group[0]);
                    continue;
                }

                var groupCombined = string.Join(Environment.NewLine + "---" + Environment.NewLine, group);
                var groupPrompt = BuildPrompt(
                    modelName,
                    "Combine the following partial summaries into a single consolidated summary, preserving all key points:",
                    groupCombined);

                nextLevelSummaries.Add(CleanSummary(await RunInferenceAsync(executor, groupPrompt, inferenceParams, cancellationToken)));
            }

            return await ReduceSummariesAsync(executor, nextLevelSummaries, modelName, systemPrompt, inferenceParams, charBudget, cancellationToken, depth + 1);
        }

        private static async Task<string> RunInferenceAsync(
            StatelessExecutor executor,
            string prompt,
            InferenceParams inferenceParams,
            CancellationToken cancellationToken)
        {
            var response = new StringBuilder();

            await foreach (var token in executor.InferAsync(prompt, inferenceParams, cancellationToken))
            {
                response.Append(token);
            }

            return response.ToString();
        }

        private string CleanSummary(string rawOutput)
        {
            return SummaryOutputCleaner.Clean(rawOutput, _options.StopPhrases);
        }
    }
}

using System.Text;
using LLama;
using LLama.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SummaryGenerator.Models;

namespace SummaryGenerator.Services
{
    public class LlamaSharpSummarizer(
        IOptions<SummarizationOptions> options,
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
                var resolvedContextSize = ResolveContextSize(modelDetails);
                var modelParams = new ModelParams(modelPath)
                {
                    ContextSize = resolvedContextSize,
                    Threads = ResolveThreadCount(_options.Threads),
                    GpuLayerCount = _options.GpuLayerCount,
                    FlashAttention = _options.FlashAttention
                };

                using var weights = LLamaWeights.LoadFromFile(modelParams);
                using var context = weights.CreateContext(modelParams, NullLogger.Instance);
                var executor = new InteractiveExecutor(context, NullLogger.Instance);
                var inferenceParams = new InferenceParams
                {
                    MaxTokens = _options.MaxTokens,
                    AntiPrompts = _options.StopPhrases
                };

                int chunkSize = 12000;
                int overlap = 1000;

                if (documentText.Length < 15000)
                {
                    var prompt = BuildPrompt(systemPrompt, documentText);
                    var response = new StringBuilder();

                    await foreach (var token in executor.InferAsync(prompt, inferenceParams, cancellationToken))
                    {
                        response.Append(token);
                    }

                    var summary = CleanSummary(response.ToString());
                    logger.LogInformation(
                        "Summarization completed using model {ModelPath}. ContextSize={ContextSize}. Output chars: {CharacterCount}",
                        modelPath,
                        resolvedContextSize,
                        summary.Length);
                    return summary;
                }

                logger.LogInformation("Document is large ({Length} chars). Using map-reduce chunking.", documentText.Length);
                var chunkSummaries = new List<string>();
                for (int i = 0; i < documentText.Length; i += (chunkSize - overlap))
                {
                    int length = Math.Min(chunkSize, documentText.Length - i);
                    string chunkText = documentText.Substring(i, length);
                    string chunkPrompt = BuildPrompt("Summarize the following section of the document, focusing on key operational and strategic points:", chunkText);
                    
                    var chunkResponse = new StringBuilder();
                    await foreach (var token in executor.InferAsync(chunkPrompt, inferenceParams, cancellationToken))
                    {
                        chunkResponse.Append(token);
                    }
                    chunkSummaries.Add(CleanSummary(chunkResponse.ToString()));
                }

                logger.LogInformation("Chunking complete. Performing final map-reduce summarization on {Count} chunks.", chunkSummaries.Count);
                string combinedSummaries = string.Join(Environment.NewLine + "---" + Environment.NewLine, chunkSummaries);
                var finalPrompt = BuildPrompt(systemPrompt, combinedSummaries);
                
                var finalResponse = new StringBuilder();
                await foreach (var token in executor.InferAsync(finalPrompt, inferenceParams, cancellationToken))
                {
                    finalResponse.Append(token);
                }

                var finalSummary = CleanSummary(finalResponse.ToString());
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

        private static string BuildPrompt(string systemPrompt, string documentText) =>
            $"{systemPrompt.Trim()}{Environment.NewLine}{Environment.NewLine}Document:{Environment.NewLine}{documentText}";

        private const uint MaxSafeContextSize = 32768;

        private uint ResolveContextSize(ModelDetails modelDetails)
        {
            var desired = modelDetails.PreferredContextSize.GetValueOrDefault(_options.ContextSize);
            if (desired < _options.MinimumContextSize)
            {
                desired = _options.MinimumContextSize;
            }

            // Hard cap to prevent native crashes due to VRAM/RAM exhaustion
            if (desired > MaxSafeContextSize)
            {
                desired = MaxSafeContextSize;
            }

            if (modelDetails.MaxContextSize is > 0)
            {
                var maxAllowed = modelDetails.MaxContextSize.Value;
                if (_options.ContextSafetyReserveTokens > 0 && _options.ContextSafetyReserveTokens < maxAllowed)
                {
                    maxAllowed -= _options.ContextSafetyReserveTokens;
                }

                if (maxAllowed > 0 && desired > maxAllowed)
                {
                    desired = maxAllowed;
                }
            }

            return desired;
        }

        private string CleanSummary(string rawOutput)
        {
            return SummaryOutputCleaner.Clean(rawOutput, _options.StopPhrases);
        }

        private static int ResolveThreadCount(int configuredThreads)
        {
            if (configuredThreads > 0)
            {
                return configuredThreads;
            }

            return Math.Max(1, Environment.ProcessorCount - 2);
        }
    }
}

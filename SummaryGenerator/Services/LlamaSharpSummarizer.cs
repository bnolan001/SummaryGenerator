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

                int chunkSize = 12000;

                if (documentText.Length < 15000)
                {
                    var prompt = BuildPrompt(modelDetails.Name, systemPrompt, documentText);
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
                int i = 0;
                while (i < documentText.Length)
                {
                    int length = Math.Min(chunkSize, documentText.Length - i);
                    
                    if (i + length < documentText.Length)
                    {
                        int searchStartIndex = i + length - 1;
                        int lastDoubleNewline = documentText.LastIndexOf("\n\n", searchStartIndex, length, StringComparison.Ordinal);
                        
                        if (lastDoubleNewline > i)
                        {
                            length = lastDoubleNewline - i + 2;
                        }
                        else
                        {
                            int lastPeriod = documentText.LastIndexOf(". ", searchStartIndex, length, StringComparison.Ordinal);
                            if (lastPeriod > i)
                            {
                                length = lastPeriod - i + 1;
                            }
                        }
                    }

                    string chunkText = documentText.Substring(i, length);
                    string chunkPrompt = BuildPrompt(modelDetails.Name, "Summarize the following section of the document, focusing on key operational and strategic points:", chunkText);
                    
                    var chunkResponse = new StringBuilder();
                    await foreach (var token in executor.InferAsync(chunkPrompt, inferenceParams, cancellationToken))
                    {
                        chunkResponse.Append(token);
                    }
                    chunkSummaries.Add(CleanSummary(chunkResponse.ToString()));
                    
                    i += length;
                }

                logger.LogInformation("Chunking complete. Performing final map-reduce summarization on {Count} chunks.", chunkSummaries.Count);
                string combinedSummaries = string.Join(Environment.NewLine + "---" + Environment.NewLine, chunkSummaries);
                var finalPrompt = BuildPrompt(modelDetails.Name, systemPrompt, combinedSummaries);
                
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

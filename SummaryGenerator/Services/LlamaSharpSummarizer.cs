using LLama;
using LLama.Common;
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
                var resolvedGpuLayerCount = InferenceParameterResolver.ResolveGpuLayerCount(
                    _options.GpuLayerCount,
                    _options.HardwareAutoTune,
                    hardwarePlan,
                    new FileInfo(modelPath).Length,
                    _options.IntegratedGpuMaxModelPercentOfRam);
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
                var summaryModel = new LlamaSummaryModel(weights, executor, modelDetails.Name, _options);

                var summary = await new MapReduceSummarizer(summaryModel, logger).SummarizeAsync(
                    documentText,
                    systemPrompt,
                    resolvedContextSize,
                    _options.MaxTokens,
                    cancellationToken);

                logger.LogInformation(
                    "Summarization completed using model {ModelPath}. ContextSize={ContextSize}. Output chars: {CharacterCount}",
                    modelPath,
                    resolvedContextSize,
                    summary.Length);
                return summary;
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
    }
}

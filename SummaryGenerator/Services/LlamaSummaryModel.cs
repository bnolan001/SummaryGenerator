using System.Text;
using LLama;
using LLama.Common;
using LLama.Sampling;
using SummaryGenerator.Models;

namespace SummaryGenerator.Services
{
    /// <summary>
    /// <see cref="ISummaryModel"/> backed by loaded LLamaSharp weights. Token counts use the
    /// model's own tokenizer on the fully templated prompt, so budgets are exact, not estimated.
    /// </summary>
    internal sealed class LlamaSummaryModel(
        LLamaWeights weights,
        StatelessExecutor executor,
        string modelName,
        SummarizationOptions options) : ISummaryModel
    {
        public int CountPromptTokens(string instruction, string content)
        {
            var prompt = PromptTemplate.Build(modelName, instruction, content);
            return weights.Tokenize(prompt, true, true, Encoding.UTF8).Length;
        }

        public async Task<string> GenerateAsync(string instruction, string content, int maxOutputTokens, CancellationToken cancellationToken)
        {
            var inferenceParams = new InferenceParams
            {
                MaxTokens = maxOutputTokens,
                AntiPrompts = options.StopPhrases,
                SamplingPipeline = new DefaultSamplingPipeline
                {
                    Temperature = options.Temperature,
                    TopP = options.TopP,
                    RepeatPenalty = options.RepeatPenalty
                },
                OverflowStrategy = ContextOverflowStrategy.TruncateAndReprefill
            };

            var prompt = PromptTemplate.Build(modelName, instruction, content);
            var response = new StringBuilder();

            await foreach (var token in executor.InferAsync(prompt, inferenceParams, cancellationToken))
            {
                response.Append(token);
            }

            return SummaryOutputCleaner.Clean(response.ToString(), options.StopPhrases);
        }
    }
}

using System.Text.RegularExpressions;

namespace SummaryGenerator.Services
{
    /// <summary>
    /// Wraps an instruction and content in the chat template matching the model family.
    /// Templates mirror each GGUF's embedded tokenizer.chat_template (BOS is added by the executor).
    /// </summary>
    public static partial class PromptTemplate
    {
        [GeneratedRegex(@"gemma[\s-]*4", RegexOptions.IgnoreCase)]
        private static partial Regex Gemma4Regex();

        public static string Build(string modelName, string instruction, string content)
        {
            var name = modelName?.ToLowerInvariant() ?? string.Empty;

            if (Gemma4Regex().IsMatch(name))
            {
                // Empty thought channel = thinking disabled, as the template emits when enable_thinking is false.
                return $"<|turn>system\n{instruction.Trim()}<turn|>\n<|turn>user\n{content}<turn|>\n<|turn>model\n<|channel>thought\n<channel|>";
            }
            if (name.Contains("gemma"))
            {
                return $"<start_of_turn>user\n{instruction.Trim()}\n\n{content}<end_of_turn>\n<start_of_turn>model\n";
            }
            if (name.Contains("phi"))
            {
                return $"<|system|>{instruction.Trim()}<|end|><|user|>{content}<|end|><|assistant|>";
            }
            if (name.Contains("qwen"))
            {
                return $"<|im_start|>system\n{instruction.Trim()}<|im_end|>\n<|im_start|>user\n{content}<|im_end|>\n<|im_start|>assistant\n";
            }
            if (name.Contains("llama"))
            {
                return $"<|begin_of_text|><|start_header_id|>system<|end_header_id|>\n\n{instruction.Trim()}<|eot_id|><|start_header_id|>user<|end_header_id|>\n\n{content}<|eot_id|><|start_header_id|>assistant<|end_header_id|>\n\n";
            }

            return $"### Instruction:\n{instruction.Trim()}\n\n### Input:\n{content}\n\n### Response:\n";
        }
    }
}

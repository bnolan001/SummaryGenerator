using SummaryGenerator.Services;

namespace SummaryGenerator.Tests.Unit
{
    public class PromptTemplateTests
    {
        [Fact]
        public void Build_Phi4_UsesPhiChatFormat()
        {
            var prompt = PromptTemplate.Build("Phi-4 Mini (3.8B)", " Instr ", "Doc");

            Assert.Equal("<|system|>Instr<|end|><|user|>Doc<|end|><|assistant|>", prompt);
        }

        [Fact]
        public void Build_Gemma4_UsesTurnFormatWithThinkingDisabled()
        {
            var prompt = PromptTemplate.Build("Gemma 4 (12B)", "Instr", "Doc");

            Assert.Equal(
                "<|turn>system\nInstr<turn|>\n<|turn>user\nDoc<turn|>\n<|turn>model\n<|channel>thought\n<channel|>",
                prompt);
        }

        [Fact]
        public void Build_Gemma4MoE_UsesTurnFormat()
        {
            var prompt = PromptTemplate.Build("Gemma 4 (26B MoE)", "Instr", "Doc");

            Assert.StartsWith("<|turn>system\n", prompt);
        }

        [Fact]
        public void Build_OlderGemma_KeepsStartOfTurnFormat()
        {
            var prompt = PromptTemplate.Build("Gemma 3 (12B)", "Instr", "Doc");

            Assert.Equal("<start_of_turn>user\nInstr\n\nDoc<end_of_turn>\n<start_of_turn>model\n", prompt);
        }

        [Fact]
        public void Build_Qwen_UsesChatMl()
        {
            var prompt = PromptTemplate.Build("Qwen 2.5 (7B)", "Instr", "Doc");

            Assert.Equal("<|im_start|>system\nInstr<|im_end|>\n<|im_start|>user\nDoc<|im_end|>\n<|im_start|>assistant\n", prompt);
        }
    }
}

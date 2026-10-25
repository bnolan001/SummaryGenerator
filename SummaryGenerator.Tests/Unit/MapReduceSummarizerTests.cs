using Microsoft.Extensions.Logging.Abstractions;
using SummaryGenerator.Services;

namespace SummaryGenerator.Tests.Unit
{
    public class MapReduceSummarizerTests
    {
        private const string SystemPrompt = "Summarize the document as PME study notes.";

        private sealed class FakeSummaryModel(int tokensPerChar = 1, Func<string, int, string>? generate = null) : ISummaryModel
        {
            public List<(string Instruction, int PromptTokens, int MaxOutputTokens)> Calls { get; } = [];

            public int CountPromptTokens(string instruction, string content)
                => (instruction.Length + content.Length) * tokensPerChar;

            public Task<string> GenerateAsync(string instruction, string content, int maxOutputTokens, CancellationToken cancellationToken)
            {
                Calls.Add((instruction, CountPromptTokens(instruction, content), maxOutputTokens));
                var output = generate?.Invoke(instruction, maxOutputTokens)
                    ?? new string('x', maxOutputTokens / tokensPerChar);
                return Task.FromResult(output);
            }
        }

        private static string Document(int totalChars)
        {
            const string sentence = "The battalion crossed the river at dawn under heavy fire. ";
            var text = string.Concat(Enumerable.Repeat(sentence, (totalChars / sentence.Length) + 1));
            return text[..totalChars];
        }

        private static int FakeOverhead => new[]
        {
            SystemPrompt.Length,
            MapReduceSummarizer.ChunkInstruction.Length,
            MapReduceSummarizer.CombineInstruction.Length
        }.Max();

        private static Task<string> Run(FakeSummaryModel model, string document, uint contextSize, int maxOutputTokens = 2048)
            => new MapReduceSummarizer(model, NullLogger.Instance)
                .SummarizeAsync(document, SystemPrompt, contextSize, maxOutputTokens, CancellationToken.None);

        private static void AssertEveryPromptFits(FakeSummaryModel model, uint contextSize)
        {
            Assert.All(model.Calls, call =>
                Assert.True(call.PromptTokens + call.MaxOutputTokens + TokenBudget.SafetyMarginTokens <= contextSize,
                    $"{call.Instruction[..20]}: prompt {call.PromptTokens} + output {call.MaxOutputTokens} exceeds {contextSize}"));
        }

        [Fact]
        public async Task SummarizeAsync_ShortDocument_MakesSingleFinalCall()
        {
            var model = new FakeSummaryModel();

            await Run(model, Document(1_000), contextSize: 16384);

            var call = Assert.Single(model.Calls);
            Assert.Equal(SystemPrompt, call.Instruction);
            Assert.Equal(TokenBudget.Compute(16384, 2048, FakeOverhead).FinalOutputTokens, call.MaxOutputTokens);
        }

        [Fact]
        public async Task SummarizeAsync_LargeDocument_EveryPromptFitsContext()
        {
            var model = new FakeSummaryModel();

            await Run(model, Document(400_000), contextSize: 16384);

            Assert.True(model.Calls.Count > 1);
            AssertEveryPromptFits(model, 16384);
            Assert.Equal(SystemPrompt, model.Calls[^1].Instruction);
        }

        [Fact]
        public async Task SummarizeAsync_MinimumContext_MaxLengthSummaries_Completes()
        {
            var model = new FakeSummaryModel();

            await Run(model, Document(400_000), contextSize: 4096);

            AssertEveryPromptFits(model, 4096);
            Assert.Equal(SystemPrompt, model.Calls[^1].Instruction);
        }

        [Fact]
        public async Task SummarizeAsync_DenseDocument_EveryPromptFitsContext()
        {
            var model = new FakeSummaryModel(tokensPerChar: 2);

            await Run(model, Document(200_000), contextSize: 8192);

            AssertEveryPromptFits(model, 8192);
            Assert.Equal(SystemPrompt, model.Calls[^1].Instruction);
        }

        [Fact]
        public async Task SummarizeAsync_EmptyIntermediateSummaries_StillCompletes()
        {
            var model = new FakeSummaryModel(generate: (instruction, _) => instruction == SystemPrompt ? "final notes" : "");

            var result = await Run(model, Document(400_000), contextSize: 4096);

            Assert.Equal("final notes", result);
        }

        [Fact]
        public async Task SummarizeAsync_IntermediateCallsUseIntermediateOutputTokens()
        {
            var model = new FakeSummaryModel();
            var expected = TokenBudget.Compute(4096, 2048, FakeOverhead).IntermediateOutputTokens;

            await Run(model, Document(400_000), contextSize: 4096);

            var intermediateCalls = model.Calls.Where(call => call.Instruction != SystemPrompt).ToList();
            Assert.NotEmpty(intermediateCalls);
            Assert.All(intermediateCalls, call => Assert.Equal(expected, call.MaxOutputTokens));
        }
    }
}

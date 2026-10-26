using SummaryGenerator.Services;

namespace SummaryGenerator.Tests.Unit
{
    public class DocumentChunkerSplitToTokenBudgetTests
    {
        private static string Sentences(int totalChars)
        {
            const string sentence = "The battalion crossed the river at dawn. ";
            var text = string.Concat(Enumerable.Repeat(sentence, (totalChars / sentence.Length) + 1));
            return text[..totalChars];
        }

        [Fact]
        public void SplitToTokenBudget_FitsAlready_ReturnsSingleChunk()
        {
            var text = Sentences(1_000);

            var chunks = DocumentChunker.SplitToTokenBudget(text, s => s.Length, maxTokens: 3000);

            Assert.Equal([text], chunks);
        }

        [Fact]
        public void SplitToTokenBudget_NoChunkExceedsBudget()
        {
            var chunks = DocumentChunker.SplitToTokenBudget(Sentences(50_000), s => s.Length, maxTokens: 3000);

            Assert.True(chunks.Count > 1);
            Assert.All(chunks, chunk => Assert.True(chunk.Length <= 3000));
        }

        [Fact]
        public void SplitToTokenBudget_DenseText_NoChunkExceedsBudget()
        {
            var chunks = DocumentChunker.SplitToTokenBudget(Sentences(50_000), s => s.Length * 2, maxTokens: 3000);

            Assert.All(chunks, chunk => Assert.True(chunk.Length * 2 <= 3000));
        }

        [Fact]
        public void SplitToTokenBudget_ConcatenationEqualsInput()
        {
            var text = Sentences(50_000);

            var chunks = DocumentChunker.SplitToTokenBudget(text, s => s.Length * 2, maxTokens: 3000);

            Assert.Equal(text, string.Concat(chunks));
        }
    }
}

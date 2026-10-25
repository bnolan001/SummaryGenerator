using SummaryGenerator.Services;

namespace SummaryGenerator.Tests.Unit
{
    public class DocumentChunkerSplitIntoChunksTests
    {
        [Fact]
        public void SplitIntoChunks_TextShorterThanBudget_ReturnsSingleChunk()
        {
            var chunks = DocumentChunker.SplitIntoChunks("short document text", maxChunkChars: 100);

            Assert.Equal(["short document text"], chunks);
        }

        [Fact]
        public void SplitIntoChunks_PrefersSplittingAtDoubleNewline()
        {
            var paragraph1 = new string('a', 10);
            var paragraph2 = new string('b', 10);
            var text = paragraph1 + "\n\n" + paragraph2;

            // Budget covers paragraph1 + the double-newline, forcing a split there
            // instead of a hard cut mid-paragraph2.
            var chunks = DocumentChunker.SplitIntoChunks(text, maxChunkChars: 15);

            Assert.Equal(paragraph1 + "\n\n", chunks[0]);
            Assert.Equal(paragraph2, chunks[1]);
        }

        [Fact]
        public void SplitIntoChunks_FallsBackToSentenceBoundary_WhenNoDoubleNewline()
        {
            // The split keeps the period but not the trailing space (matches the
            // existing, pre-extraction boundary behavior).
            var text = "First sentence is here. Second sentence follows.";

            var chunks = DocumentChunker.SplitIntoChunks(text, maxChunkChars: 28);

            Assert.Equal("First sentence is here.", chunks[0]);
            Assert.Equal(" Second sentence follows.", chunks[1]);
            Assert.Equal(text, string.Concat(chunks));
        }

        [Fact]
        public void SplitIntoChunks_HardCutsWhenNoBoundaryFound()
        {
            var text = new string('x', 25);

            var chunks = DocumentChunker.SplitIntoChunks(text, maxChunkChars: 10);

            Assert.Equal(new string('x', 10), chunks[0]);
            Assert.Equal(new string('x', 10), chunks[1]);
            Assert.Equal(new string('x', 5), chunks[2]);
        }

        [Fact]
        public void SplitIntoChunks_NoChunkExceedsBudget()
        {
            var random = new Random(42);
            var text = string.Concat(Enumerable.Range(0, 500).Select(_ => (char)('a' + random.Next(26))));

            var chunks = DocumentChunker.SplitIntoChunks(text, maxChunkChars: 37);

            Assert.All(chunks, chunk => Assert.True(chunk.Length <= 37, $"Chunk of length {chunk.Length} exceeded budget."));
            Assert.Equal(text, string.Concat(chunks));
        }
    }
}

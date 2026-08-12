namespace SummaryGenerator.Models
{
    public class PromptProfile
    {
        public required string Id { get; init; }

        public required string Name { get; init; }

        public required string Prompt { get; init; }

        public bool IsBuiltIn { get; init; }
    }
}

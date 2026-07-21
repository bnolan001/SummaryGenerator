using SummaryGenerator.Models;

namespace SummaryGenerator.Services
{
    public interface IPromptProfileStore
    {
        string DefaultProfileId { get; }

        IReadOnlyList<PromptProfile> GetAll();

        PromptProfile? GetById(string profileId);

        PromptProfile SaveCustom(string name, string prompt);
    }
}

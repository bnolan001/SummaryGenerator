namespace SummaryGenerator.Services
{
    public interface IAudioGenerator
    {
        IReadOnlyList<string> GetAvailableVoices();

        Task<string> GenerateAudioAsync(string markdownContent, string outputPath, string? selectedVoice = null, CancellationToken cancellationToken = default);
    }
}

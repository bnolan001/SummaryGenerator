using System.Speech.Synthesis;
using System.Text.RegularExpressions;

namespace SummaryGenerator.Services
{
    public class SystemSpeechAudioGenerator : IAudioGenerator
    {
        private readonly ILogger<SystemSpeechAudioGenerator> _logger;

        public SystemSpeechAudioGenerator(ILogger<SystemSpeechAudioGenerator> logger)
        {
            _logger = logger;
        }

        public IReadOnlyList<string> GetAvailableVoices()
        {
            using var synthesizer = new SpeechSynthesizer();
            return synthesizer.GetInstalledVoices()
                .Where(v => v.Enabled)
                .Select(v => v.VoiceInfo.Name)
                .ToList();
        }

        public async Task<string> GenerateAudioAsync(string markdownContent, string outputPath, string? selectedVoice = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(markdownContent))
                throw new ArgumentException("Content is required.", nameof(markdownContent));

            // Clean up basic markdown so it reads better
            var cleanText = CleanMarkdownForSpeech(markdownContent);

            return await Task.Run(() =>
            {
                try
                {
                    // Create synthesizer
                    using var synthesizer = new SpeechSynthesizer();
                    
                    if (!string.IsNullOrWhiteSpace(selectedVoice))
                    {
                        try
                        {
                            synthesizer.SelectVoice(selectedVoice);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to select voice {SelectedVoice}. Falling back to default.", selectedVoice);
                        }
                    }

                    // Route output to wav file
                    synthesizer.SetOutputToWaveFile(outputPath);
                    
                    // You can optionally configure rate or voice here if needed
                    // synthesizer.SelectVoiceByHints(VoiceGender.Female);

                    // Speak
                    synthesizer.Speak(cleanText);
                    
                    _logger.LogInformation("Generated audio file at {OutputPath}", outputPath);
                    return outputPath;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to generate audio using System.Speech at {OutputPath}", outputPath);
                    throw new IOException($"Failed to generate audio: {ex.Message}", ex);
                }
            }, cancellationToken);
        }

        private static string CleanMarkdownForSpeech(string markdown)
        {
            // Remove headers
            var text = Regex.Replace(markdown, @"^#+\s*", "", RegexOptions.Multiline);
            
            // Remove bold/italic stars and underscores
            text = Regex.Replace(text, @"(\*\*|\*|__|_)", "");
            
            // Remove markdown links but keep text
            text = Regex.Replace(text, @"\[([^\]]+)\]\([^\)]+\)", "$1");
            
            // Remove code blocks
            text = Regex.Replace(text, @"```[\s\S]*?```", "");
            
            // Remove inline code
            text = Regex.Replace(text, @"`[^`]+`", "");
            
            // Clean up extra whitespace
            text = Regex.Replace(text, @"\n{3,}", "\n\n");
            
            return text.Trim();
        }
    }
}

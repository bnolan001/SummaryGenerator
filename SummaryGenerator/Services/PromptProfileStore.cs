using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SummaryGenerator.Models;

namespace SummaryGenerator.Services
{
    public class PromptProfileStore : IPromptProfileStore
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, PromptProfile> _builtInProfiles;
        private readonly Dictionary<string, PromptProfile> _customProfiles = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _customPromptsPath;
        private readonly ILogger<PromptProfileStore> _logger;

        public PromptProfileStore(
            IOptions<SummarizationOptions> options,
            IWebHostEnvironment environment,
            ILogger<PromptProfileStore> logger)
        {
            _logger = logger;
            var summarizationOptions = options.Value;

            _builtInProfiles = BuildBuiltInProfiles(summarizationOptions);
            DefaultProfileId = ResolveDefaultProfileId(summarizationOptions, _builtInProfiles);

            _customPromptsPath = ResolveStorageRoot(environment.ContentRootPath, summarizationOptions.CustomPromptsFilePath);
            LoadCustomProfiles();
        }

        public string DefaultProfileId { get; }

        public IReadOnlyList<PromptProfile> GetAll()
        {
            lock (_sync)
            {
                return _builtInProfiles.Values
                    .Concat(_customProfiles.Values.OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        public PromptProfile? GetById(string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                return null;
            }

            lock (_sync)
            {
                if (_builtInProfiles.TryGetValue(profileId, out var builtIn))
                {
                    return builtIn;
                }

                return _customProfiles.TryGetValue(profileId, out var custom) ? custom : null;
            }
        }

        public PromptProfile SaveCustom(string name, string prompt)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Custom prompt name is required.", nameof(name));
            }

            if (string.IsNullOrWhiteSpace(prompt))
            {
                throw new ArgumentException("Custom prompt text is required.", nameof(prompt));
            }

            var normalizedName = name.Trim();
            var normalizedPrompt = prompt.Trim();

            lock (_sync)
            {
                var existingByName = _customProfiles.Values.FirstOrDefault(profile =>
                    string.Equals(profile.Name, normalizedName, StringComparison.OrdinalIgnoreCase));

                PromptProfile savedProfile;
                if (existingByName is not null)
                {
                    savedProfile = new PromptProfile
                    {
                        Id = existingByName.Id,
                        Name = normalizedName,
                        Prompt = normalizedPrompt,
                        IsBuiltIn = false
                    };
                }
                else
                {
                    var id = GenerateUniqueCustomId(normalizedName);
                    savedProfile = new PromptProfile
                    {
                        Id = id,
                        Name = normalizedName,
                        Prompt = normalizedPrompt,
                        IsBuiltIn = false
                    };
                }

                _customProfiles[savedProfile.Id] = savedProfile;
                PersistCustomProfiles();
                return savedProfile;
            }
        }

        private void LoadCustomProfiles()
        {
            if (!File.Exists(_customPromptsPath))
            {
                return;
            }

            try
            {
                var json = File.ReadAllText(_customPromptsPath);
                var records = JsonSerializer.Deserialize<List<CustomPromptRecord>>(json) ?? [];

                lock (_sync)
                {
                    foreach (var record in records)
                    {
                        if (string.IsNullOrWhiteSpace(record.Id) ||
                            string.IsNullOrWhiteSpace(record.Name) ||
                            string.IsNullOrWhiteSpace(record.Prompt))
                        {
                            continue;
                        }

                        if (_builtInProfiles.ContainsKey(record.Id))
                        {
                            continue;
                        }

                        _customProfiles[record.Id] = new PromptProfile
                        {
                            Id = record.Id,
                            Name = record.Name,
                            Prompt = record.Prompt,
                            IsBuiltIn = false
                        };
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Custom prompts file contains invalid JSON: {Path}", _customPromptsPath);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Custom prompts file could not be read: {Path}", _customPromptsPath);
            }
        }

        private void PersistCustomProfiles()
        {
            var directory = Path.GetDirectoryName(_customPromptsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var records = _customProfiles.Values
                .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                .Select(profile => new CustomPromptRecord
                {
                    Id = profile.Id,
                    Name = profile.Name,
                    Prompt = profile.Prompt
                })
                .ToList();

            var json = JsonSerializer.Serialize(records, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(_customPromptsPath, json, Encoding.UTF8);
        }

        private string GenerateUniqueCustomId(string name)
        {
            var baseId = ToSlug(name);
            var candidate = baseId;
            var suffix = 2;
            while (_builtInProfiles.ContainsKey(candidate) || _customProfiles.ContainsKey(candidate))
            {
                candidate = $"{baseId}-{suffix}";
                suffix++;
            }

            return candidate;
        }

        private static string ResolveStorageRoot(string contentRootPath, string configuredPath)
        {
            if (Path.IsPathRooted(configuredPath))
            {
                return configuredPath;
            }

            return Path.Combine(contentRootPath, configuredPath);
        }

        private static Dictionary<string, PromptProfile> BuildBuiltInProfiles(SummarizationOptions options)
        {
            var profiles = new Dictionary<string, PromptProfile>(StringComparer.OrdinalIgnoreCase);

            foreach (var configuredProfile in options.PromptProfiles
                         .Where(profile =>
                             !string.IsNullOrWhiteSpace(profile.Id) &&
                             !string.IsNullOrWhiteSpace(profile.Name) &&
                             !string.IsNullOrWhiteSpace(profile.Prompt)))
            {
                profiles[configuredProfile.Id] = new PromptProfile
                {
                    Id = configuredProfile.Id,
                    Name = configuredProfile.Name,
                    Prompt = configuredProfile.Prompt,
                    IsBuiltIn = true
                };
            }

            if (profiles.Count > 0)
            {
                return profiles;
            }

            profiles["default"] = new PromptProfile
            {
                Id = "default",
                Name = "Default",
                Prompt = options.SystemPrompt,
                IsBuiltIn = true
            };

            return profiles;
        }

        private static string ResolveDefaultProfileId(
            SummarizationOptions options,
            IReadOnlyDictionary<string, PromptProfile> builtIns)
        {
            if (!string.IsNullOrWhiteSpace(options.DefaultPromptProfileId) &&
                builtIns.ContainsKey(options.DefaultPromptProfileId))
            {
                return options.DefaultPromptProfileId;
            }

            return builtIns.Keys.First();
        }

        private static string ToSlug(string value)
        {
            var normalized = value.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "custom-prompt";
            }

            var sb = new StringBuilder();
            var lastWasDash = false;
            foreach (var c in normalized)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                    lastWasDash = false;
                    continue;
                }

                if (lastWasDash)
                {
                    continue;
                }

                sb.Append('-');
                lastWasDash = true;
            }

            var slug = sb.ToString().Trim('-');
            return string.IsNullOrWhiteSpace(slug) ? "custom-prompt" : slug;
        }

        private class CustomPromptRecord
        {
            public string Id { get; init; } = string.Empty;

            public string Name { get; init; } = string.Empty;

            public string Prompt { get; init; } = string.Empty;
        }
    }
}

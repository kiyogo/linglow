using System;
using System.IO;
using VideoNarrator.Helpers;
using VideoNarrator.Models;

namespace VideoNarrator.Services
{
    public interface ISettingsService
    {
        AppSettings CurrentSettings { get; }
        AppSettings LoadSettings();
        void SaveSettings(AppSettings settings);
    }

    public class SettingsService : ISettingsService
    {
        private readonly string _settingsFilePath;
        private AppSettings _currentSettings;

        public AppSettings CurrentSettings => _currentSettings;

        public SettingsService()
        {
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VideoNarrator");
            Directory.CreateDirectory(appData);
            _settingsFilePath = Path.Combine(appData, "settings.json");
            _currentSettings = LoadSettings();
        }

        public AppSettings LoadSettings()
        {
            AppSettings? settings = null;
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    string json = File.ReadAllText(_settingsFilePath);
                    settings = JsonHelper.DeserializeSafe<AppSettings>(json);
                }
            }
            catch
            {
                // Fallback to default
            }

            settings ??= new AppSettings();

            if (string.IsNullOrWhiteSpace(settings.OutputDirectory))
            {
                settings.OutputDirectory = AppSettings.GetDefaultOutputDirectory();
            }

            // Auto-load API keys from Environment Variables and .env files
            var envDict = LoadEnvDictionary();

            if (string.IsNullOrWhiteSpace(settings.GeminiApiKey))
            {
                if (envDict.TryGetValue("GEMINI_API_KEY", out var geminiKey) && !string.IsNullOrWhiteSpace(geminiKey))
                {
                    settings.GeminiApiKey = geminiKey.Trim();
                }
                else if (envDict.TryGetValue("GOOGLE_API_KEY", out var googleKey) && !string.IsNullOrWhiteSpace(googleKey))
                {
                    settings.GeminiApiKey = googleKey.Trim();
                }
                else if (envDict.TryGetValue("GEMINI_KEY", out var gKey) && !string.IsNullOrWhiteSpace(gKey))
                {
                    settings.GeminiApiKey = gKey.Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(settings.TtsApiKey))
            {
                if (envDict.TryGetValue("OPENAI_API_KEY", out var oaiKey) && !string.IsNullOrWhiteSpace(oaiKey))
                {
                    settings.TtsApiKey = oaiKey.Trim();
                }
                else if (envDict.TryGetValue("ELEVENLABS_API_KEY", out var elKey) && !string.IsNullOrWhiteSpace(elKey))
                {
                    settings.TtsApiKey = elKey.Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(settings.TtsProvider))
            {
                settings.TtsProvider = "Edge-TTS";
            }

            if (string.IsNullOrWhiteSpace(settings.TtsVoice))
            {
                settings.TtsVoice = "vi-VN-NamMinhNeural (Nam - Tiếng Việt)";
            }

            _currentSettings = settings;
            return _currentSettings;
        }

        private static Dictionary<string, string> LoadEnvDictionary()
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // 1. Read System & User Environment variables
            string[] envVarNames = { "GEMINI_API_KEY", "GOOGLE_API_KEY", "GEMINI_KEY", "OPENAI_API_KEY", "ELEVENLABS_API_KEY" };
            foreach (var name in envVarNames)
            {
                string? val = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrWhiteSpace(val))
                {
                    dict[name] = val.Trim();
                }
            }

            // 2. Search for .env files in multiple candidate paths
            var candidatePaths = new List<string>
            {
                Path.Combine(AppContext.BaseDirectory, ".env"),
                Path.Combine(Directory.GetCurrentDirectory(), ".env"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VideoNarrator", ".env")
            };

            // Search parent directories up to 3 levels (e.g. solution/project root)
            try
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                for (int i = 0; i < 3 && dir != null; i++)
                {
                    string candidate = Path.Combine(dir.FullName, ".env");
                    if (!candidatePaths.Contains(candidate))
                    {
                        candidatePaths.Add(candidate);
                    }
                    dir = dir.Parent;
                }
            }
            catch { }

            foreach (var path in candidatePaths)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        foreach (var line in File.ReadAllLines(path))
                        {
                            var trimmed = line.Trim();
                            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#")) continue;

                            int idx = trimmed.IndexOf('=');
                            if (idx > 0)
                            {
                                string k = trimmed.Substring(0, idx).Trim();
                                string v = trimmed.Substring(idx + 1).Trim().Trim('"', '\'');
                                if (!string.IsNullOrWhiteSpace(k) && !string.IsNullOrWhiteSpace(v))
                                {
                                    dict[k] = v;
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            return dict;
        }

        public void SaveSettings(AppSettings settings)
        {
            try
            {
                _currentSettings = settings ?? throw new ArgumentNullException(nameof(settings));
                string json = JsonHelper.Serialize(settings);
                File.WriteAllText(_settingsFilePath, json);
            }
            catch
            {
                // Silently ignore save errors
            }
        }
    }
}

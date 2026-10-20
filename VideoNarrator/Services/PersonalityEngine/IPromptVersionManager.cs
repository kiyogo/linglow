using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using VideoNarrator.Models;

namespace VideoNarrator.Services.PersonalityEngine
{
    public interface IPromptVersionManager
    {
        IReadOnlyList<PromptVersion> GetVersionsForPersona(string personaId);
        IReadOnlyList<PromptVersion> GetAllVersions();
        PromptVersion SaveVersion(PersonaProfile persona, string compiledPrompt, string? note = null);
        PromptVersion? GetVersion(string versionId);
        void DeleteVersion(string versionId);
        void Save();
        void Load();
    }

    public class PromptVersionManager : IPromptVersionManager
    {
        private readonly List<PromptVersion> _versions = new();
        private readonly string _storagePath;
        private readonly object _lock = new();

        public PromptVersionManager()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "VideoNarrator");
            if (!Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); } catch { }
            }
            _storagePath = Path.Combine(dir, "prompt_versions.json");
            Load();
        }

        public IReadOnlyList<PromptVersion> GetVersionsForPersona(string personaId)
        {
            lock (_lock)
            {
                return _versions.Where(v => v.PersonaId == personaId)
                                .OrderByDescending(v => v.Timestamp)
                                .ToList();
            }
        }

        public IReadOnlyList<PromptVersion> GetAllVersions()
        {
            lock (_lock)
            {
                return _versions.OrderByDescending(v => v.Timestamp).ToList();
            }
        }

        public PromptVersion SaveVersion(PersonaProfile persona, string compiledPrompt, string? note = null)
        {
            if (persona == null) throw new ArgumentNullException(nameof(persona));

            lock (_lock)
            {
                int existingCount = _versions.Count(v => v.PersonaId == persona.Id);
                string tag = $"v{(existingCount + 1)}.0";

                var version = new PromptVersion
                {
                    Id = Guid.NewGuid().ToString("N"),
                    PersonaId = persona.Id,
                    PersonaName = persona.Name,
                    VersionTag = tag,
                    Timestamp = DateTime.Now,
                    Note = note ?? $"Bản lưu tự động lúc {DateTime.Now:HH:mm:ss dd/MM}",
                    CompiledSystemPrompt = compiledPrompt,
                    StyleConfigSnapshot = persona.StyleConfig.Clone()
                };

                _versions.Add(version);
                Save();
                return version;
            }
        }

        public PromptVersion? GetVersion(string versionId)
        {
            lock (_lock)
            {
                return _versions.FirstOrDefault(v => v.Id == versionId);
            }
        }

        public void DeleteVersion(string versionId)
        {
            lock (_lock)
            {
                _versions.RemoveAll(v => v.Id == versionId);
                Save();
            }
        }

        public void Save()
        {
            try
            {
                var opts = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_versions, opts);
                File.WriteAllText(_storagePath, json, Encoding.UTF8);
            }
            catch { }
        }

        public void Load()
        {
            lock (_lock)
            {
                _versions.Clear();
                if (File.Exists(_storagePath))
                {
                    try
                    {
                        string json = File.ReadAllText(_storagePath, Encoding.UTF8);
                        var loaded = JsonSerializer.Deserialize<List<PromptVersion>>(json);
                        if (loaded != null)
                        {
                            _versions.AddRange(loaded);
                        }
                    }
                    catch { }
                }
            }
        }
    }
}

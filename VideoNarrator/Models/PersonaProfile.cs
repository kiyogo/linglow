using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VideoNarrator.Models
{
    public class PersonaProfile
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        [JsonPropertyName("name")]
        public string Name { get; set; } = "Persona Mới";

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("voice_tone")]
        public string VoiceTone { get; set; } = "Bí ẩn, sắc bén, kích thích tò mò";

        [JsonPropertyName("is_default")]
        public bool IsDefault { get; set; } = false;

        [JsonPropertyName("is_built_in")]
        public bool IsBuiltIn { get; set; } = false;

        [JsonPropertyName("system_prompt_override")]
        public string SystemPromptOverride { get; set; } = string.Empty;

        [JsonPropertyName("style_config")]
        public StyleEngineConfig StyleConfig { get; set; } = new();

        [JsonPropertyName("custom_rules")]
        public List<string> CustomRules { get; set; } = new();

        [JsonPropertyName("examples")]
        public List<FewShotExample> Examples { get; set; } = new();

        public PersonaProfile Clone(string newName)
        {
            var cloned = new PersonaProfile
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = newName,
                Description = this.Description,
                VoiceTone = this.VoiceTone,
                IsDefault = false,
                IsBuiltIn = false,
                SystemPromptOverride = this.SystemPromptOverride,
                StyleConfig = this.StyleConfig.Clone(),
                CustomRules = new List<string>(this.CustomRules)
            };

            foreach (var ex in this.Examples)
            {
                var exClone = ex.Clone();
                exClone.PersonaId = cloned.Id;
                cloned.Examples.Add(exClone);
            }

            return cloned;
        }
    }
}

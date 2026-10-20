using System;
using System.Text.Json.Serialization;

namespace VideoNarrator.Models
{
    public class PromptVersion
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        [JsonPropertyName("persona_id")]
        public string PersonaId { get; set; } = string.Empty;

        [JsonPropertyName("persona_name")]
        public string PersonaName { get; set; } = string.Empty;

        [JsonPropertyName("version_tag")]
        public string VersionTag { get; set; } = "v1.0";

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; } = DateTime.Now;

        [JsonPropertyName("note")]
        public string Note { get; set; } = string.Empty;

        [JsonPropertyName("compiled_system_prompt")]
        public string CompiledSystemPrompt { get; set; } = string.Empty;

        [JsonPropertyName("style_config_snapshot")]
        public StyleEngineConfig StyleConfigSnapshot { get; set; } = new();
    }
}

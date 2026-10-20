using System.Text.Json.Serialization;

namespace VideoNarrator.Models
{
    public class SoundEffectEvent
    {
        [JsonPropertyName("filePath")]
        public string FilePath { get; set; } = string.Empty;

        [JsonPropertyName("start")]
        public double Start { get; set; }

        [JsonPropertyName("duration")]
        public double Duration { get; set; } = 1.0;

        [JsonPropertyName("volume")]
        public double Volume { get; set; } = 0.5;

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;

        [JsonPropertyName("sfxType")]
        public string SfxType { get; set; } = "whoosh"; // whoosh, impact, click, transition, mechanical, ambience
    }
}

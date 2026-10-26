using System;
using System.Text.Json.Serialization;

namespace VideoNarrator.Models
{
    /// <summary>
    /// Cấu hình phong cách và thông số điều khiển động cho Persona Engine
    /// </summary>
    public class NarrationPersonaConfig
    {
        [JsonPropertyName("presetId")]
        public string PresetId { get; set; } = "Curiosity";

        [JsonPropertyName("energy")]
        public int Energy { get; set; } = 8; // 1 - 10

        [JsonPropertyName("curiosity")]
        public int Curiosity { get; set; } = 9; // 1 - 10

        [JsonPropertyName("humor")]
        public int Humor { get; set; } = 4; // 1 - 10

        [JsonPropertyName("drama")]
        public int Drama { get; set; } = 7; // 1 - 10

        [JsonPropertyName("technicalDepth")]
        public int TechnicalDepth { get; set; } = 6; // 1 - 10

        [JsonPropertyName("sentenceVariation")]
        public int SentenceVariation { get; set; } = 8; // 1 - 10

        [JsonPropertyName("narrationSpeed")]
        public double NarrationSpeed { get; set; } = 1.15; // 0.8x - 1.3x

        [JsonPropertyName("audienceLevel")]
        public string AudienceLevel { get; set; } = "General"; // General, Enthusiast, Expert

        [JsonPropertyName("targetDurationSeconds")]
        public double TargetDurationSeconds { get; set; } = 30.0; // 15, 30, 45, 60 or custom

        [JsonPropertyName("minWords")]
        public int MinWords { get; set; } = 80;

        [JsonPropertyName("maxWords")]
        public int MaxWords { get; set; } = 115;

        public NarrationPersonaConfig Clone()
        {
            return new NarrationPersonaConfig
            {
                PresetId = this.PresetId,
                Energy = this.Energy,
                Curiosity = this.Curiosity,
                Humor = this.Humor,
                Drama = this.Drama,
                TechnicalDepth = this.TechnicalDepth,
                SentenceVariation = this.SentenceVariation,
                NarrationSpeed = this.NarrationSpeed,
                AudienceLevel = this.AudienceLevel,
                TargetDurationSeconds = this.TargetDurationSeconds,
                MinWords = this.MinWords,
                MaxWords = this.MaxWords
            };
        }
    }
}

using System;
using System.Text.Json.Serialization;

namespace VideoNarrator.Models
{
    public class StyleEngineConfig
    {
        // Sliders (Thang điểm 0 - 100)
        [JsonPropertyName("curiosity_level")]
        public int CuriosityLevel { get; set; } = 85;

        [JsonPropertyName("dramatic_level")]
        public int DramaticLevel { get; set; } = 75;

        [JsonPropertyName("weirdness_level")]
        public int WeirdnessLevel { get; set; } = 60;

        [JsonPropertyName("sarcasm_level")]
        public int SarcasmLevel { get; set; } = 40;

        [JsonPropertyName("visual_language_level")]
        public int VisualLanguageLevel { get; set; } = 90;

        [JsonPropertyName("hook_intensity")]
        public int HookIntensity { get; set; } = 95;

        [JsonPropertyName("suspense_level")]
        public int SuspenseLevel { get; set; } = 80;

        [JsonPropertyName("satisfaction_level")]
        public int SatisfactionLevel { get; set; } = 85;

        // Dropdown selections
        [JsonPropertyName("sentence_rhythm")]
        public string SentenceRhythm { get; set; } = "alternating"; 
        // "alternating" (ngắn xen kẽ dài), "rapid_fire" (dồn dập), "slow_burn" (thong thả bí ẩn), "syncopated" (nhấn ngắt bất ngờ)

        [JsonPropertyName("narration_speed")]
        public string NarrationSpeed { get; set; } = "rapid_1.2x"; 
        // "rapid_1.2x" (1.2x tốc độ cao), "normal_1.0x" (bình thường), "fast_1.1x" (1.1x)

        [JsonPropertyName("ending_style")]
        public string EndingStyle { get; set; } = "mind_bending"; 
        // "mind_bending" (hack não/bất ngờ), "punchline" (chốt hạ hài hước), "loop_seamless" (vòng lặp vô tận), "cliffhanger" (lửng lơ tò mò), "payoff_climax" (chốt hạ cao trào mãn nhãn)

        [JsonPropertyName("verbosity")]
        public string Verbosity { get; set; } = "balanced"; 
        // "concise" (cô đọng, ít từ), "balanced" (vừa vặn nhịp nhàng), "detailed" (giàu chi tiết tả cảnh)

        public StyleEngineConfig Clone()
        {
            return new StyleEngineConfig
            {
                CuriosityLevel = this.CuriosityLevel,
                DramaticLevel = this.DramaticLevel,
                WeirdnessLevel = this.WeirdnessLevel,
                SarcasmLevel = this.SarcasmLevel,
                VisualLanguageLevel = this.VisualLanguageLevel,
                HookIntensity = this.HookIntensity,
                SuspenseLevel = this.SuspenseLevel,
                SatisfactionLevel = this.SatisfactionLevel,
                SentenceRhythm = this.SentenceRhythm,
                NarrationSpeed = this.NarrationSpeed,
                EndingStyle = this.EndingStyle,
                Verbosity = this.Verbosity
            };
        }
    }
}

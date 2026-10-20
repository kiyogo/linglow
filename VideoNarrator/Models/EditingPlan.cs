using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VideoNarrator.Models
{
    public class EditingPlan
    {
        [JsonPropertyName("stylePreset")]
        public string StylePreset { get; set; } = "TikTok Shorts";

        [JsonPropertyName("videoFormat")]
        public string VideoFormat { get; set; } = "9:16";

        [JsonPropertyName("outputWidth")]
        public int OutputWidth { get; set; } = 1080;

        [JsonPropertyName("outputHeight")]
        public int OutputHeight { get; set; } = 1920;

        [JsonPropertyName("targetFps")]
        public int TargetFps { get; set; } = 30;

        [JsonPropertyName("hook")]
        public string Hook { get; set; } = string.Empty;

        [JsonPropertyName("narrationScript")]
        public string NarrationScript { get; set; } = string.Empty;

        [JsonPropertyName("musicStyle")]
        public string MusicStyle { get; set; } = "energetic";

        [JsonPropertyName("musicVolume")]
        public double MusicVolume { get; set; } = 0.12;

        [JsonPropertyName("duckingVolume")]
        public double DuckingVolume { get; set; } = 0.04;

        [JsonPropertyName("focusX")]
        public double FocusX { get; set; } = 0.5;

        [JsonPropertyName("focusY")]
        public double FocusY { get; set; } = 0.5;

        [JsonPropertyName("scenes")]
        public List<VideoScene> Scenes { get; set; } = new();

        [JsonPropertyName("importantMoments")]
        public List<ImportantMoment> ImportantMoments { get; set; } = new();

        [JsonPropertyName("segments")]
        public List<EditingSegment> Segments { get; set; } = new();

        [JsonPropertyName("zoomEffects")]
        public List<ZoomEffect> ZoomEffects { get; set; } = new();

        [JsonPropertyName("subtitles")]
        public List<SubtitleSegment> Subtitles { get; set; } = new();

        [JsonPropertyName("bRollRequests")]
        public List<BRollRequest> BRollRequests { get; set; } = new();

        [JsonPropertyName("soundEffects")]
        public List<SoundEffectEvent> SoundEffects { get; set; } = new();

        [JsonPropertyName("transitions")]
        public List<TransitionEvent> Transitions { get; set; } = new();
    }
}

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VideoNarrator.Models
{
    public class VideoProject
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        [JsonPropertyName("name")]
        public string Name { get; set; } = "Dự Án Video Mới";

        [JsonPropertyName("topic")]
        public string Topic { get; set; } = string.Empty;

        [JsonPropertyName("aspectRatio")]
        public string AspectRatio { get; set; } = "9:16"; // "9:16", "16:9", "1:1"

        [JsonPropertyName("outputWidth")]
        public int OutputWidth { get; set; } = 1080;

        [JsonPropertyName("outputHeight")]
        public int OutputHeight { get; set; } = 1920;

        [JsonPropertyName("personaId")]
        public string PersonaId { get; set; } = "mad_muse";

        [JsonPropertyName("stylePreset")]
        public string StylePreset { get; set; } = "TikTok Shorts";

        [JsonPropertyName("hook")]
        public string Hook { get; set; } = string.Empty;

        [JsonPropertyName("fullScript")]
        public string FullScript { get; set; } = string.Empty;

        [JsonPropertyName("scenes")]
        public List<ProjectScene> Scenes { get; set; } = new();

        [JsonPropertyName("audioSettings")]
        public ProjectAudioSettings AudioSettings { get; set; } = new();

        [JsonPropertyName("subtitleSettings")]
        public ProjectSubtitleSettings SubtitleSettings { get; set; } = new();

        [JsonPropertyName("finalVideoPath")]
        public string FinalVideoPath { get; set; } = string.Empty;

        [JsonPropertyName("proxyVideoPath")]
        public string ProxyVideoPath { get; set; } = string.Empty;

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [JsonPropertyName("updatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public void ApplyAspectRatio(string ratio)
        {
            AspectRatio = ratio;
            switch (ratio)
            {
                case "16:9":
                    OutputWidth = 1920;
                    OutputHeight = 1080;
                    break;
                case "1:1":
                    OutputWidth = 1080;
                    OutputHeight = 1080;
                    break;
                case "9:16":
                default:
                    OutputWidth = 1080;
                    OutputHeight = 1920;
                    AspectRatio = "9:16";
                    break;
            }
        }
    }

    public class ProjectScene
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("sceneId")]
        public string SceneId { get; set; } = Guid.NewGuid().ToString("N");

        [JsonPropertyName("narration")]
        public string Narration { get; set; } = string.Empty;

        [JsonPropertyName("visualPrompt")]
        public string VisualPrompt { get; set; } = string.Empty;

        [JsonPropertyName("mediaPath")]
        public string MediaPath { get; set; } = string.Empty;

        [JsonPropertyName("isVideoClip")]
        public bool IsVideoClip { get; set; } = false;

        [JsonPropertyName("duration")]
        public double Duration { get; set; } = 4.0;

        [JsonIgnore]
        public double EstimatedDuration
        {
            get => Duration;
            set => Duration = value;
        }

        [JsonIgnore]
        public double ActualDuration
        {
            get => Duration;
            set => Duration = value;
        }

        [JsonPropertyName("transitionType")]
        public string TransitionType { get; set; } = "crossfade";

        [JsonPropertyName("zoomDirection")]
        public string ZoomDirection { get; set; } = "zoom-in"; // "zoom-in", "zoom-out", "pan-left", "pan-right"
    }

    public class ProjectAudioSettings
    {
        [JsonPropertyName("ttsProvider")]
        public string TtsProvider { get; set; } = "Edge-TTS";

        [JsonPropertyName("ttsVoice")]
        public string TtsVoice { get; set; } = "vi-VN-NamMinhNeural (Nam - Tiếng Việt)";

        [JsonPropertyName("speechRate")]
        public double SpeechRate { get; set; } = 1.05;

        [JsonPropertyName("speechPitch")]
        public double SpeechPitch { get; set; } = 2.0;

        [JsonPropertyName("backgroundMusicPath")]
        public string BackgroundMusicPath { get; set; } = string.Empty;

        [JsonPropertyName("backgroundMusicVolume")]
        public double BackgroundMusicVolume { get; set; } = 0.12;

        [JsonPropertyName("enableMusicDucking")]
        public bool EnableMusicDucking { get; set; } = true;

        [JsonPropertyName("enableSoundEffects")]
        public bool EnableSoundEffects { get; set; } = false;
    }

    public class ProjectSubtitleSettings
    {
        [JsonPropertyName("presetName")]
        public string PresetName { get; set; } = "TikTok (Từng từ hiện lên - Đẹp nhất)";

        [JsonPropertyName("enableKeywordHighlight")]
        public bool EnableKeywordHighlight { get; set; } = true;

        [JsonPropertyName("fontSize")]
        public int FontSize { get; set; } = 65;

        [JsonPropertyName("primaryColor")]
        public string PrimaryColor { get; set; } = "#FFFFFF";

        [JsonPropertyName("highlightColor")]
        public string HighlightColor { get; set; } = "#FFE500";
    }
}

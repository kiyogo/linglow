using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VideoNarrator.Models
{
    public class AIRequest
    {
        public string SystemInstruction { get; set; } = string.Empty;
        public string UserPrompt { get; set; } = string.Empty;
        public string Model { get; set; } = "gemini-3.8-flash";
        public double Temperature { get; set; } = 0.72;
        public double TopP { get; set; } = 0.95;
        public int MaxTokens { get; set; } = 4096;
        public string ResponseFormat { get; set; } = "text"; // "text" or "json"
        public string? VideoFilePath { get; set; }
    }

    public class AIResponse
    {
        public bool Success { get; set; }
        public string Text { get; set; } = string.Empty;
        public string RawResponse { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public long DurationMs { get; set; }
    }

    public class StructuredScriptOutput
    {
        [JsonPropertyName("hook")]
        public string Hook { get; set; } = string.Empty;

        [JsonPropertyName("buildup")]
        public string Buildup { get; set; } = string.Empty;

        [JsonPropertyName("escalation")]
        public string Escalation { get; set; } = string.Empty;

        [JsonPropertyName("payoff")]
        public string Payoff { get; set; } = string.Empty;

        [JsonPropertyName("ending")]
        public string Ending { get; set; } = string.Empty;

        [JsonPropertyName("full_narration")]
        public string FullNarration { get; set; } = string.Empty;

        [JsonPropertyName("estimated_duration_seconds")]
        public double EstimatedDurationSeconds { get; set; }
    }

    public class VideoPromptScene
    {
        [JsonPropertyName("scene_index")]
        public int SceneIndex { get; set; }

        [JsonPropertyName("timestamp_start")]
        public double TimestampStart { get; set; }

        [JsonPropertyName("timestamp_end")]
        public double TimestampEnd { get; set; }

        [JsonPropertyName("visual_prompt")]
        public string VisualPrompt { get; set; } = string.Empty;

        [JsonPropertyName("camera_motion")]
        public string CameraMotion { get; set; } = string.Empty;

        [JsonPropertyName("lighting_mood")]
        public string LightingMood { get; set; } = string.Empty;

        [JsonPropertyName("narration_line")]
        public string NarrationLine { get; set; } = string.Empty;
    }

    public class VideoGenerationPromptOutput
    {
        [JsonPropertyName("video_title")]
        public string VideoTitle { get; set; } = string.Empty;

        [JsonPropertyName("overall_visual_style")]
        public string OverallVisualStyle { get; set; } = string.Empty;

        [JsonPropertyName("aspect_ratio")]
        public string AspectRatio { get; set; } = "9:16";

        [JsonPropertyName("scenes")]
        public List<VideoPromptScene> Scenes { get; set; } = new();
    }
}

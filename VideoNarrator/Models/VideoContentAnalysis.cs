using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VideoNarrator.Models
{
    /// <summary>
    /// Giai đoạn A — Content Understanding: Dữ liệu phân tích khách quan trích xuất từ video đa phương thức
    /// </summary>
    public class VideoContentAnalysis
    {
        [JsonPropertyName("videoTopic")]
        public string VideoTopic { get; set; } = string.Empty;

        [JsonPropertyName("mainSubject")]
        public string MainSubject { get; set; } = string.Empty;

        [JsonPropertyName("observedEvents")]
        public List<string> ObservedEvents { get; set; } = new();

        [JsonPropertyName("keyVisualDetails")]
        public List<string> KeyVisualDetails { get; set; } = new();

        [JsonPropertyName("curiosityPoint")]
        public string CuriosityPoint { get; set; } = string.Empty;

        [JsonPropertyName("explanation")]
        public string Explanation { get; set; } = string.Empty;

        [JsonPropertyName("payoff")]
        public string Payoff { get; set; } = string.Empty;

        [JsonPropertyName("uncertainties")]
        public List<string> Uncertainties { get; set; } = new();

        [JsonPropertyName("sceneTimeline")]
        public List<AnalysisSceneItem> SceneTimeline { get; set; } = new();

        [JsonPropertyName("overallPacing")]
        public string OverallPacing { get; set; } = "medium"; // slow, medium, fast
    }

    public class AnalysisSceneItem
    {
        [JsonPropertyName("startTime")]
        public double StartTime { get; set; }

        [JsonPropertyName("endTime")]
        public double EndTime { get; set; }

        [JsonPropertyName("visualDescription")]
        public string VisualDescription { get; set; } = string.Empty;

        [JsonPropertyName("narrativeRole")]
        public string NarrativeRole { get; set; } = "development"; // hook, context, tension, mechanism, climax, payoff
    }
}

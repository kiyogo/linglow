using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VideoNarrator.Models
{
    /// <summary>
    /// Giai đoạn C — Post-Generation Quality Filter: Kết quả đánh giá chất lượng kịch bản 8 tiêu chí (thang điểm 1-10)
    /// </summary>
    public class ScriptQualityEvaluation
    {
        [JsonPropertyName("hookSpecificity")]
        public double HookSpecificity { get; set; } = 8.0;

        [JsonPropertyName("logicalFlow")]
        public double LogicalFlow { get; set; } = 8.0;

        [JsonPropertyName("informationDensity")]
        public double InformationDensity { get; set; } = 8.0;

        [JsonPropertyName("clicheFree")]
        public double ClicheFree { get; set; } = 8.0;

        [JsonPropertyName("spokenNaturalness")]
        public double SpokenNaturalness { get; set; } = 8.0;

        [JsonPropertyName("visualAccuracy")]
        public double VisualAccuracy { get; set; } = 8.0;

        [JsonPropertyName("payoffSatisfaction")]
        public double PayoffSatisfaction { get; set; } = 8.0;

        [JsonPropertyName("durationFit")]
        public double DurationFit { get; set; } = 8.0;

        [JsonPropertyName("averageScore")]
        public double AverageScore { get; set; } = 8.0;

        [JsonPropertyName("passed")]
        public bool Passed { get; set; } = true;

        [JsonPropertyName("weakSentences")]
        public List<WeakSentenceItem> WeakSentences { get; set; } = new();

        [JsonPropertyName("revisionInstructions")]
        public string RevisionInstructions { get; set; } = string.Empty;
    }

    public class WeakSentenceItem
    {
        [JsonPropertyName("originalSentence")]
        public string OriginalSentence { get; set; } = string.Empty;

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;

        [JsonPropertyName("suggestedFix")]
        public string SuggestedFix { get; set; } = string.Empty;
    }
}

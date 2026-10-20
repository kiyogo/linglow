using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VideoNarrator.Models
{
    public class SubtitleSegment
    {
        [JsonPropertyName("start")]
        public double Start { get; set; }

        [JsonPropertyName("end")]
        public double End { get; set; }

        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("highlightWords")]
        public List<string> HighlightWords { get; set; } = new();

        [JsonPropertyName("wordTimestamps")]
        public List<WordTimestamp> WordTimestamps { get; set; } = new();

        [JsonIgnore]
        public string FormattedTimeRange
        {
            get
            {
                TimeSpan tStart = TimeSpan.FromSeconds(Math.Max(0, Start));
                TimeSpan tEnd = TimeSpan.FromSeconds(Math.Max(0, End));
                return $"[{tStart:mm\\:ss\\.ff} - {tEnd:mm\\:ss\\.ff}]";
            }
        }
    }
}

using System;
using System.Text.Json.Serialization;

namespace VideoNarrator.Models
{
    public class VideoScene
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("start")]
        public double Start { get; set; }

        [JsonPropertyName("end")]
        public double End { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("importance")]
        public string Importance { get; set; } = "Medium";

        [JsonPropertyName("subject")]
        public string Subject { get; set; } = string.Empty;

        [JsonPropertyName("action")]
        public string Action { get; set; } = string.Empty;

        [JsonPropertyName("cameraType")]
        public string CameraType { get; set; } = "Static";

        [JsonPropertyName("importanceScore")]
        public double ImportanceScore { get; set; } = 0.5;

        [JsonPropertyName("focusX")]
        public double FocusX { get; set; } = 0.5;

        [JsonPropertyName("focusY")]
        public double FocusY { get; set; } = 0.5;

        [JsonIgnore]
        public double Duration => Math.Max(0, End - Start);

        [JsonIgnore]
        public string FormattedTimeRange
        {
            get
            {
                TimeSpan tStart = TimeSpan.FromSeconds(Math.Max(0, Start));
                TimeSpan tEnd = TimeSpan.FromSeconds(Math.Max(0, End));
                return $"[{tStart:mm\\:ss} - {tEnd:mm\\:ss}]";
            }
        }
    }
}

using System;
using System.Text.Json.Serialization;

namespace VideoNarrator.Models
{
    public class ImportantMoment
    {
        [JsonPropertyName("start")]
        public double Start { get; set; }

        [JsonPropertyName("end")]
        public double End { get; set; }

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;

        [JsonPropertyName("score")]
        public double Score { get; set; } = 0.8;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

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

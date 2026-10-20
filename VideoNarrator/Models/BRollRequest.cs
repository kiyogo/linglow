using System.Text.Json.Serialization;

namespace VideoNarrator.Models
{
    public class BRollRequest
    {
        [JsonPropertyName("query")]
        public string Query { get; set; } = string.Empty;

        [JsonPropertyName("start")]
        public double Start { get; set; }

        [JsonPropertyName("end")]
        public double End { get; set; }

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;

        [JsonPropertyName("importance")]
        public double Importance { get; set; } = 0.7;

        [JsonIgnore]
        public string? MatchedFilePath { get; set; }
    }
}

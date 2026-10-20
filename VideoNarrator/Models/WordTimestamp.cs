using System.Text.Json.Serialization;

namespace VideoNarrator.Models
{
    public class WordTimestamp
    {
        [JsonPropertyName("word")]
        public string Word { get; set; } = string.Empty;

        [JsonPropertyName("start")]
        public double Start { get; set; }

        [JsonPropertyName("end")]
        public double End { get; set; }
    }
}

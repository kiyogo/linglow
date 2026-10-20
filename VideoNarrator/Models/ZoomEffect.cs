using System.Text.Json.Serialization;

namespace VideoNarrator.Models
{
    public class ZoomEffect
    {
        [JsonPropertyName("start")]
        public double Start { get; set; }

        [JsonPropertyName("end")]
        public double End { get; set; }

        [JsonPropertyName("fromScale")]
        public double FromScale { get; set; } = 1.0;

        [JsonPropertyName("toScale")]
        public double ToScale { get; set; } = 1.08;

        [JsonPropertyName("easing")]
        public string Easing { get; set; } = "linear";
    }
}

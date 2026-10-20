using System.Text.Json.Serialization;

namespace VideoNarrator.Models
{
    public class TransitionEvent
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "Cut"; // Cut, Fade, Crossfade, Quick Zoom, Slide

        [JsonPropertyName("atTime")]
        public double AtTime { get; set; }

        [JsonPropertyName("duration")]
        public double Duration { get; set; } = 0.25;
    }
}

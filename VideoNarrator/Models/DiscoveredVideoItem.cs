using System;
using System.Text.Json.Serialization;

namespace VideoNarrator.Models
{
    public class DiscoveredVideoItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("duration")]
        public double Duration { get; set; }

        [JsonPropertyName("thumbnail")]
        public string Thumbnail { get; set; } = string.Empty;

        [JsonPropertyName("uploader")]
        public string Uploader { get; set; } = string.Empty;

        [JsonPropertyName("viewCount")]
        public long ViewCount { get; set; }

        [JsonPropertyName("platform")]
        public string Platform { get; set; } = "YouTube";

        public string DurationFormatted
        {
            get
            {
                if (Duration <= 0) return "--:--";
                var ts = TimeSpan.FromSeconds(Duration);
                return ts.TotalHours >= 1
                    ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                    : $"{ts.Minutes}:{ts.Seconds:D2}";
            }
        }
    }
}

using System;
using System.Text.Json.Serialization;

namespace VideoNarrator.Models
{
    public class StickerOverlayEvent
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        [JsonPropertyName("start")]
        public double Start { get; set; }

        [JsonPropertyName("end")]
        public double End { get; set; }

        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("emoji")]
        public string Emoji { get; set; } = "💡";

        /// <summary>
        /// "topic_badge", "fact_badge", "highlight_callout", "subscribe_callout"
        /// </summary>
        [JsonPropertyName("stickerType")]
        public string StickerType { get; set; } = "fact_badge";

        /// <summary>
        /// "top", "upper_center", "center", "bottom"
        /// </summary>
        [JsonPropertyName("position")]
        public string Position { get; set; } = "upper_center";
    }
}

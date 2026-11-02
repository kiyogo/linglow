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
        /// "topic_badge", "fact_badge", "highlight_callout", "subscribe_callout", "red_circle"
        /// </summary>
        [JsonPropertyName("stickerType")]
        public string StickerType { get; set; } = "fact_badge";

        /// <summary>
        /// "top", "upper_center", "center", "bottom", "custom"
        /// </summary>
        [JsonPropertyName("position")]
        public string Position { get; set; } = "upper_center";

        [JsonPropertyName("focusX")]
        public double FocusX { get; set; } = 0.5;

        [JsonPropertyName("focusY")]
        public double FocusY { get; set; } = 0.5;

        [JsonPropertyName("radiusX")]
        public int RadiusX { get; set; } = 140;

        [JsonPropertyName("radiusY")]
        public int RadiusY { get; set; } = 120;
    }
}

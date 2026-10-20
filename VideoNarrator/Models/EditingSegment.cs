using System;
using System.Text.Json.Serialization;

namespace VideoNarrator.Models
{
    public class EditingSegment
    {
        [JsonPropertyName("sourceStart")]
        public double SourceStart { get; set; }

        [JsonPropertyName("sourceEnd")]
        public double SourceEnd { get; set; }

        [JsonPropertyName("outputStart")]
        public double OutputStart { get; set; }

        [JsonPropertyName("outputEnd")]
        public double OutputEnd { get; set; }

        [JsonPropertyName("purpose")]
        public string Purpose { get; set; } = "Content";

        [JsonPropertyName("importance")]
        public double Importance { get; set; } = 0.8;

        [JsonIgnore]
        public double SourceDuration => Math.Max(0, SourceEnd - SourceStart);

        [JsonIgnore]
        public double OutputDuration => Math.Max(0, OutputEnd - OutputStart);
    }
}

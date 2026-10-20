using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace VideoNarrator.Models
{
    public class VideoAnalysisResult
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("summary")]
        public string Summary { get; set; } = string.Empty;

        [JsonPropertyName("segments")]
        public List<ScriptSegment> Segments { get; set; } = new();

        [JsonIgnore]
        public string FormattedScriptText
        {
            get
            {
                var sb = new StringBuilder();
                if (Segments != null && Segments.Count > 0)
                {
                    for (int i = 0; i < Segments.Count; i++)
                    {
                        var seg = Segments[i];
                        sb.AppendLine(seg.FormattedTimeRange);
                        sb.AppendLine(seg.Text?.Trim() ?? string.Empty);
                        if (i < Segments.Count - 1)
                        {
                            sb.AppendLine();
                        }
                    }
                }
                return sb.ToString();
            }
        }

        [JsonIgnore]
        public double TotalDurationSeconds
        {
            get
            {
                if (Segments == null || Segments.Count == 0) return 0;
                double maxEnd = 0;
                foreach (var seg in Segments)
                {
                    if (seg.End > maxEnd) maxEnd = seg.End;
                }
                return maxEnd;
            }
        }
    }
}

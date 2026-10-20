using System;
using System.Collections.Generic;

namespace VideoNarrator.Models
{
    public class StyleValidationResult
    {
        public bool IsValid { get; set; } = true;
        public int QualityScore { get; set; } = 100;
        public bool HasHook { get; set; } = true;
        public bool HasPayoff { get; set; } = true;
        public List<string> DetectedCliches { get; set; } = new();
        public List<string> RepetitivePhrases { get; set; } = new();
        public List<string> Issues { get; set; } = new();
        public List<string> Suggestions { get; set; } = new();
        public int WordCount { get; set; }
        public double EstimatedDurationSeconds { get; set; }
        public string SanitizedOutput { get; set; } = string.Empty;
    }
}

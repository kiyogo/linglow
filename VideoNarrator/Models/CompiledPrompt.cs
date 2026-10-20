using System;

namespace VideoNarrator.Models
{
    public class CompiledPrompt
    {
        public string PersonaId { get; set; } = string.Empty;
        public string PersonaName { get; set; } = string.Empty;
        public string SystemInstruction { get; set; } = string.Empty;
        public string UserPrompt { get; set; } = string.Empty;
        public string OutputMode { get; set; } = "script_only";
        public int EstimatedTokens { get; set; }
        public double EstimatedCostUsd { get; set; }
        public string RedactedPreview { get; set; } = string.Empty;
        public DateTime GeneratedAt { get; set; } = DateTime.Now;
    }
}

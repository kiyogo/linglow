using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using VideoNarrator.Helpers;
using VideoNarrator.Models;

namespace VideoNarrator.Services.PersonalityEngine
{
    public interface IOutputFormatter
    {
        string FormatOutput(string rawResponse, string outputMode);
        StructuredScriptOutput? ParseStructuredScript(string rawResponse);
        VideoGenerationPromptOutput? ParseVideoPrompt(string rawResponse);
        EditingPlan? ParseEditingPlan(string rawResponse);
    }

    public class OutputFormatter : IOutputFormatter
    {
        public string FormatOutput(string rawResponse, string outputMode)
        {
            if (string.IsNullOrWhiteSpace(rawResponse)) return string.Empty;

            switch (outputMode)
            {
                case "script_only":
                    return ExtractScriptOnly(rawResponse);

                case "structured_script":
                    var structObj = ParseStructuredScript(rawResponse);
                    if (structObj != null)
                    {
                        return JsonSerializer.Serialize(structObj, new JsonSerializerOptions { WriteIndented = true });
                    }
                    return ExtractJson(rawResponse);

                case "video_generation_prompt":
                    var promptObj = ParseVideoPrompt(rawResponse);
                    if (promptObj != null)
                    {
                        return JsonSerializer.Serialize(promptObj, new JsonSerializerOptions { WriteIndented = true });
                    }
                    return ExtractJson(rawResponse);

                case "full_video_package":
                default:
                    return ExtractJson(rawResponse);
            }
        }

        public StructuredScriptOutput? ParseStructuredScript(string rawResponse)
        {
            string json = ExtractJson(rawResponse);
            return JsonHelper.DeserializeSafe<StructuredScriptOutput>(json);
        }

        public VideoGenerationPromptOutput? ParseVideoPrompt(string rawResponse)
        {
            string json = ExtractJson(rawResponse);
            return JsonHelper.DeserializeSafe<VideoGenerationPromptOutput>(json);
        }

        public EditingPlan? ParseEditingPlan(string rawResponse)
        {
            string json = ExtractJson(rawResponse);
            return JsonHelper.DeserializeSafe<EditingPlan>(json);
        }

        private static string ExtractScriptOnly(string raw)
        {
            string clean = raw.Trim();

            // Strip ``` markdown
            clean = Regex.Replace(clean, @"^```[a-zA-Z]*\r?\n?", "", RegexOptions.Multiline);
            clean = Regex.Replace(clean, @"\r?\n?```$", "", RegexOptions.Multiline);

            if (clean.StartsWith("{") && clean.EndsWith("}"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(clean);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("full_narration", out var fn))
                    {
                        return fn.GetString()?.Trim() ?? clean;
                    }
                    if (root.TryGetProperty("narrationScript", out var ns))
                    {
                        return ns.GetString()?.Trim() ?? clean;
                    }
                }
                catch { }
            }

            clean = Regex.Replace(clean, @"^(kịch bản|lời thoại|narration script|script)[\s:]*", "", RegexOptions.IgnoreCase).Trim();
            if (clean.StartsWith("\"") && clean.EndsWith("\"") && clean.Length > 2)
            {
                clean = clean.Substring(1, clean.Length - 2).Trim();
            }

            return clean;
        }

        private static string ExtractJson(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "{}";
            string clean = raw.Trim();

            var match = Regex.Match(clean, @"```(?:json)?\s*(\{[\s\S]*?\})\s*```", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }

            int firstBrace = clean.IndexOf('{');
            int lastBrace = clean.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                return clean.Substring(firstBrace, lastBrace - firstBrace + 1).Trim();
            }

            return clean;
        }
    }
}

using System;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VideoNarrator.Helpers
{
    public static class JsonHelper
    {
        public static readonly JsonSerializerOptions DefaultOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static string CleanJsonString(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            string text = raw.Trim();

            // Remove markdown code fences like ```json ... ``` or ``` ... ```
            if (text.StartsWith("```", StringComparison.Ordinal))
            {
                int firstLineBreak = text.IndexOf('\n');
                if (firstLineBreak >= 0)
                {
                    text = text.Substring(firstLineBreak + 1);
                }
                else
                {
                    text = text.Trim('`');
                }
            }

            if (text.EndsWith("```", StringComparison.Ordinal))
            {
                int lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
                if (lastFence >= 0)
                {
                    text = text.Substring(0, lastFence);
                }
            }

            text = text.Trim();

            // Find first '{' and extract the balanced root JSON object
            int firstBrace = text.IndexOf('{');
            if (firstBrace >= 0)
            {
                int depth = 0;
                bool inQuotes = false;
                bool escape = false;
                int endBrace = -1;
                for (int i = firstBrace; i < text.Length; i++)
                {
                    char c = text[i];
                    if (escape)
                    {
                        escape = false;
                        continue;
                    }
                    if (c == '\\')
                    {
                        escape = true;
                        continue;
                    }
                    if (c == '"')
                    {
                        inQuotes = !inQuotes;
                        continue;
                    }
                    if (!inQuotes)
                    {
                        if (c == '{') depth++;
                        else if (c == '}')
                        {
                            depth--;
                            if (depth == 0)
                            {
                                endBrace = i;
                                break;
                            }
                        }
                    }
                }

                if (endBrace > firstBrace)
                {
                    text = text.Substring(firstBrace, endBrace - firstBrace + 1);
                }
                else
                {
                    int lastBrace = text.LastIndexOf('}');
                    if (lastBrace > firstBrace)
                    {
                        text = text.Substring(firstBrace, lastBrace - firstBrace + 1);
                    }
                }
            }

            // Remove illegal trailing commas before closing braces/brackets if any
            text = Regex.Replace(text, @",\s*([\]}])", "$1");

            return text;
        }

        public static T? DeserializeSafe<T>(string rawJson) where T : class
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return null;
            }

            string cleaned = CleanJsonString(rawJson);
            try
            {
                return JsonSerializer.Deserialize<T>(cleaned, DefaultOptions);
            }
            catch (Exception ex)
            {
                throw new FormatException($"Không thể giải mã phản hồi JSON từ AI: {ex.Message}. Nội dung nhận được:\n{rawJson}", ex);
            }
        }

        public static string Serialize<T>(T obj)
        {
            return JsonSerializer.Serialize(obj, DefaultOptions);
        }
    }
}

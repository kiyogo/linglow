using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using VideoNarrator.Models;

namespace VideoNarrator.Services.PersonalityEngine
{
    public interface IStyleValidator
    {
        StyleValidationResult Validate(
            string rawOutput,
            string outputMode,
            PersonaProfile persona,
            double targetDurationSeconds);

        string SanitizeScriptOnlyText(string rawText);
    }

    public class StyleValidator : IStyleValidator
    {
        private static readonly string[] ClichéList =
        {
            "chào mừng các bạn",
            "trong video này chúng ta",
            "hãy cùng theo dõi",
            "hãy cùng khám phá",
            "bạn sẽ không tin",
            "đừng chớp mắt",
            "một khung cảnh tuyệt vời",
            "nhưng rồi điều bất ngờ",
            "khiến ai nấy đều phải",
            "cùng đón xem",
            "như chúng ta đã biết",
            "sau đây là kịch bản",
            "gia vị tình cảm",
            "vô cùng thú vị",
            "cực kỳ bất ổn",
            "hành trình khám phá",
            "vô cùng tuyệt vời",
            "khoảnh khắc đáng nhớ",
            "cười không ngậm được mồm",
            "bất lực giùm",
            "cười nghiêng ngả",
            "hãy like",
            "bấm like",
            "đăng ký kênh",
            "theo dõi kênh",
            "follow kênh",
            "để lại bình luận",
            "thả tim",
            "chia sẻ video"
        };

        public StyleValidationResult Validate(
            string rawOutput,
            string outputMode,
            PersonaProfile persona,
            double targetDurationSeconds)
        {
            var result = new StyleValidationResult();
            if (string.IsNullOrWhiteSpace(rawOutput))
            {
                result.IsValid = false;
                result.QualityScore = 0;
                result.Issues.Add("Nội dung AI trả về hoàn toàn rỗng.");
                return result;
            }

            int score = 100;
            string textToValidate = rawOutput.Trim();

            // 1. Kiểm tra định dạng đầu ra (Output Format Validation)
            if (outputMode == "script_only")
            {
                string cleaned = SanitizeScriptOnlyText(rawOutput);
                result.SanitizedOutput = cleaned;

                // Nếu chứa dấu ngoặc nhọn JSON trong script_only -> trừ điểm
                if (cleaned.StartsWith("{") || cleaned.Contains("\"hook\":"))
                {
                    score -= 35;
                    result.Issues.Add("Yêu cầu chế độ 'script_only' nhưng AI trả về định dạng JSON.");
                    result.Suggestions.Add("Hệ thống sẽ tự động bóc tách lấy phần lời thoại thuần túy.");
                }

                // Nếu chứa lời dẫn đề giải thích
                if (Regex.IsMatch(rawOutput, @"^(dưới đây là|kịch bản:|sau đây là|đây là lời thoại)", RegexOptions.IgnoreCase))
                {
                    score -= 15;
                    result.Issues.Add("Có lời dẫn đề thừa trước kịch bản.");
                }

                textToValidate = cleaned;
            }
            else
            {
                // Các chế độ JSON
                string jsonClean = ExtractJsonFromResponse(rawOutput);
                try
                {
                    using var doc = JsonDocument.Parse(jsonClean);
                    var root = doc.RootElement;
                    result.SanitizedOutput = jsonClean;

                    if (outputMode == "structured_script")
                    {
                        if (!root.TryGetProperty("hook", out _) || !root.TryGetProperty("full_narration", out _))
                        {
                            score -= 25;
                            result.Issues.Add("JSON thiếu trường bắt buộc 'hook' hoặc 'full_narration'.");
                        }
                        if (root.TryGetProperty("full_narration", out var fnProp))
                        {
                            textToValidate = fnProp.GetString() ?? "";
                        }
                    }
                    else if (outputMode == "full_video_package")
                    {
                        if (!root.TryGetProperty("scenes", out _) || !root.TryGetProperty("narrationScript", out _))
                        {
                            score -= 20;
                            result.Issues.Add("Gói full_video_package thiếu trường 'scenes' hoặc 'narrationScript'.");
                        }
                        if (root.TryGetProperty("narrationScript", out var nsProp))
                        {
                            textToValidate = nsProp.GetString() ?? "";
                        }
                    }
                    else if (outputMode == "video_generation_prompt")
                    {
                        if (!root.TryGetProperty("scenes", out _))
                        {
                            score -= 25;
                            result.Issues.Add("JSON video_generation_prompt thiếu mảng 'scenes'.");
                        }
                    }
                }
                catch (JsonException jex)
                {
                    score -= 40;
                    result.Issues.Add($"Cấu trúc JSON không hợp lệ: {jex.Message}");
                    result.Suggestions.Add("Cần làm sạch hoặc yêu cầu model tạo lại định dạng JSON.");
                    result.SanitizedOutput = rawOutput;
                }
            }

            // 2. Kiểm tra Cliches sáo rỗng trên toàn bộ văn bản gốc AI sinh ra
            string lowerText = rawOutput.ToLowerInvariant();
            foreach (var cliche in ClichéList)
            {
                if (lowerText.Contains(cliche))
                {
                    result.DetectedCliches.Add(cliche);
                    score -= 10;
                }
            }
            if (result.DetectedCliches.Count > 0)
            {
                result.Issues.Add($"Phát hiện {result.DetectedCliches.Count} cụm từ sáo rỗng: {string.Join(", ", result.DetectedCliches)}");
                result.Suggestions.Add("Nên thay thế các câu sáo rỗng bằng hình ảnh hoặc câu hỏi trực diện.");
            }

            // 3. Kiểm tra Hook (3 giây đầu)
            if (textToValidate.Length < 10)
            {
                result.HasHook = false;
                score -= 30;
                result.Issues.Add("Kịch bản quá ngắn, không có câu mở đầu (Hook).");
            }
            else
            {
                string firstSentence = textToValidate.Split(new[] { '.', '!', '?', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                if (firstSentence.StartsWith("Chào", StringComparison.OrdinalIgnoreCase) ||
                    firstSentence.StartsWith("Xin chào", StringComparison.OrdinalIgnoreCase))
                {
                    result.HasHook = false;
                    score -= 20;
                    result.Issues.Add("Mở đầu bằng lời chào mừng thay vì một Hook thị giác cuốn hút.");
                }
            }

            // 4. Kiểm tra lặp từ / lặp cấu trúc
            var words = textToValidate.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            result.WordCount = words.Length;

            // Tính thời lượng đọc ước tính (trung bình 2.6 từ/giây ở tốc độ nhanh 1.2x)
            result.EstimatedDurationSeconds = Math.Round(result.WordCount / 2.6, 1);

            // Kiểm tra lặp lại từ khóa đặc biệt quá nhiều lần
            var wordFreq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in words)
            {
                string cleanW = Regex.Replace(w, @"[^\w]", "").ToLowerInvariant();
                if (cleanW.Length >= 4)
                {
                    wordFreq[cleanW] = wordFreq.GetValueOrDefault(cleanW, 0) + 1;
                }
            }

            foreach (var kv in wordFreq.Where(k => k.Value >= 4))
            {
                result.RepetitivePhrases.Add($"Từ '{kv.Key}' lặp {kv.Value} lần");
                score -= 5;
            }

            // 5. Kiểm tra thời lượng so với mục tiêu
            if (targetDurationSeconds > 5.0)
            {
                double durationDiff = Math.Abs(result.EstimatedDurationSeconds - targetDurationSeconds);
                if (result.EstimatedDurationSeconds < targetDurationSeconds * 0.45)
                {
                    score -= 15;
                    result.Issues.Add($"Lời thoại hơi ngắn ({result.EstimatedDurationSeconds:F1}s so với mục tiêu {targetDurationSeconds:F1}s).");
                    result.Suggestions.Add("Có thể bổ sung thêm chi tiết miêu tả hoặc giai đoạn escalation.");
                }
            }

            result.QualityScore = Math.Clamp(score, 10, 100);
            result.IsValid = (result.QualityScore >= 60 && result.Issues.Count <= 2);

            return result;
        }

        public string SanitizeScriptOnlyText(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText)) return string.Empty;

            string text = rawText.Trim();

            // 1. Remove markdown code blocks if model wrapped it
            text = Regex.Replace(text, @"^```[a-zA-Z]*\r?\n?", "", RegexOptions.Multiline);
            text = Regex.Replace(text, @"\r?\n?```$", "", RegexOptions.Multiline);

            // 2. If it's a JSON string, extract the narration field
            if (text.TrimStart().StartsWith("{") && text.TrimEnd().EndsWith("}"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(text);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("full_narration", out var fn))
                    {
                        text = fn.GetString()?.Trim() ?? text;
                    }
                    else if (root.TryGetProperty("narrationScript", out var ns))
                    {
                        text = ns.GetString()?.Trim() ?? text;
                    }
                }
                catch { }
            }

            // 3. Remove leading labels & preambles (e.g. "Kịch bản:", "Dưới đây là kịch bản...")
            text = Regex.Replace(text, @"^(kịch bản|lời thoại|narration script|script|nội dung|bản nháp)[\s:]*", "", RegexOptions.IgnoreCase).Trim();
            text = Regex.Replace(text, @"^(dưới đây là|đây là|sau đây là|here is|this is|below is)[^\n\r.!?:;]*[:.]?\s*", "", RegexOptions.IgnoreCase).Trim();

            // 4. Remove robotic greetings & YouTube clichés at the very start
            text = Regex.Replace(text, @"^(chào mừng\s+(các\s+bạn|mọi\s+người|anh\s+em|quý\s+vị)?[^\n\r.!?:;]*[.!?:;]?)\s*", "", RegexOptions.IgnoreCase).Trim();
            text = Regex.Replace(text, @"^(xin\s+chào\s+(các\s+bạn|mọi\s+người|anh\s+em|quý\s+vị)?[^\n\r.!?:;]*[.!?:;]?)\s*", "", RegexOptions.IgnoreCase).Trim();
            text = Regex.Replace(text, @"^(hello\s+(everyone|guys|viewers)?[^\n\r.!?:;]*[.!?:;]?)\s*", "", RegexOptions.IgnoreCase).Trim();

            // 5. Strip surround quotation marks
            if (text.StartsWith("\"") && text.EndsWith("\"") && text.Length > 2)
            {
                text = text.Substring(1, text.Length - 2).Trim();
            }

            return text;
        }

        private static string ExtractJsonFromResponse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "{}";

            string clean = raw.Trim();

            // Strip ```json ... ```
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

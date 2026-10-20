using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Speech.Synthesis;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VideoNarrator.Models;

namespace VideoNarrator.Services
{
    public class TtsResult
    {
        public string AudioPath { get; set; } = string.Empty;
        public double DurationSeconds { get; set; }
        public List<WordTimestamp> WordTimestamps { get; set; } = new();
    }

    public interface ITtsService
    {
        IReadOnlyList<string> GetSupportedVoices(string provider);
        Task<string> GenerateSpeechAsync(string text, TtsSettings settings, string outputPath, CancellationToken cancellationToken = default);
        Task<TtsResult> GenerateSpeechWithTimestampsAsync(string text, TtsSettings settings, string outputPath, CancellationToken cancellationToken = default);
        Task<bool> TestConnectionAsync(TtsSettings settings, CancellationToken cancellationToken = default);
    }

    public class TtsService : ITtsService
    {
        private readonly HttpClient _httpClient;
        private readonly IFileService _fileService;

        private static readonly string[] OpenAiVoices =
        {
            "alloy", "echo", "fable", "onyx", "nova", "shimmer"
        };

        private static readonly string[] ElevenLabsVoices =
        {
            "21m00Tcm4TlvDq8ikWAM (Rachel)",
            "AZnzlk1XvdvUeBnXmlld (Domi)",
            "EXAVITQu4vr4xnSDxMaL (Bella)",
            "ErXwobaYiN019PkySvjV (Antoni)",
            "MF3mGyEYCl7XYWbV9V6O (Elli)",
            "TxGEqnHWrfWFTfGW9XjX (Josh)",
            "VR6AewLTigWG4xSOukaG (Arnold)",
            "pNInz6obpgDQGcFmaJgB (Adam)",
            "yoZ06aMxZJJ28mfd3POQ (Sam)"
        };

        private static readonly string[] EdgeTtsVoices =
        {
            "vi-VN-NamMinhNeural (Nam - Tiếng Việt)",
            "vi-VN-HoaiMyNeural (Nữ - Tiếng Việt)",
            "en-US-GuyNeural (Nam - Tiếng Anh)",
            "en-US-JennyNeural (Nữ - Tiếng Anh)"
        };

        public TtsService(HttpClient httpClient, IFileService fileService)
        {
            _httpClient = httpClient;
            _fileService = fileService;
        }

        public IReadOnlyList<string> GetSupportedVoices(string provider)
        {
            return provider switch
            {
                "OpenAI" => OpenAiVoices,
                "ElevenLabs" => ElevenLabsVoices,
                "Edge-TTS" or _ => EdgeTtsVoices
            };
        }

        public async Task<bool> TestConnectionAsync(TtsSettings settings, CancellationToken cancellationToken = default)
        {
            try
            {
                string tempAudio = Path.Combine(Path.GetTempPath(), $"tts_test_{Guid.NewGuid():N}.mp3");
                try
                {
                    await GenerateSpeechAsync("Kiểm tra kết nối giọng đọc AI.", settings, tempAudio, cancellationToken);
                    return File.Exists(tempAudio) && new FileInfo(tempAudio).Length > 0;
                }
                finally
                {
                    if (File.Exists(tempAudio))
                    {
                        File.Delete(tempAudio);
                    }
                }
            }
            catch (Exception ex)
            {
                _fileService.Log($"Kiểm tra TTS kết nối thất bại: {ex.Message}", "WARN", ex);
                return false;
            }
        }

        public async Task<string> GenerateSpeechAsync(string text, TtsSettings settings, string outputPath, CancellationToken cancellationToken = default)
        {
            var res = await GenerateSpeechWithTimestampsAsync(text, settings, outputPath, cancellationToken);
            return res.AudioPath;
        }

        public async Task<TtsResult> GenerateSpeechWithTimestampsAsync(string text, TtsSettings settings, string outputPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException("Nội dung kịch bản để tạo giọng đọc bị rỗng.", nameof(text));
            }

            string cleanText = StripTimestampsAndFormatText(text);
            cleanText = NormalizeTextForVietnameseTts(cleanText);
            if (string.IsNullOrWhiteSpace(cleanText))
            {
                throw new ArgumentException("Kịch bản sau khi lọc mốc thời gian không có nội dung chữ.");
            }

            string outDir = Path.GetDirectoryName(outputPath) ?? "";
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }

            _fileService.Log($"Tạo giọng đọc bằng provider: {settings.Provider}, Voice: {settings.Voice}, Text length: {cleanText.Length}");

            TtsResult result;
            if (settings.Provider == "OpenAI")
            {
                string path = await GenerateOpenAiSpeechAsync(cleanText, settings, outputPath, cancellationToken);
                double estDur = File.Exists(path) ? new FileInfo(path).Length / 16000.0 : 35.0;
                result = new TtsResult
                {
                    AudioPath = path,
                    DurationSeconds = estDur,
                    WordTimestamps = EstimateWordTimestamps(cleanText, estDur)
                };
            }
            else if (settings.Provider == "ElevenLabs")
            {
                string path = await GenerateElevenLabsSpeechAsync(cleanText, settings, outputPath, cancellationToken);
                double estDur = File.Exists(path) ? new FileInfo(path).Length / 16000.0 : 35.0;
                result = new TtsResult
                {
                    AudioPath = path,
                    DurationSeconds = estDur,
                    WordTimestamps = EstimateWordTimestamps(cleanText, estDur)
                };
            }
            else
            {
                result = await GenerateEdgeTtsSpeechAsync(cleanText, settings, outputPath, cancellationToken);
                if (result.WordTimestamps == null || result.WordTimestamps.Count == 0)
                {
                    double dur = result.DurationSeconds > 0 ? result.DurationSeconds : (File.Exists(outputPath) ? new FileInfo(outputPath).Length / 6000.0 : 35.0);
                    result.WordTimestamps = EstimateWordTimestamps(cleanText, dur);
                }
            }

            return result;
        }

        public static string StripTimestampsAndFormatText(string script)
        {
            if (string.IsNullOrWhiteSpace(script)) return string.Empty;

            var lines = script.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var sb = new StringBuilder();

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                // Match lines containing only timestamps e.g. [00:00 - 00:05] or [00:00.20 - 00:04.00]
                if (Regex.IsMatch(line, @"^\[\s*[\d:\.,\s-]+\s*\]$"))
                {
                    continue;
                }

                // Remove timestamps inline or at line beginnings
                line = Regex.Replace(line, @"\[\s*[\d:\.,\s-]+\s*\]", "").Trim();
                if (string.IsNullOrEmpty(line)) continue;

                sb.AppendLine(line);
            }

            return sb.ToString().Trim();
        }

        public static string NormalizeTextForVietnameseTts(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            string result = text;

            // 1. Chuẩn hóa các từ viết tắt có thể bị máy đọc sai thành tiếng Việt không dấu:
            // "FA" -> đọc thành "pha" -> Đổi thành "F A" để giọng đọc phát âm chuẩn xác là "ép-ê"
            result = Regex.Replace(result, @"\bFA\b", "F A", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"\bF\.A\b", "F A", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"\bF\.A\.\b", "F A", RegexOptions.IgnoreCase);

            // "mili giây", "miligiây" -> Đổi thành "từng tích tắc" / "tích tắc" tự nhiên hơn
            result = Regex.Replace(result, @"\btừng\s+mili\s*giây\b", "từng tích tắc", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"\bmili\s*giây\b", "tích tắc", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"\bmiligiây\b", "tích tắc", RegexOptions.IgnoreCase);

            // "khớp cạ", "siêu khớp cạ" -> Đổi thành "siêu ăn ý" / "ăn ý"
            result = Regex.Replace(result, @"\bsiêu\s+khớp\s+cạ\b", "siêu ăn ý", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"\bkhớp\s+cạ\b", "ăn ý", RegexOptions.IgnoreCase);

            // GIỮ NGUYÊN các từ tiếng Anh trendy quen thuộc (trend, đu trend, bắt trend, flex, crush, vibe, chill, visual, view, slay, cool ngầu...) vì Edge-TTS phát âm tiếng Anh rất chuẩn, bắt tai và hiện đại!

            // 2. Chữa câu cụt đầu câu gây tụt ngữ điệu khi có dấu chấm (Sentence smoothing)
            result = Regex.Replace(result, @"(^|(?<=[.!?\n]\s*))Từ\s+màn\b", "$1Đầu tiên là màn", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"(^|(?<=[.!?\n]\s*))Cho\s+tới\s+khoảnh\s+khắc\b", "$1Kế tiếp là khoảnh khắc", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"(^|(?<=[.!?\n]\s*))Từ\s+khoảnh\s+khắc\b", "$1Bắt đầu từ khoảnh khắc", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"(^|(?<=[.!?\n]\s*))Cho\s+tới\s+khi\b", "$1Và rồi cho tới khi", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"(^|(?<=[.!?\n]\s*))Cho\s+đến\s+khi\b", "$1Và rồi cho đến khi", RegexOptions.IgnoreCase);

            // 3. Chuẩn hóa dấu câu ngắt nghỉ tự nhiên:
            // GIỮ NGUYÊN các dấu ngắt nghỉ cơ bản (. , ! ? ; :) để Edge-TTS phát âm chuẩn xác 100%,
            // có nhịp thở tự nhiên, rõ từng âm tiết, không bị nuốt từ, không đọc dính chữ hay sai từ.
            result = Regex.Replace(result, @"[\(\)\[\]\{\}""'“”„«»_~*^#/\\]", " ");
            result = Regex.Replace(result, @"\s*,\s*", ", ");
            result = Regex.Replace(result, @"\s*\.\s*", ". ");
            result = Regex.Replace(result, @"\s*!\s*", "! ");
            result = Regex.Replace(result, @"\s*\?\s*", "? ");
            result = Regex.Replace(result, @"\s*;\s*", ", ");
            result = Regex.Replace(result, @"\s*:\s*", ", ");
            result = Regex.Replace(result, @"\s*-\s*", " ");
            result = Regex.Replace(result, @"\s+", " ").Trim();

            // 4. Nếu toàn bộ kịch bản bị viết HOA (ALL CAPS), chuyển về viết hoa đầu câu tự nhiên
            // để tránh Edge-TTS nhầm lẫn thành từ viết tắt tiếng Anh và đánh vần từng chữ cái:
            if (IsMostlyUpperCase(result))
            {
                result = ConvertToSentenceCase(result);
            }

            return result;
        }

        private static bool IsMostlyUpperCase(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            int letters = 0;
            int upper = 0;
            foreach (char c in text)
            {
                if (char.IsLetter(c))
                {
                    letters++;
                    if (char.IsUpper(c)) upper++;
                }
            }
            return letters > 10 && ((double)upper / letters) > 0.65;
        }

        private static string ConvertToSentenceCase(string text)
        {
            var sentences = Regex.Split(text, @"(?<=[.!?\n])\s+");
            var sb = new StringBuilder();
            var viCulture = new CultureInfo("vi-VN");
            for (int i = 0; i < sentences.Length; i++)
            {
                string s = sentences[i].Trim();
                if (string.IsNullOrEmpty(s)) continue;

                string lower = s.ToLower(viCulture);
                string formatted = lower.Length > 1
                    ? char.ToUpper(lower[0], viCulture) + lower.Substring(1)
                    : lower.ToUpper(viCulture);

                if (sb.Length > 0) sb.Append(" ");
                sb.Append(formatted);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Loại bỏ hoàn toàn các câu cầu khiến / mệnh lệnh thức kêu gọi hành động mạng xã hội (CTA câu like/share/sub)
        /// khỏi kịch bản thuyết minh để giữ trọn vẹn mạch truyện tự nhiên.
        /// </summary>
        public static string StripSocialCallToAction(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            var sentences = Regex.Split(text, @"(?<=[.!?\n])\s+")
                                 .Where(s => !string.IsNullOrWhiteSpace(s))
                                 .Select(s => s.Trim())
                                 .ToList();

            if (sentences.Count == 0) return text.Trim();

            var kept = new List<string>();
            foreach (var s in sentences)
            {
                if (!IsSocialCallToAction(s))
                {
                    kept.Add(s);
                }
            }

            if (kept.Count == 0) return text.Trim();
            return string.Join(" ", kept).Trim();
        }

        /// <summary>
        /// Nhận diện xem một câu có phải là câu cầu khiến mang tính tương tác mạng xã hội (CTA câu like) hay không.
        /// </summary>
        public static bool IsSocialCallToAction(string sentence)
        {
            if (string.IsNullOrWhiteSpace(sentence)) return false;

            // 1. Câu cầu khiến kêu gọi hành động mạng xã hội (like, share, subscribe, follow, thả tim, bình luận)
            if (Regex.IsMatch(sentence, @"\b(hãy|nhớ|đừng\s+quên|bấm|nhấn|cùng|nhanh\s+tay)\s+(ngay\s+)?(vào\s+)?(like|thích|share|chia\s+sẻ|đăng\s+ký|sub|subscribe|follow|theo\s+dõi|thả\s+tim|bình\s+luận|comment)\b", RegexOptions.IgnoreCase))
                return true;

            // 2. Kêu gọi tương tác kênh (đăng ký kênh, follow kênh, theo dõi kênh, sub kênh, ủng hộ kênh)
            if (Regex.IsMatch(sentence, @"\b(đăng\s+ký|theo\s+dõi|follow|sub|subscribe|ủng\s+hộ)\s+kênh\b", RegexOptions.IgnoreCase))
                return true;

            // 3. Đuôi câu tương tác kênh ("...kênh để đón xem / không bỏ lỡ")
            if (Regex.IsMatch(sentence, @"\bkênh\s+(của\s+mình|này)?\s*để\s+(xem\s+tiếp|xem\s+thêm|không\s+bỏ\s+lỡ|cập\s+nhật|đón\s+xem)\b", RegexOptions.IgnoreCase))
                return true;

            // 4. Kêu gọi để lại bình luận ("để lại bình luận / ý kiến bên dưới nhé")
            if (Regex.IsMatch(sentence, @"\bđể\s+lại\s+(ý\s+kiến|bình\s+luận|comment|cảm\s+nghĩ|chia\s+sẻ)\s*(của\s+bạn\s*)?(ở|phía|bên)?\s*(dưới|nhé|nha|nhá|nào)\b", RegexOptions.IgnoreCase))
                return true;

            // 5. Xin like / tim / tương tác ("cho mình xin 1 like", "tặng 1 tim")
            if (Regex.IsMatch(sentence, @"\b(cho|tặng|xin)\s+(mình|kênh|chúng\s+mình)?\s*(1|một)?\s*(like|tim|thả\s+tim|chia\s+sẻ|follow|đăng\s+ký|sub)\b", RegexOptions.IgnoreCase))
                return true;

            // 6. Điều kiện câu like ("Nếu thấy hay thì hãy...", "Nếu bạn thấy bổ ích nhớ...")
            if (Regex.IsMatch(sentence, @"\bnếu\s+(bạn\s+)?thấy\s+(hay|thú\s+vị|hữu\s+ích|bổ\s+ích|ý\s+nghĩa)\s*(thì|,)?\s*(hãy|nhớ|đừng\s+quên|bấm|nhấn|cho)\b", RegexOptions.IgnoreCase))
                return true;

            // 7. Nhấn chuông thông báo
            if (Regex.IsMatch(sentence, @"\b(bấm|nhấn)\s+(vào\s+)?(nút\s+)?(chuông|thông\s+báo)\b", RegexOptions.IgnoreCase))
                return true;

            // 8. Cặp từ ghép mạng xã hội ("like và follow", "follow và đăng ký")
            if (Regex.IsMatch(sentence, @"\b(like|follow|share|sub)\s+và\s+(like|follow|share|sub|đăng\s+ký|chia\s+sẻ|thả\s+tim)\b", RegexOptions.IgnoreCase))
                return true;

            return false;
        }

        private async Task<string> GenerateOpenAiSpeechAsync(string text, TtsSettings settings, string outputPath, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                throw new ArgumentException("Chưa nhập OpenAI API Key.");
            }

            string voice = ExtractVoiceCode(settings.Voice, "alloy");
            double speed = Math.Clamp(settings.Speed, 0.25, 4.0);

            var payload = new
            {
                model = string.IsNullOrWhiteSpace(settings.OpenAIModel) ? "tts-1" : settings.OpenAIModel,
                input = text,
                voice = voice,
                speed = speed
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/speech");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                string err = await response.Content.ReadAsStringAsync(cancellationToken);
                _fileService.Log($"Lỗi OpenAI TTS: {response.StatusCode} - {err}", "ERROR");
                throw new InvalidOperationException($"Lỗi OpenAI TTS (HTTP {(int)response.StatusCode}): {err}");
            }

            byte[] audioBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            await File.WriteAllBytesAsync(outputPath, audioBytes, cancellationToken);
            _fileService.Log($"Tạo audio OpenAI thành công: {outputPath} ({audioBytes.Length} bytes)");
            return outputPath;
        }

        private async Task<string> GenerateElevenLabsSpeechAsync(string text, TtsSettings settings, string outputPath, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                throw new ArgumentException("Chưa nhập ElevenLabs API Key.");
            }

            string voiceId = ExtractVoiceCode(settings.Voice, "21m00Tcm4TlvDq8ikWAM");
            string url = $"https://api.elevenlabs.io/v1/text-to-speech/{voiceId}";

            var payload = new
            {
                text = text,
                model_id = string.IsNullOrWhiteSpace(settings.ElevenLabsModelId) ? "eleven_multilingual_v2" : settings.ElevenLabsModelId,
                voice_settings = new
                {
                    stability = 0.5,
                    similarity_boost = 0.75
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("xi-api-key", settings.ApiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                string err = await response.Content.ReadAsStringAsync(cancellationToken);
                _fileService.Log($"Lỗi ElevenLabs TTS: {response.StatusCode} - {err}", "ERROR");
                throw new InvalidOperationException($"Lỗi ElevenLabs TTS (HTTP {(int)response.StatusCode}): {err}");
            }

            byte[] audioBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            await File.WriteAllBytesAsync(outputPath, audioBytes, cancellationToken);
            _fileService.Log($"Tạo audio ElevenLabs thành công: {outputPath} ({audioBytes.Length} bytes)");
            return outputPath;
        }

        private const string EdgeTrustedClientToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
        private const string ChromiumFullVersion = "143.0.3650.75";
        private const string SecMsGecVersion = "1-" + ChromiumFullVersion;
        private const long WinEpoch = 11644473600L;

        private static string GenerateSecMsGec()
        {
            long unixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long ticks = unixSeconds + WinEpoch;
            ticks -= ticks % 300L;
            ticks *= 10000000L;

            string strToHash = $"{ticks}{EdgeTrustedClientToken}";
            using var sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(Encoding.ASCII.GetBytes(strToHash));
            return Convert.ToHexString(hash);
        }

        private static string GenerateMuid()
        {
            byte[] bytes = new byte[16];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToHexString(bytes);
        }

        private async Task<TtsResult> GenerateEdgeTtsSpeechAsync(string text, TtsSettings settings, string outputPath, CancellationToken cancellationToken)
        {
            try
            {
                string voice = ExtractVoiceCode(settings.Voice, "vi-VN-NamMinhNeural");
                var chunks = SplitTextIntoOptimalChunks(text, 550);

                if (chunks.Count == 0)
                {
                    throw new InvalidOperationException("Văn bản kịch bản không có nội dung hợp lệ.");
                }

                if (chunks.Count == 1)
                {
                    return await GenerateEdgeTtsSingleChunkAsync(chunks[0], voice, settings.Speed, settings.Pitch, settings.EmotionStyle, outputPath, cancellationToken);
                }

                _fileService.Log($"Kịch bản dài ({text.Length} ký tự) -> Tự động chia thành {chunks.Count} phân đoạn TTS an toàn.");

                string tempDir = Path.Combine(Path.GetTempPath(), $"tts_chunks_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);
                var chunkAudioPaths = new List<string>();
                var allWordTimestamps = new List<WordTimestamp>();
                double accumulatedDuration = 0.0;

                try
                {
                    for (int i = 0; i < chunks.Count; i++)
                    {
                        string chunkPath = Path.Combine(tempDir, $"chunk_{i:D3}.mp3");
                        var chunkResult = await GenerateEdgeTtsSingleChunkAsync(
                            chunks[i], voice, settings.Speed, settings.Pitch, settings.EmotionStyle, chunkPath, cancellationToken);

                        chunkAudioPaths.Add(chunkResult.AudioPath);

                        foreach (var wt in chunkResult.WordTimestamps)
                        {
                            allWordTimestamps.Add(new WordTimestamp
                            {
                                Word = wt.Word,
                                Start = Math.Round(accumulatedDuration + wt.Start, 3),
                                End = Math.Round(accumulatedDuration + wt.End, 3)
                            });
                        }

                        accumulatedDuration += chunkResult.DurationSeconds;
                        if (i < chunks.Count - 1)
                        {
                            await Task.Delay(200, cancellationToken);
                        }
                    }

                    await ConcatenateMp3FilesAsync(chunkAudioPaths, outputPath, cancellationToken);

                    double exactTotalDuration = File.Exists(outputPath) ? new FileInfo(outputPath).Length / 6000.0 : accumulatedDuration;
                    _fileService.Log($"Ghép {chunks.Count} đoạn giọng đọc Edge-TTS thành công: {outputPath} ({exactTotalDuration:F1}s, {allWordTimestamps.Count} từ)");

                    return new TtsResult
                    {
                        AudioPath = outputPath,
                        DurationSeconds = Math.Round(exactTotalDuration, 2),
                        WordTimestamps = allWordTimestamps
                    };
                }
                finally
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
            catch (Exception ex)
            {
                _fileService.Log($"Edge-TTS không khả dụng: {ex.Message}. Kích hoạt Google Translate TTS (tiếng Việt)...", "WARN");
                try
                {
                    return await GenerateGoogleTranslateTtsAsync(text, outputPath, cancellationToken);
                }
                catch (Exception gex)
                {
                    _fileService.Log($"Google Translate TTS cảnh báo: {gex.Message}. Kích hoạt Windows SAPI Fallback...", "WARN");
                    string voice = ExtractVoiceCode(settings.Voice, "vi-VN-NamMinhNeural");
                    return await GenerateSystemSpeechAsync(text, voice, outputPath, cancellationToken);
                }
            }
        }

        private async Task<TtsResult> GenerateGoogleTranslateTtsAsync(string text, string outputPath, CancellationToken cancellationToken)
        {
            _fileService.Log($"Kích hoạt Google Translate TTS (giọng tiếng Việt vi)...", "INFO");
            var chunks = SplitTextIntoOptimalChunks(text, 180);
            string tempDir = Path.Combine(Path.GetTempPath(), $"gtts_chunks_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            var chunkAudioPaths = new List<string>();

            try
            {
                for (int i = 0; i < chunks.Count; i++)
                {
                    string chunk = chunks[i];
                    string chunkPath = Path.Combine(tempDir, $"gtts_{i:D3}.mp3");
                    string url = $"https://translate.google.com/translate_tts?ie=UTF-8&q={Uri.EscapeDataString(chunk)}&tl=vi&client=tw-ob";

                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                    using var resp = await _httpClient.SendAsync(req, cancellationToken);
                    resp.EnsureSuccessStatusCode();
                    byte[] bytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);
                    await File.WriteAllBytesAsync(chunkPath, bytes, cancellationToken);
                    chunkAudioPaths.Add(chunkPath);

                    if (i < chunks.Count - 1)
                    {
                        await Task.Delay(150, cancellationToken);
                    }
                }

                await ConcatenateMp3FilesAsync(chunkAudioPaths, outputPath, cancellationToken);

                double dur = File.Exists(outputPath) ? new FileInfo(outputPath).Length / 4000.0 : Math.Max(5.0, chunks.Count * 2.5);
                var words = EstimateWordTimestamps(text, dur);

                _fileService.Log($"Tạo giọng đọc Google Translate TTS thành công: {outputPath} ({dur:F1}s)");
                return new TtsResult
                {
                    AudioPath = outputPath,
                    DurationSeconds = Math.Round(dur, 2),
                    WordTimestamps = words
                };
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        private async Task<TtsResult> GenerateEdgeTtsSingleChunkAsync(
            string chunkText,
            string voice,
            double speed,
            double pitch,
            string emotionStyle,
            string chunkOutputPath,
            CancellationToken cancellationToken)
        {
            // Ưu tiên 1: Edge-TTS Python Worker với edge-tts 7.3.1 (DRM v2 mới nhất, có word boundary chính xác 100%)
            var workerResult = await TryGenerateWithEdgeTtsWorkerAsync(chunkText, voice, speed, pitch, chunkOutputPath, cancellationToken);
            if (workerResult != null && File.Exists(chunkOutputPath) && new FileInfo(chunkOutputPath).Length > 0)
            {
                return workerResult;
            }

            // Ưu tiên 2: Edge-TTS Python CLI trực tiếp
            var cliResult = await TryGenerateWithEdgeTtsCliAsync(chunkText, voice, speed, pitch, chunkOutputPath, cancellationToken);
            if (cliResult != null && File.Exists(chunkOutputPath) && new FileInfo(chunkOutputPath).Length > 0)
            {
                return cliResult;
            }

            // Ưu tiên 3: WebSocket với SSML Biểu cảm (thử 2 lần)
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    return await GenerateEdgeTtsViaWebSocketAsync(chunkText, voice, speed, pitch, emotionStyle, chunkOutputPath, cancellationToken, useBasicSsml: false);
                }
                catch (Exception ex)
                {
                    _fileService.Log($"Edge-TTS WebSocket (lần {attempt}/2, chunk {chunkText.Length} ký tự) cảnh báo: {ex.Message}", "WARN");
                    if (attempt < 2)
                    {
                        await Task.Delay(300, cancellationToken);
                    }
                }
            }

            // Ưu tiên 4: WebSocket với SSML Cơ bản
            try
            {
                _fileService.Log("Thử lại Edge-TTS WebSocket với SSML cơ bản...", "INFO");
                return await GenerateEdgeTtsViaWebSocketAsync(chunkText, voice, speed, pitch, emotionStyle, chunkOutputPath, cancellationToken, useBasicSsml: true);
            }
            catch (Exception ex)
            {
                _fileService.Log($"Edge-TTS WebSocket cơ bản cảnh báo: {ex.Message}", "WARN");
            }

            throw new InvalidOperationException($"Không thể tạo âm thanh Edge-TTS cho đoạn văn bản ({chunkText.Length} ký tự).");
        }

        public static List<string> SplitTextIntoOptimalChunks(string text, int maxChunkLength = 550)
        {
            var chunks = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return chunks;

            if (text.Length <= maxChunkLength)
            {
                chunks.Add(text.Trim());
                return chunks;
            }

            var sentences = Regex.Split(text, @"(?<=[.!?\n])\s+")
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            if (sentences.Count <= 1)
            {
                var clauses = Regex.Split(text, @"(?<=[,;])\s+")
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();

                if (clauses.Count <= 1)
                {
                    chunks.Add(text.Trim());
                    return chunks;
                }

                sentences = clauses;
            }

            var currentChunk = new StringBuilder();
            foreach (var s in sentences)
            {
                if (currentChunk.Length + s.Length + 1 > maxChunkLength && currentChunk.Length > 0)
                {
                    chunks.Add(currentChunk.ToString().Trim());
                    currentChunk.Clear();
                }

                if (currentChunk.Length > 0) currentChunk.Append(" ");
                currentChunk.Append(s);
            }

            if (currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString().Trim());
            }

            return chunks;
        }

        private async Task ConcatenateMp3FilesAsync(List<string> audioFiles, string outputPath, CancellationToken cancellationToken)
        {
            if (audioFiles.Count == 0) return;
            if (audioFiles.Count == 1)
            {
                File.Copy(audioFiles[0], outputPath, true);
                return;
            }

            // Cách 1: Sử dụng FFmpeg concat demuxer nếu có sẵn
            string ffmpegExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "ffmpeg", "ffmpeg.exe");
            if (File.Exists(ffmpegExe))
            {
                string listFile = Path.Combine(Path.GetDirectoryName(audioFiles[0])!, $"concat_{Guid.NewGuid():N}.txt");
                try
                {
                    var fileLines = audioFiles.Select(p => $"file '{p.Replace("\\", "/")}'");
                    await File.WriteAllLinesAsync(listFile, fileLines, Encoding.UTF8, cancellationToken);

                    var psi = new ProcessStartInfo
                    {
                        FileName = ffmpegExe,
                        Arguments = $"-y -f concat -safe 0 -i \"{listFile}\" -c copy \"{outputPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true
                    };

                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        await proc.WaitForExitAsync(cancellationToken);
                        if (proc.ExitCode == 0 && File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
                        {
                            return;
                        }
                    }
                }
                catch { }
                finally
                {
                    try { if (File.Exists(listFile)) File.Delete(listFile); } catch { }
                }
            }

            // Cách 2: Ghép nhị phân stream trực tiếp (Edge-TTS sinh MP3 CBR 48kbps 24kHz mono hoàn toàn tương thích)
            await using var outStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            foreach (var file in audioFiles)
            {
                await using var inStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                await inStream.CopyToAsync(outStream, cancellationToken);
            }
            await outStream.FlushAsync(cancellationToken);
        }

        public static string BuildExpressiveSsml(
            string text,
            string voice,
            double speed,
            double pitchPercent = 2.0,
            string emotionStyle = "Dynamic")
        {
            string lang = voice.StartsWith("vi-", StringComparison.OrdinalIgnoreCase) ? "vi-VN" : "en-US";
            double effectiveSpeed = speed <= 0 ? 1.20 : speed;

            var rawSentences = Regex.Split(text, @"(?<=[.!?\n])\s+")
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            if (rawSentences.Count == 0)
            {
                rawSentences.Add(text.Trim());
            }

            var sb = new StringBuilder();
            sb.Append($"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xmlns:mstts='http://www.w3.org/2001/mstts' xml:lang='{lang}'>");
            sb.Append($"<voice name='{voice}'>");

            for (int i = 0; i < rawSentences.Count; i++)
            {
                string sentence = rawSentences[i];
                if (string.IsNullOrWhiteSpace(sentence)) continue;

                bool isHook = (i == 0);
                bool isQuestion = sentence.EndsWith("?") || Regex.IsMatch(sentence, @"\b(sao|gì|hả|đâu|phải không|thế)\b", RegexOptions.IgnoreCase);
                bool isExclamation = sentence.EndsWith("!") || Regex.IsMatch(sentence, @"\b(ủa|ủa alo|trời ơi|cứu tui|bất lực|quỳ|nhìn kìa|chết cười|mắc cười|đỉnh nóc|hết cứu|sợ thật)\b", RegexOptions.IgnoreCase);
                bool isSuspense = Regex.IsMatch(sentence, @"\b(ban đầu|ai ngờ|hóa ra|tôi thề|lạ gì|thế nhưng|ai dè|cứ ngỡ)\b", RegexOptions.IgnoreCase);

                double sentencePitch = pitchPercent;
                double sentenceRate = effectiveSpeed;

                if (emotionStyle == "Cheerful" || (emotionStyle != "Calm" && emotionStyle != "Standard" && (isExclamation || isHook)))
                {
                    sentencePitch += 4.0; // Tăng cao độ cho câu hook / cảm thán sôi nổi
                    sentenceRate *= 1.02;
                }
                else if (isQuestion)
                {
                    sentencePitch += 3.5; // Lên giọng câu hỏi tự nhiên
                }
                else if (isSuspense || emotionStyle == "Calm")
                {
                    sentencePitch -= 2.0; // Hạ nhẹ cao độ cho câu bí ẩn / trầm lắng
                    sentenceRate *= 0.98;
                }
                else if (emotionStyle != "Standard")
                {
                    // Nhịp điệu luân phiên nhẹ giữa các câu kể tạo speech melody
                    sentencePitch += (i % 2 == 1) ? 1.5 : 0.0;
                }

                string rateStr = FormatRateForSsml(sentenceRate);
                string pitchStr = FormatPitchForSsml(sentencePitch);

                string escapedSentence = EscapeXml(sentence);

                sb.Append($"<prosody rate='{rateStr}' pitch='{pitchStr}'>{escapedSentence}</prosody>");

                // Chèn khoảng nghỉ lấy hơi gọn gàng giữa các câu
                if (i < rawSentences.Count - 1)
                {
                    int breakMs = isSuspense ? 120 : (isExclamation ? 70 : 90);
                    if (emotionStyle == "FastPaced") breakMs = 50;
                    sb.Append($"<break time='{breakMs}ms'/>");
                }
            }

            sb.Append("</voice></speak>");
            return sb.ToString();
        }

        private static string EscapeXml(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }

        private static string FormatPitchForSsml(double pitchPercent)
        {
            if (Math.Abs(pitchPercent) < 0.2) return "+0Hz";
            return pitchPercent >= 0 ? $"+{pitchPercent:0}%" : $"{pitchPercent:0}%";
        }

        public static string BuildBasicSsml(
            string text,
            string voice,
            double speed,
            double pitchPercent = 2.0)
        {
            string lang = voice.StartsWith("vi-", StringComparison.OrdinalIgnoreCase) ? "vi-VN" : "en-US";
            string rateStr = FormatRateForSsml(speed);
            string pitchStr = FormatPitchForSsml(pitchPercent);
            string escapedText = EscapeXml(text);

            return $"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xmlns:mstts='http://www.w3.org/2001/mstts' xml:lang='{lang}'>" +
                   $"<voice name='{voice}'>" +
                   $"<prosody rate='{rateStr}' pitch='{pitchStr}'>{escapedText}</prosody>" +
                   $"</voice></speak>";
        }

        private async Task<TtsResult> GenerateEdgeTtsViaWebSocketAsync(
            string text,
            string voice,
            double speed,
            double pitch,
            string emotionStyle,
            string outputPath,
            CancellationToken cancellationToken,
            bool useBasicSsml = false)
        {
            var wordTimestamps = new List<WordTimestamp>();
            string ssml = useBasicSsml
                ? BuildBasicSsml(text, voice, speed, pitch)
                : BuildExpressiveSsml(text, voice, speed, pitch, emotionStyle);

            string requestId = Guid.NewGuid().ToString("N");
            string timestamp = DateTime.UtcNow.ToString("ddd MMM dd yyyy HH:mm:ss 'GMT+0000 (Coordinated Universal Time)'", CultureInfo.InvariantCulture);

            string secMsGec = GenerateSecMsGec();
            string muid = GenerateMuid();

            string wssUrl = "wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1" +
                            $"?TrustedClientToken={EdgeTrustedClientToken}" +
                            $"&ConnectionId={requestId}" +
                            $"&Sec-MS-GEC={secMsGec}" +
                            $"&Sec-MS-GEC-Version={SecMsGecVersion}";

            using var client = new ClientWebSocket();
            client.Options.SetRequestHeader("Pragma", "no-cache");
            client.Options.SetRequestHeader("Cache-Control", "no-cache");
            client.Options.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36 Edg/143.0.0.0");
            client.Options.SetRequestHeader("Origin", "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold");
            client.Options.SetRequestHeader("Accept-Encoding", "gzip, deflate, br, zstd");
            client.Options.SetRequestHeader("Accept-Language", "vi-VN,vi;q=0.9,en-US;q=0.8");
            client.Options.SetRequestHeader("Cookie", $"muid={muid};");

            await client.ConnectAsync(new Uri(wssUrl), cancellationToken);

            string configMsg = $"X-Timestamp:{timestamp}\r\n" +
                               "Content-Type:application/json; charset=utf-8\r\n" +
                               "Path:speech.config\r\n\r\n" +
                               "{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":{\"sentenceBoundaryEnabled\":\"false\",\"wordBoundaryEnabled\":\"true\"},\"outputFormat\":\"audio-24khz-48kbitrate-mono-mp3\"}}}}\r\n";

            byte[] configBytes = Encoding.UTF8.GetBytes(configMsg);
            await client.SendAsync(new ArraySegment<byte>(configBytes), WebSocketMessageType.Text, true, cancellationToken);

            string ssmlMsg = $"X-RequestId:{requestId}\r\n" +
                             "Content-Type:application/ssml+xml\r\n" +
                             $"X-Timestamp:{timestamp}Z\r\n" +
                             "Path:ssml\r\n\r\n" +
                             ssml;

            byte[] ssmlBytes = Encoding.UTF8.GetBytes(ssmlMsg);
            await client.SendAsync(new ArraySegment<byte>(ssmlBytes), WebSocketMessageType.Text, true, cancellationToken);

            await using var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            var buffer = new byte[8192];
            var memStream = new MemoryStream();
            bool turnEndReceived = false;
            long audioBytesReceived = 0;

            try
            {
                while (client.State == WebSocketState.Open)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    memStream.Write(buffer, 0, result.Count);

                    if (result.EndOfMessage)
                    {
                        byte[] msgBytes = memStream.ToArray();
                        memStream.SetLength(0);

                        string headerText = Encoding.ASCII.GetString(msgBytes, 0, Math.Min(msgBytes.Length, 300));
                        if (headerText.Contains("Path:turn.end"))
                        {
                            turnEndReceived = true;
                            break;
                        }

                        if (headerText.Contains("Path:audio.metadata"))
                        {
                            ParseWordBoundaryMetadata(msgBytes, wordTimestamps);
                        }
                        else if (headerText.Contains("Path:audio"))
                        {
                            if (msgBytes.Length > 2)
                            {
                                int headerLen = (msgBytes[0] << 8) | msgBytes[1];
                                int audioStart = headerLen + 2;
                                if (audioStart < msgBytes.Length)
                                {
                                    int audioLen = msgBytes.Length - audioStart;
                                    await outputStream.WriteAsync(msgBytes.AsMemory(audioStart, audioLen), cancellationToken);
                                    audioBytesReceived += audioLen;
                                }
                            }
                        }
                    }
                }
            }
            catch (WebSocketException wsex)
            {
                // Nếu đã nhận được turn.end hoặc đã có đầy đủ dung lượng âm thanh, việc server ngắt TCP sớm là bình thường
                if (turnEndReceived || audioBytesReceived > 4000)
                {
                    _fileService.Log($"WebSocket kết thúc nhẹ ({wsex.Message}) khi đã nhận {audioBytesReceived} bytes âm thanh.", "INFO");
                }
                else
                {
                    throw;
                }
            }

            try
            {
                if (client.State == WebSocketState.Open || client.State == WebSocketState.CloseReceived)
                {
                    await client.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);
                }
            }
            catch { }

            await outputStream.FlushAsync(cancellationToken);

            long fileLength = new FileInfo(outputPath).Length;
            if (fileLength == 0)
            {
                throw new InvalidOperationException("Edge-TTS không nhận được dữ liệu âm thanh.");
            }

            // Với audio-24khz-48kbitrate-mono-mp3: 48,000 bits/s = 6,000 bytes/s
            double exactDuration = Math.Round(fileLength / 6000.0, 2);

            _fileService.Log($"Tạo audio Edge-TTS thành công: {outputPath} ({fileLength} bytes, {exactDuration:F1}s, {wordTimestamps.Count} từ)");
            return new TtsResult
            {
                AudioPath = outputPath,
                DurationSeconds = exactDuration,
                WordTimestamps = wordTimestamps
            };
        }

        private static void ParseWordBoundaryMetadata(byte[] msgBytes, List<WordTimestamp> wordTimestamps)
        {
            try
            {
                string fullMsg = Encoding.UTF8.GetString(msgBytes);
                int bodyIdx = fullMsg.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (bodyIdx < 0) return;

                string json = fullMsg.Substring(bodyIdx + 4);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("Metadata", out var metaArr)) return;

                foreach (var item in metaArr.EnumerateArray())
                {
                    if (item.TryGetProperty("Type", out var typeProp) && typeProp.GetString() == "WordBoundary")
                    {
                        if (item.TryGetProperty("Data", out var dataProp))
                        {
                            long offset = dataProp.GetProperty("Offset").GetInt64();
                            long duration = dataProp.GetProperty("Duration").GetInt64();
                            string word = "";
                            if (dataProp.TryGetProperty("text", out var textObj) && textObj.TryGetProperty("Text", out var wordVal))
                            {
                                word = wordVal.GetString() ?? "";
                            }
                            if (!string.IsNullOrWhiteSpace(word))
                            {
                                wordTimestamps.Add(new WordTimestamp
                                {
                                    Word = word,
                                    Start = Math.Round(offset / 10000000.0, 3),
                                    End = Math.Round((offset + duration) / 10000000.0, 3)
                                });
                            }
                        }
                    }
                }
            }
            catch { }
        }

        public static List<WordTimestamp> EstimateWordTimestamps(string text, double totalAudioDuration)
        {
            var list = new List<WordTimestamp>();
            if (string.IsNullOrWhiteSpace(text) || totalAudioDuration <= 0) return list;

            string clean = StripTimestampsAndFormatText(text);
            var words = clean.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return list;

            int totalChars = 0;
            foreach (var w in words) totalChars += Math.Max(1, w.Length);

            double currentTime = 0.0;
            for (int i = 0; i < words.Length; i++)
            {
                string w = words[i];
                double proportion = (double)Math.Max(1, w.Length) / totalChars;
                double dur = Math.Max(0.12, proportion * totalAudioDuration);
                double end = (i == words.Length - 1) ? totalAudioDuration : Math.Min(totalAudioDuration, currentTime + dur);

                list.Add(new WordTimestamp
                {
                    Word = w,
                    Start = Math.Round(currentTime, 3),
                    End = Math.Round(end, 3)
                });
                currentTime = end;
            }

            return list;
        }

        private async Task<TtsResult?> TryGenerateWithEdgeTtsWorkerAsync(
            string text,
            string voice,
            double speed,
            double pitch,
            string outputPath,
            CancellationToken cancellationToken)
        {
            string pythonExe = FindPythonExecutable();
            if (string.IsNullOrEmpty(pythonExe)) return null;

            string[] candidateScripts = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Assets", "scripts", "edge_tts_worker.py"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "scripts", "edge_tts_worker.py"),
                Path.Combine(Directory.GetCurrentDirectory(), "VideoNarrator", "Assets", "scripts", "edge_tts_worker.py"),
                Path.Combine(Directory.GetCurrentDirectory(), "Assets", "scripts", "edge_tts_worker.py"),
                @"D:\code\tao video\VideoNarrator\Assets\scripts\edge_tts_worker.py"
            };

            string workerScript = candidateScripts.FirstOrDefault(File.Exists) ?? string.Empty;
            if (string.IsNullOrEmpty(workerScript))
            {
                return null;
            }

            string tempTextFile = Path.Combine(Path.GetTempPath(), $"tts_in_{Guid.NewGuid():N}.txt");
            string tempJsonFile = Path.Combine(Path.GetTempPath(), $"tts_words_{Guid.NewGuid():N}.json");

            try
            {
                await File.WriteAllTextAsync(tempTextFile, text, Encoding.UTF8, cancellationToken);
                string rateStr = FormatRateForCli(speed);
                string pitchStr = FormatPitchForCli(pitch);

                var psi = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"\"{workerScript}\" --text-file \"{tempTextFile}\" --voice \"{voice}\" --rate \"{rateStr}\" --pitch \"{pitchStr}\" --write-media \"{outputPath}\" --write-words \"{tempJsonFile}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return null;

                var stderrTask = proc.StandardError.ReadToEndAsync(cancellationToken);
                var stdoutTask = proc.StandardOutput.ReadToEndAsync(cancellationToken);

                await proc.WaitForExitAsync(cancellationToken);
                string stderr = await stderrTask;
                string stdout = await stdoutTask;

                if (proc.ExitCode == 0 && File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
                {
                    double dur = 0;
                    try
                    {
                        dur = new FileInfo(outputPath).Length / 6000.0;
                    }
                    catch { }

                    var words = new List<WordTimestamp>();
                    if (File.Exists(tempJsonFile))
                    {
                        string json = await File.ReadAllTextAsync(tempJsonFile, cancellationToken);
                        var parsed = JsonSerializer.Deserialize<List<WordTimestamp>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (parsed != null && parsed.Count > 0)
                        {
                            words = parsed;
                            if (words.Count > 0 && words[^1].End > 0)
                            {
                                dur = words[^1].End + 0.15;
                            }
                        }
                    }

                    if (words.Count == 0)
                    {
                        words = EstimateWordTimestamps(text, dur);
                    }

                    _fileService.Log($"Tạo audio Edge-TTS bằng Python Worker thành công: {outputPath} ({dur:F1}s, {words.Count} từ)");
                    return new TtsResult
                    {
                        AudioPath = outputPath,
                        DurationSeconds = Math.Round(dur, 2),
                        WordTimestamps = words
                    };
                }

                _fileService.Log($"Edge-TTS Python Worker thông báo (ExitCode {proc.ExitCode}): {stderr} {stdout}", "WARN");
                return null;
            }
            catch (Exception ex)
            {
                _fileService.Log($"Lỗi chạy Edge-TTS Python Worker: {ex.Message}", "WARN", ex);
                return null;
            }
            finally
            {
                try { if (File.Exists(tempTextFile)) File.Delete(tempTextFile); } catch { }
                try { if (File.Exists(tempJsonFile)) File.Delete(tempJsonFile); } catch { }
            }
        }

        private async Task<TtsResult?> TryGenerateWithEdgeTtsCliAsync(string text, string voice, double speed, double pitch, string outputPath, CancellationToken cancellationToken)
        {
            string tempTextFile = Path.Combine(Path.GetTempPath(), $"tts_{Guid.NewGuid():N}.txt");
            string tempVttFile = Path.Combine(Path.GetTempPath(), $"tts_{Guid.NewGuid():N}.vtt");
            try
            {
                await File.WriteAllTextAsync(tempTextFile, text, Encoding.UTF8, cancellationToken);
                string rateStr = FormatRateForCli(speed);
                string pitchStr = FormatPitchForCli(pitch);
                string pythonExe = FindPythonExecutable();

                var psi = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"-m edge_tts --voice \"{voice}\" --rate \"{rateStr}\" --pitch \"{pitchStr}\" -f \"{tempTextFile}\" --write-media \"{outputPath}\" --write-subtitles \"{tempVttFile}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return null;

                var stderrTask = proc.StandardError.ReadToEndAsync(cancellationToken);
                var stdoutTask = proc.StandardOutput.ReadToEndAsync(cancellationToken);

                await proc.WaitForExitAsync(cancellationToken);
                string stderr = await stderrTask;
                string stdout = await stdoutTask;

                if (proc.ExitCode == 0 && File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
                {
                    double dur = new FileInfo(outputPath).Length / 6000.0;
                    var words = ParseVttTimestamps(tempVttFile);
                    if (words.Count == 0)
                    {
                        words = EstimateWordTimestamps(text, dur);
                    }

                    _fileService.Log($"Tạo audio Edge-TTS CLI fallback thành công: {outputPath} ({dur:F1}s, {words.Count} từ)");
                    return new TtsResult
                    {
                        AudioPath = outputPath,
                        DurationSeconds = dur,
                        WordTimestamps = words
                    };
                }

                _fileService.Log($"Edge-TTS CLI thất bại (ExitCode {proc.ExitCode}): {stderr} {stdout}", "WARN");
                return null;
            }
            catch (Exception ex)
            {
                _fileService.Log($"Lỗi chạy edge-tts CLI fallback: {ex.Message}", "WARN", ex);
                return null;
            }
            finally
            {
                try { if (File.Exists(tempTextFile)) File.Delete(tempTextFile); } catch { }
                try { if (File.Exists(tempVttFile)) File.Delete(tempVttFile); } catch { }
            }
        }

        private static List<WordTimestamp> ParseVttTimestamps(string vttPath)
        {
            var list = new List<WordTimestamp>();
            if (!File.Exists(vttPath)) return list;

            try
            {
                var lines = File.ReadAllLines(vttPath, Encoding.UTF8);
                var regex = new Regex(@"(\d{2}):(\d{2}):(\d{2})[.,](\d{3})\s*-->\s*(\d{2}):(\d{2}):(\d{2})[.,](\d{3})");

                for (int i = 0; i < lines.Length; i++)
                {
                    var m = regex.Match(lines[i]);
                    if (m.Success && i + 1 < lines.Length)
                    {
                        double start = int.Parse(m.Groups[1].Value) * 3600 +
                                       int.Parse(m.Groups[2].Value) * 60 +
                                       int.Parse(m.Groups[3].Value) +
                                       int.Parse(m.Groups[4].Value) / 1000.0;

                        double end = int.Parse(m.Groups[5].Value) * 3600 +
                                     int.Parse(m.Groups[6].Value) * 60 +
                                     int.Parse(m.Groups[7].Value) +
                                     int.Parse(m.Groups[8].Value) / 1000.0;

                        string cueText = lines[i + 1].Trim();
                        if (!string.IsNullOrEmpty(cueText) && !cueText.StartsWith("WEBVTT"))
                        {
                            var words = cueText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            double dur = Math.Max(0.2, end - start);
                            double perWord = dur / Math.Max(1, words.Length);
                            for (int w = 0; w < words.Length; w++)
                            {
                                double ws = start + (w * perWord);
                                double we = (w == words.Length - 1) ? end : ws + perWord;
                                list.Add(new WordTimestamp
                                {
                                    Word = words[w],
                                    Start = Math.Round(ws, 3),
                                    End = Math.Round(we, 3)
                                });
                            }
                        }
                    }
                }
            }
            catch { }

            return list;
        }

        private Task<TtsResult> GenerateSystemSpeechAsync(string text, string voice, string outputPath, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool isVietnameseVoice = voice.StartsWith("vi-", StringComparison.OrdinalIgnoreCase);
                string wavPath = Path.ChangeExtension(outputPath, ".wav");

                using (var synth = new SpeechSynthesizer())
                {
                    var viVoice = synth.GetInstalledVoices()
                        .FirstOrDefault(v => v.Enabled && v.VoiceInfo.Culture.TwoLetterISOLanguageName.Equals("vi", StringComparison.OrdinalIgnoreCase));

                    if (viVoice != null)
                    {
                        synth.SelectVoice(viVoice.VoiceInfo.Name);
                    }
                    else if (isVietnameseVoice)
                    {
                        // TUYỆT ĐỐI CHẶN: Không dùng giọng Microsoft David / Zira (tiếng Anh) để đọc tiếng Việt!
                        // Tránh triệt để lỗi người dùng phản ánh: "lâu lâu cái giọng nói bị lỗi nói tiếng anh"
                        throw new InvalidOperationException("Máy chủ Edge-TTS tạm thời không khả dụng và Windows chưa cài đặt giọng đọc tiếng Việt offline. Đã dừng phát giọng tiếng Anh để bảo đảm chất lượng video.");
                    }
                    else
                    {
                        var anyVoice = synth.GetInstalledVoices().FirstOrDefault(v => v.Enabled);
                        if (anyVoice != null) synth.SelectVoice(anyVoice.VoiceInfo.Name);
                    }

                    synth.SetOutputToWaveFile(wavPath);
                    synth.Speak(text);
                    synth.SetOutputToNull();
                }

                if (outputPath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                {
                    string ffmpegExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "ffmpeg", "ffmpeg.exe");
                    if (File.Exists(ffmpegExe))
                    {
                        try
                        {
                            var psi = new ProcessStartInfo
                            {
                                FileName = ffmpegExe,
                                Arguments = $"-y -i \"{wavPath}\" -c:a libmp3lame -q:a 2 \"{outputPath}\"",
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };
                            using var proc = Process.Start(psi);
                            proc?.WaitForExit(10000);
                        }
                        catch { }
                    }

                    if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
                    {
                        File.Copy(wavPath, outputPath, true);
                    }

                    if (File.Exists(wavPath) && wavPath != outputPath)
                    {
                        try { File.Delete(wavPath); } catch { }
                    }
                }
                else
                {
                    if (wavPath != outputPath && File.Exists(wavPath))
                    {
                        File.Move(wavPath, outputPath, true);
                    }
                }

                double estimatedDur = Math.Max(2.0, text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length * 0.32);
                _fileService.Log($"Tạo audio System.Speech offline hoàn tất: {outputPath} (~{estimatedDur:F1}s)");

                return new TtsResult
                {
                    AudioPath = outputPath,
                    DurationSeconds = estimatedDur,
                    WordTimestamps = EstimateWordTimestamps(text, estimatedDur)
                };
            }, cancellationToken);
        }

        private static string FindPythonExecutable()
        {
            string[] candidatePaths =
            {
                @"C:\Users\nguye\AppData\Local\Programs\Python\Python312\python.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python\Python312\python.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python\Python313\python.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python\Python314\python.exe"),
                @"C:\Python312\python.exe",
                @"C:\Python311\python.exe",
                @"C:\Program Files\Python312\python.exe"
            };

            foreach (var path in candidatePaths)
            {
                if (File.Exists(path)) return path;
            }

            string? envPath = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(envPath))
            {
                foreach (var dir in envPath.Split(Path.PathSeparator))
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(dir)) continue;
                        string full = Path.Combine(dir.Trim(), "python.exe");
                        if (File.Exists(full) && !full.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase))
                        {
                            return full;
                        }
                    }
                    catch { }
                }
            }

            return "python";
        }

        private static string ExtractVoiceCode(string voiceDisplay, string fallback)
        {
            if (string.IsNullOrWhiteSpace(voiceDisplay)) return fallback;
            int spaceIdx = voiceDisplay.IndexOf(' ');
            if (spaceIdx > 0)
            {
                return voiceDisplay.Substring(0, spaceIdx).Trim();
            }
            return voiceDisplay.Trim();
        }

        private static string FormatRateForSsml(double speed)
        {
            double effectiveSpeed = speed <= 0 ? 1.0 : speed;
            double percent = (effectiveSpeed - 1.0) * 100.0;
            if (Math.Abs(percent) < 0.1) return "+0%";
            return percent >= 0 ? $"+{percent:0}%" : $"{percent:0}%";
        }

        private static string FormatRateForCli(double speed)
        {
            double effectiveSpeed = speed <= 0 ? 1.0 : speed;
            double percent = (effectiveSpeed - 1.0) * 100.0;
            if (Math.Abs(percent) < 0.1) return "+0%";
            return percent >= 0 ? $"+{percent:0}%" : $"{percent:0}%";
        }

        private static string FormatPitchForCli(double pitchPercent)
        {
            if (Math.Abs(pitchPercent) < 0.2) return "+0Hz";
            int hz = (int)Math.Round(pitchPercent * 1.5);
            return hz >= 0 ? $"+{hz}Hz" : $"{hz}Hz";
        }
    }
}

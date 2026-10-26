using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VideoNarrator.Helpers;
using VideoNarrator.Models;
using VideoNarrator.Services.PersonalityEngine;

namespace VideoNarrator.Services
{
    public interface IEditingPlanService
    {
        Task<EditingPlan> GenerateEditingPlanAsync(
            string apiKey,
            string model,
            string videoFilePath,
            double videoDurationSeconds,
            string stylePreset,
            List<VideoScene>? detectedScenes,
            AppSettings settings,
            string? customScript = null,
            string? customTopic = null,
            Action<string, double>? progressCallback = null,
            CancellationToken cancellationToken = default);

        void ValidateAndSanitizePlan(EditingPlan plan, double sourceDurationSeconds);
    }

    public class EditingPlanService : IEditingPlanService
    {
        private readonly HttpClient _httpClient;
        private readonly IFileService _fileService;
        private readonly IGeminiService _geminiService;
        private readonly IPersonaManager? _personaManager;
        private readonly ISystemPromptBuilder? _promptBuilder;
        private readonly IFewShotManager? _fewShotManager;
        private readonly IVideoProbeService? _videoProbeService;
        private readonly IMultiStageNarrationEngine? _narrationEngine;
        private readonly IStyleEngine? _styleEngine;
        private const string BaseApiUrl = "https://generativelanguage.googleapis.com";

        public EditingPlanService(
            HttpClient httpClient,
            IFileService fileService,
            IGeminiService geminiService,
            IPersonaManager? personaManager = null,
            ISystemPromptBuilder? promptBuilder = null,
            IFewShotManager? fewShotManager = null,
            IVideoProbeService? videoProbeService = null,
            IMultiStageNarrationEngine? narrationEngine = null,
            IStyleEngine? styleEngine = null)
        {
            _httpClient = httpClient;
            _fileService = fileService;
            _geminiService = geminiService;
            _personaManager = personaManager;
            _promptBuilder = promptBuilder;
            _fewShotManager = fewShotManager;
            _videoProbeService = videoProbeService;
            _narrationEngine = narrationEngine;
            _styleEngine = styleEngine;
        }

        public static bool IsGenericDownloaderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            string clean = name.Trim();

            // Download, Download (25), Downloads, Tải xuống
            if (Regex.IsMatch(clean, @"^(download|downloads|tai_xuong|tải xuống)(\s*\(\d+\))?$", RegexOptions.IgnoreCase))
                return true;

            // Camera / Phone / Default video names: VID_20241004, VIDEO_001, MOV_123, IMG_123, CAM_123, CLIP_123, SCREEN_123
            if (Regex.IsMatch(clean, @"^(VID|VIDEO|MOV|IMG|CAM|CLIP|REC|SCREEN|AUD|PXL|FILE)[_\-\s0-9]+$", RegexOptions.IgnoreCase))
                return true;

            // Snaptik / TikTok / Douyin numeric IDs: snaptik.vn_7675720859902070037, tiktok_..., y2mate_...
            if (Regex.IsMatch(clean, @"^(snaptik|tiktok|douyin|y2mate|ssstik|savefrom|yt1s|facebook|fb)[_\.\-\s0-9a-zA-Z]*$", RegexOptions.IgnoreCase)
                && Regex.IsMatch(clean, @"\d{6,}"))
                return true;

            // Pure numbers or short hashes
            if (Regex.IsMatch(clean, @"^\d+$") || Regex.IsMatch(clean, @"^[0-9a-fA-F_\-]{8,}$"))
                return true;

            if (clean.Equals("video", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("clip", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("output", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("test", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("trim", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("new", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        public static string ExtractTopicFromFileName(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return string.Empty;
            string name = Path.GetFileNameWithoutExtension(filePath);

            if (IsGenericDownloaderName(name)) return string.Empty;

            // Strip prefix from downloader tools
            name = Regex.Replace(name, @"^(YTSave|YouTube|Shorts|TikTok|Downloader|Video|SnapTik|Y2mate)[_A-Za-z0-9]*_", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"_\d{3,4}p$", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\(\d+\)$", "", RegexOptions.IgnoreCase);
            name = name.Replace('-', ' ').Replace('_', ' ').Trim();

            if (IsGenericDownloaderName(name)) return string.Empty;

            return name;
        }

        public async Task<EditingPlan> GenerateEditingPlanAsync(
            string apiKey,
            string model,
            string videoFilePath,
            double videoDurationSeconds,
            string stylePreset,
            List<VideoScene>? detectedScenes,
            AppSettings settings,
            string? customScript = null,
            string? customTopic = null,
            Action<string, double>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            string topic = !string.IsNullOrWhiteSpace(customTopic)
                ? customTopic.Trim()
                : ExtractTopicFromFileName(videoFilePath);

            string logTopic = !string.IsNullOrWhiteSpace(topic) ? topic : "Tự động nhận diện từ video";

            string apiUrl = !string.IsNullOrWhiteSpace(settings?.GeminiApiUrl) ? settings.GeminiApiUrl.Trim() : BaseApiUrl;
            bool isCustomApi = !apiUrl.Contains("googleapis.com", StringComparison.OrdinalIgnoreCase);

            if (!isCustomApi && string.IsNullOrWhiteSpace(apiKey))
            {
                if (!string.IsNullOrWhiteSpace(customScript))
                {
                    _fileService.Log($"Chưa cấu hình Gemini API Key: Tiến hành dựng video dựa trên kịch bản người dùng đã nhập...", "INFO");
                    progressCallback?.Invoke($"Đang lập kế hoạch dựng 9:16 theo kịch bản của bạn...", 40);
                    var offlinePlan = GenerateSmartOfflineEditingPlan(videoDurationSeconds, stylePreset, detectedScenes, settings, topic, customScript);
                    ParseResolution(settings.OutputResolution, offlinePlan);
                    offlinePlan.StylePreset = stylePreset;
                    ValidateAndSanitizePlan(offlinePlan, videoDurationSeconds);
                    progressCallback?.Invoke("Kế hoạch dựng 9:16 hoàn tất!", 85);
                    return offlinePlan;
                }

                throw new InvalidOperationException("Chưa cấu hình Gemini API Key. Vui lòng mở Cài Đặt (biểu tượng bánh răng) và nhập Gemini API Key để AI phân tích video.");
            }

            VideoProbeResult? probeResult = null;
            if (_videoProbeService != null && File.Exists(videoFilePath))
            {
                try
                {
                    progressCallback?.Invoke("AI đang lắng nghe âm thanh và quan sát bối cảnh video...", 20);
                    probeResult = await _videoProbeService.ProbeVideoAsync(videoFilePath, cancellationToken);
                    if (probeResult != null && probeResult.Success)
                    {
                        if (string.IsNullOrWhiteSpace(topic) && !string.IsNullOrWhiteSpace(probeResult.SuggestedTopic))
                        {
                            topic = probeResult.SuggestedTopic;
                            logTopic = topic;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _fileService.Log($"Thăm dò bối cảnh video gặp lỗi: {ex.Message}", "WARN");
                }
            }

            string targetModel = string.IsNullOrWhiteSpace(model) ? (isCustomApi ? "gemini-3.1-flash-lite" : "gemini-3.5-flash") : model;
            _fileService.Log($"Tạo AI Auto Editing Plan: Chủ đề '{logTopic}', Model '{targetModel}', Duration {videoDurationSeconds:F1}s (API: {(isCustomApi ? apiUrl : "Google Cloud")})");

            // Attempt to upload video file to Gemini File API so Gemini can watch the actual video (chỉ khả dụng trên Google Cloud)
            string? fileUri = null;
            if (!isCustomApi && File.Exists(videoFilePath))
            {
                try
                {
                    progressCallback?.Invoke("Đang tải video lên Gemini để AI xem trực tiếp và phân tích...", 20);
                    string mimeType = _geminiService.GetMimeType(videoFilePath);
                    var (uploadedUri, remoteFileName) = await _geminiService.UploadVideoFileAsync(apiKey, videoFilePath, mimeType, cancellationToken);
                    await _geminiService.WaitForFileActiveStateAsync(apiKey, remoteFileName, progressCallback, cancellationToken);
                    fileUri = uploadedUri;
                    _fileService.Log($"Video tải lên Gemini File API thành công: {fileUri}");
                }
                catch (Exception ex)
                {
                    _fileService.Log($"Không thể tải video trực tiếp lên Gemini ({ex.Message}). Tiếp tục phân tích theo chủ đề bám sát...", "WARN");
                }
            }

            // GIAI ĐOẠN MULTI-STAGE NARRATION ENGINE (NÂNG CẤP KỂ CHUYỆN ĐA GIAI ĐOẠN A -> B -> C)
            if (string.IsNullOrWhiteSpace(customScript) && _narrationEngine != null && (!string.IsNullOrWhiteSpace(apiKey) || isCustomApi))
            {
                try
                {
                    progressCallback?.Invoke("AI đang chạy Multi-Stage Narration Engine (Phân tích bối cảnh & Tạo lời thoại hấp dẫn)...", 30);
                    var persona = _personaManager?.GetPersonaById(stylePreset) ?? _personaManager?.GetPersonaById("curiosity") ?? new PersonaProfile();
                    var personaConfig = new NarrationPersonaConfig { TargetDurationSeconds = videoDurationSeconds };
                    if (_styleEngine != null)
                    {
                        var (minW, maxW, _) = _styleEngine.CalculateDurationBudget(videoDurationSeconds, settings?.SpeechRate ?? 1.20);
                        personaConfig.MinWords = minW;
                        personaConfig.MaxWords = maxW;
                    }

                    var (generatedScript, analysis, evaluation) = await _narrationEngine.RunFullNarrationPipelineAsync(
                        apiKey,
                        targetModel,
                        fileUri,
                        !string.IsNullOrEmpty(fileUri) ? _geminiService.GetMimeType(videoFilePath) : null,
                        videoFilePath,
                        videoDurationSeconds,
                        personaConfig,
                        persona,
                        probeResult,
                        topic,
                        isCustomApi ? apiUrl : null,
                        progressCallback,
                        cancellationToken);

                    if (!string.IsNullOrWhiteSpace(generatedScript))
                    {
                        customScript = generatedScript;
                        _fileService.Log($"[Multi-Stage Engine] Sinh kịch bản thành công: {MultiStageNarrationEngine.CountWords(customScript)} từ | Đánh giá Stage C: {evaluation.AverageScore:F1}/10 (Passed: {evaluation.Passed})");

                        if (string.IsNullOrWhiteSpace(topic) && !string.IsNullOrWhiteSpace(analysis.VideoTopic))
                        {
                            topic = analysis.VideoTopic;
                        }

                        if ((detectedScenes == null || detectedScenes.Count <= 1) && analysis.SceneTimeline != null && analysis.SceneTimeline.Count > 1)
                        {
                            detectedScenes = analysis.SceneTimeline.Select(st => new VideoScene
                            {
                                Start = st.StartTime,
                                End = Math.Min(videoDurationSeconds, st.EndTime),
                                Description = $"{st.NarrativeRole}: {st.VisualDescription}",
                                Importance = st.NarrativeRole.Equals("Hook", StringComparison.OrdinalIgnoreCase) || st.NarrativeRole.Equals("Payoff", StringComparison.OrdinalIgnoreCase) ? "High" : "Medium",
                                ImportanceScore = 0.9,
                                FocusX = 0.5,
                                FocusY = 0.5
                            }).ToList();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _fileService.Log($"[Multi-Stage Engine] Ngoại lệ: {ex.Message}. Tiếp tục quy trình phân tích mặc định...", "WARN");
                }
            }

            string prompt = BuildEditingPrompt(videoDurationSeconds, stylePreset, detectedScenes, settings, topic, customScript, probeResult);

            // Setup candidate models list
            var candidateModels = new List<string>();
            if (!string.IsNullOrWhiteSpace(targetModel))
            {
                candidateModels.Add(targetModel);
            }

            string[] defaultFallbacks = isCustomApi
                ? new[] { "gemini-3.1-flash-lite", "fbb127bbb056c959", "9d8ca3786ebdfbea", "5bf011840784117a", "e051ce1aa80aa576" }
                : new[] { "gemini-3.1-flash-lite", "gemini-3.5-flash", "gemini-flash-lite-latest", "gemini-flash-latest" };

            foreach (var m in defaultFallbacks)
            {
                if (!candidateModels.Contains(m)) candidateModels.Add(m);
            }
            if (candidateModels.Count > 3)
            {
                candidateModels = candidateModels.Take(3).ToList();
            }

            bool includeVideoFile = !isCustomApi && !string.IsNullOrEmpty(fileUri);
            EditingPlan? plan = null;

            for (int mIdx = 0; mIdx < candidateModels.Count; mIdx++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                string currentModelName = candidateModels[mIdx];
                progressCallback?.Invoke($"Đang gửi yêu cầu lên kế hoạch dựng đến AI ({currentModelName})...", 45);

                string url;
                string jsonBody;

                if (isCustomApi)
                {
                    url = $"{apiUrl.TrimEnd('/')}/generate";
                    var customPayload = new
                    {
                        prompt = prompt,
                        model = currentModelName
                    };
                    jsonBody = JsonSerializer.Serialize(customPayload);
                }
                else
                {
                    url = $"{BaseApiUrl}/v1beta/models/{currentModelName}:generateContent?key={apiKey}";
                    var partsList = new List<object>();
                    if (includeVideoFile && !string.IsNullOrEmpty(fileUri))
                    {
                        partsList.Add(new
                        {
                            file_data = new
                            {
                                mime_type = _geminiService.GetMimeType(videoFilePath),
                                file_uri = fileUri
                            }
                        });
                    }
                    partsList.Add(new { text = prompt });

                    var requestPayload = new
                    {
                        contents = new[]
                        {
                            new
                            {
                                parts = partsList.ToArray()
                            }
                        },
                        generationConfig = new
                        {
                            response_mime_type = "application/json",
                            temperature = 0.72
                        }
                    };
                    jsonBody = JsonSerializer.Serialize(requestPayload);
                }

                try
                {
                    int timeoutSeconds = includeVideoFile ? 65 : 45;
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                    using var request = new HttpRequestMessage(HttpMethod.Post, url);
                    request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                    using var response = await _httpClient.SendAsync(request, timeoutCts.Token);
                    string responseText = await response.Content.ReadAsStringAsync(timeoutCts.Token);

                    if (response.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(responseText);
                        var root = doc.RootElement;
                        string? rawJson = null;

                        if (root.TryGetProperty("text", out var textProp))
                        {
                            rawJson = textProp.GetString();
                        }
                        else if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                        {
                            var firstCandidate = candidates[0];
                            if (firstCandidate.TryGetProperty("content", out var content) &&
                                content.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0)
                            {
                                rawJson = parts[0].GetProperty("text").GetString();
                            }
                        }
                        else if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                        {
                            var firstChoice = choices[0];
                            if (firstChoice.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var cnt))
                            {
                                rawJson = cnt.GetString();
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(rawJson))
                        {
                            plan = JsonHelper.DeserializeSafe<EditingPlan>(rawJson);
                            if (plan != null)
                            {
                                _fileService.Log($"Tạo EditingPlan thành công với model '{currentModelName}' (API: {(isCustomApi ? apiUrl : "Google Cloud")})");
                                break;
                            }
                        }
                    }
                    else
                    {
                        int statusCode = (int)response.StatusCode;
                        _fileService.Log($"Model '{currentModelName}' phản hồi HTTP {statusCode}: {responseText}", "WARN");

                        if (mIdx < candidateModels.Count - 1)
                        {
                            string nextModel = candidateModels[mIdx + 1];
                            progressCallback?.Invoke($"Model {currentModelName} phản hồi HTTP {statusCode}. Đang chuyển sang {nextModel}...", 50);
                            await Task.Delay(500, cancellationToken);
                            continue;
                        }
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    _fileService.Log($"Model '{currentModelName}' quá thời gian chờ (timeout).", "WARN");
                    if (mIdx < candidateModels.Count - 1)
                    {
                        string nextModel = candidateModels[mIdx + 1];
                        progressCallback?.Invoke($"Đang chuyển sang model dự phòng {nextModel}...", 50);
                        await Task.Delay(300, cancellationToken);
                        continue;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _fileService.Log($"Ngoại lệ khi gọi AI model '{currentModelName}': {ex.Message}", "WARN");
                }
            }

            // NẾU TẤT CẢ MODEL GEMINI ĐỀU THẤT BẠI
            if (plan == null)
            {
                if (!string.IsNullOrWhiteSpace(customScript))
                {
                    _fileService.Log($"Tất cả model Gemini đều không khả dụng. Tiến hành dựng video dựa trên kịch bản người dùng đã nhập...", "WARN");
                    plan = CreateFallbackPlan(videoDurationSeconds, stylePreset, detectedScenes, settings, topic, customScript);
                }
                else
                {
                    throw new InvalidOperationException("Không thể tạo kịch bản từ Gemini: Tất cả các model đều gặp sự cố. Vui lòng kiểm tra lại API Key, Model hoặc kết nối mạng.");
                }
            }

            bool isKeepOriginalUncut = IsKeepOriginalUncutMode(settings.DurationMode, videoDurationSeconds);
            bool isHighlightMode = !isKeepOriginalUncut;

            if (isHighlightMode)
            {
                // Highlight mode: DO NOT force full video! Keep Gemini's scenes or SelectHighlightScenes if empty or single
                if (plan.Scenes == null || plan.Scenes.Count <= 1)
                {
                    _fileService.Log($"Tự động thiết lập danh sách phân đoạn Highlight ({videoDurationSeconds:F1}s).");
                    plan.Scenes = SelectHighlightScenes(videoDurationSeconds, Math.Min(42.0, videoDurationSeconds), detectedScenes, topic);
                }
            }
            else
            {
                // Chế độ giữ nguyên toàn bộ video gốc (Không cắt)
                plan.Scenes = new List<VideoScene>
                {
                    new()
                    {
                        Id = "scene_full_01",
                        Start = 0.0,
                        End = videoDurationSeconds,
                        Description = string.IsNullOrWhiteSpace(topic) ? "Toàn bộ video" : $"{topic} - Toàn cảnh",
                        Importance = "High",
                        ImportanceScore = 1.0,
                        FocusX = 0.5,
                        FocusY = 0.5
                    }
                };
            }

            if (!string.IsNullOrWhiteSpace(customScript))
            {
                plan.NarrationScript = customScript;
            }

            // Normalize narration script, hook and subtitles for Vietnamese TTS
            if (!string.IsNullOrWhiteSpace(plan.NarrationScript))
            {
                plan.NarrationScript = TtsService.NormalizeTextForVietnameseTts(plan.NarrationScript);
                int currentWords = plan.NarrationScript.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                
                double speechRate = settings.SpeechRate > 0 ? settings.SpeechRate : 1.20;
                double wordsPerSec = 4.15 * speechRate;
                double speechTargetSec = Math.Max(12.0, (isKeepOriginalUncut ? videoDurationSeconds : Math.Min(45.0, videoDurationSeconds)) - 0.5);
                int minRequiredWords = Math.Max(60, (int)Math.Round(speechTargetSec * (wordsPerSec * 0.98)));
                int targetRequiredWords = Math.Max(70, (int)Math.Round(speechTargetSec * (wordsPerSec * 1.03)));

                _fileService.Log($"Kiểm tra độ dài kịch bản AI: {currentWords} từ (Tốc độ {speechRate:0.##}x ~{wordsPerSec:F1} từ/s; Yêu cầu: {minRequiredWords} - {targetRequiredWords} từ cho video {videoDurationSeconds:F1}s).");

                // Nếu AI viết thiếu từ (< minRequiredWords) và có Gemini API key hoặc Custom API (chỉ mở rộng khi kịch bản chưa qua Multi-Stage Engine hoặc quá ngắn):
                if (string.IsNullOrWhiteSpace(customScript) && currentWords < minRequiredWords && (!string.IsNullOrWhiteSpace(apiKey) || isCustomApi))
                {
                    _fileService.Log($"Kịch bản AI chỉ có {currentWords} từ (thiếu so với yêu cầu {minRequiredWords} từ). Tự động mở rộng kịch bản để giọng nói bao phủ trọn vẹn {videoDurationSeconds:F1}s...", "WARN");
                    progressCallback?.Invoke($"Đang bổ sung chi tiết thuyết minh ({currentWords}/{targetRequiredWords} từ) để phủ trọn video...", 50);

                    try
                    {
                        string? expandedScript = await ExpandScriptToTargetDurationAsync(
                            apiUrl, apiKey, targetModel, plan.NarrationScript, currentWords, targetRequiredWords, minRequiredWords, videoDurationSeconds, speechRate, wordsPerSec, topic, cancellationToken);
                        if (!string.IsNullOrWhiteSpace(expandedScript))
                        {
                            plan.NarrationScript = TtsService.NormalizeTextForVietnameseTts(expandedScript);
                            int newWords = plan.NarrationScript.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                            _fileService.Log($"Mở rộng kịch bản thành công: {newWords} từ (Ước tính đọc: ~{newWords / wordsPerSec:F1}s).");
                        }
                    }
                    catch (Exception ex)
                    {
                        _fileService.Log($"Không thể tự động mở rộng kịch bản: {ex.Message}", "WARN");
                    }
                }
            }
            if (!string.IsNullOrWhiteSpace(plan.Hook))
            {
                plan.Hook = TtsService.NormalizeTextForVietnameseTts(plan.Hook);
            }

            // Parse resolution from settings
            ParseResolution(settings.OutputResolution, plan);
            plan.StylePreset = stylePreset;

            ValidateAndSanitizePlan(plan, videoDurationSeconds);

            progressCallback?.Invoke("Kế hoạch dựng video AI hoàn tất!", 85);
            _fileService.Log($"Kế hoạch dựng hoàn tất: {plan.Scenes.Count} scenes, {plan.Subtitles.Count} subtitles, {plan.ZoomEffects.Count} zooms.");

            return plan;
        }

        private static void ParseResolution(string resString, EditingPlan plan)
        {
            if (string.IsNullOrWhiteSpace(resString))
            {
                plan.OutputWidth = 1080;
                plan.OutputHeight = 1920;
                return;
            }

            var m = Regex.Match(resString, @"(\d+)\s*[xX*:]\s*(\d+)");
            if (m.Success && int.TryParse(m.Groups[1].Value, out int w) && int.TryParse(m.Groups[2].Value, out int h) && w > 0 && h > 0)
            {
                plan.OutputWidth = (w % 2 != 0) ? w + 1 : w;
                plan.OutputHeight = (h % 2 != 0) ? h + 1 : h;
                return;
            }

            if (resString.Contains("720x1280"))
            {
                plan.OutputWidth = 720;
                plan.OutputHeight = 1280;
            }
            else if (resString.Contains("2160x3840"))
            {
                plan.OutputWidth = 2160;
                plan.OutputHeight = 3840;
            }
            else if (resString.Contains("1920x1080"))
            {
                plan.OutputWidth = 1920;
                plan.OutputHeight = 1080;
            }
            else if (resString.Contains("1280x720"))
            {
                plan.OutputWidth = 1280;
                plan.OutputHeight = 720;
            }
            else if (resString.Contains("1080x1080"))
            {
                plan.OutputWidth = 1080;
                plan.OutputHeight = 1080;
            }
            else
            {
                plan.OutputWidth = 1080;
                plan.OutputHeight = 1920;
            }
        }

        public void ValidateAndSanitizePlan(EditingPlan plan, double sourceDurationSeconds)
        {
            if (sourceDurationSeconds <= 0) sourceDurationSeconds = 60;

            // 1. Validate scenes
            if (plan.Scenes == null || plan.Scenes.Count == 0)
            {
                plan.Scenes = new List<VideoScene>
                {
                    new()
                    {
                        Id = "scene_01",
                        Start = 0,
                        End = sourceDurationSeconds,
                        Description = "Cảnh chính",
                        Importance = "High",
                        ImportanceScore = 1.0,
                        FocusX = 0.5,
                        FocusY = 0.5
                    }
                };
            }
            else
            {
                foreach (var scene in plan.Scenes)
                {
                    double maxStart = Math.Max(0, sourceDurationSeconds - 0.2);
                    scene.Start = Math.Clamp(scene.Start, 0, maxStart);
                    double minEnd = Math.Min(sourceDurationSeconds, scene.Start + 0.05);
                    double maxEnd = sourceDurationSeconds;
                    if (minEnd > maxEnd) minEnd = maxEnd;
                    scene.End = Math.Clamp(scene.End, minEnd, maxEnd);
                    scene.FocusX = Math.Clamp(scene.FocusX, 0.0, 1.0);
                    scene.FocusY = Math.Clamp(scene.FocusY, 0.0, 1.0);
                }

                plan.Scenes = MergeAdjacentScenes(plan.Scenes, 0.8);
            }

            // 2. Validate subtitles
            if (plan.Subtitles != null)
            {
                var validSubs = new List<SubtitleSegment>();
                foreach (var sub in plan.Subtitles)
                {
                    if (string.IsNullOrWhiteSpace(sub.Text)) continue;
                    sub.Text = TtsService.NormalizeTextForVietnameseTts(sub.Text);
                    if (string.IsNullOrWhiteSpace(sub.Text)) continue;
                    double maxStart = Math.Max(0, sourceDurationSeconds - 0.2);
                    sub.Start = Math.Clamp(sub.Start, 0, maxStart);
                    double minEnd = Math.Min(sourceDurationSeconds, sub.Start + 0.05);
                    double maxEnd = sourceDurationSeconds;
                    if (minEnd > maxEnd) minEnd = maxEnd;
                    sub.End = Math.Clamp(sub.End, minEnd, maxEnd);
                    if (sub.End > sub.Start)
                    {
                        validSubs.Add(sub);
                    }
                }
                plan.Subtitles = validSubs.OrderBy(s => s.Start).ToList();
            }

            // 3. Validate zooms
            if (plan.ZoomEffects != null)
            {
                foreach (var z in plan.ZoomEffects)
                {
                    double maxStart = Math.Max(0, sourceDurationSeconds - 0.2);
                    z.Start = Math.Clamp(z.Start, 0, maxStart);
                    double minEnd = Math.Min(sourceDurationSeconds, z.Start + 0.05);
                    double maxEnd = sourceDurationSeconds;
                    if (minEnd > maxEnd) minEnd = maxEnd;
                    z.End = Math.Clamp(z.End, minEnd, maxEnd);
                    z.FromScale = Math.Clamp(z.FromScale, 1.0, 1.15);
                    z.ToScale = Math.Clamp(z.ToScale, 1.0, 1.15);
                }
            }

            // 4. Default narration script from subtitles if missing
            if (string.IsNullOrWhiteSpace(plan.NarrationScript) && plan.Subtitles != null && plan.Subtitles.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (var sub in plan.Subtitles)
                {
                    sb.AppendLine(sub.Text);
                }
                plan.NarrationScript = sb.ToString();
            }
        }

        public static List<VideoScene> MergeAdjacentScenes(List<VideoScene>? scenes, double maxGapToMerge = 0.8)
        {
            if (scenes == null || scenes.Count <= 1) return scenes ?? new List<VideoScene>();

            var valid = scenes.Where(s => s != null && s.End > s.Start + 0.05).OrderBy(s => s.Start).ToList();
            if (valid.Count <= 1) return valid;

            var merged = new List<VideoScene>();
            var current = new VideoScene
            {
                Id = valid[0].Id,
                Start = valid[0].Start,
                End = valid[0].End,
                Description = valid[0].Description,
                Importance = valid[0].Importance,
                ImportanceScore = valid[0].ImportanceScore,
                FocusX = valid[0].FocusX,
                FocusY = valid[0].FocusY
            };

            for (int i = 1; i < valid.Count; i++)
            {
                var next = valid[i];
                if (next.Start <= current.End + maxGapToMerge)
                {
                    current.End = Math.Max(current.End, next.End);
                    if (!string.IsNullOrWhiteSpace(next.Description) && !current.Description.Contains(next.Description))
                    {
                        current.Description += " + " + next.Description;
                    }
                }
                else
                {
                    merged.Add(current);
                    current = new VideoScene
                    {
                        Id = next.Id,
                        Start = next.Start,
                        End = next.End,
                        Description = next.Description,
                        Importance = next.Importance,
                        ImportanceScore = next.ImportanceScore,
                        FocusX = next.FocusX,
                        FocusY = next.FocusY
                    };
                }
            }
            merged.Add(current);

            return merged;
        }

        public static List<VideoScene> SelectHighlightScenes(
            double sourceDuration,
            double targetDuration,
            List<VideoScene>? detectedScenes = null,
            string topic = "Khám phá video")
        {
            if (targetDuration <= 0) targetDuration = 35.0;
            if (sourceDuration <= 0) sourceDuration = targetDuration;

            var result = new List<VideoScene>();

            if (sourceDuration <= targetDuration + 1.0)
            {
                // Source is shorter than or approximately equal to target duration
                result.Add(new VideoScene
                {
                    Id = "scene_full_01",
                    Start = 0.0,
                    End = Math.Max(1.0, sourceDuration),
                    Description = $"{topic} - Toàn cảnh",
                    Importance = "High",
                    ImportanceScore = 1.0,
                    FocusX = 0.5,
                    FocusY = 0.5
                });
            }
            else
            {
                int count = Math.Clamp((int)Math.Round(targetDuration / 9.5), 2, 5);
                double baseClipLen = Math.Round(targetDuration / count, 2);
                double prevEnd = 0.0;
                double accumulatedDuration = 0.0;

                for (int i = 0; i < count; i++)
                {
                    double neededLen = (i == count - 1)
                        ? Math.Max(2.0, targetDuration - accumulatedDuration)
                        : baseClipLen;

                    double start;
                    if (i == 0)
                    {
                        start = 0.0;
                    }
                    else if (i == count - 1)
                    {
                        start = Math.Max(prevEnd + 0.3, sourceDuration - neededLen);
                    }
                    else
                    {
                        double anchor = sourceDuration * ((double)i / (count - 1));
                        if (detectedScenes != null && detectedScenes.Count > 0)
                        {
                            var nearest = detectedScenes
                                .Where(s => s.Start >= prevEnd + 0.3 && s.Start + neededLen <= sourceDuration)
                                .OrderBy(s => Math.Abs(s.Start - anchor))
                                .FirstOrDefault();

                            start = nearest != null ? nearest.Start : Math.Round(anchor - neededLen / 2.0, 2);
                        }
                        else
                        {
                            start = Math.Round(anchor - neededLen / 2.0, 2);
                        }
                    }

                    double maxStart = Math.Max(0.0, sourceDuration - 1.0);
                    start = Math.Clamp(start, 0.0, maxStart);
                    if (i > 0 && start < prevEnd)
                    {
                        start = Math.Min(maxStart, prevEnd + 0.2);
                    }

                    double end = Math.Min(start + neededLen, sourceDuration);
                    if (end <= start + 0.5)
                    {
                        end = Math.Min(sourceDuration, start + 1.0);
                    }

                    accumulatedDuration += (end - start);

                    result.Add(new VideoScene
                    {
                        Id = $"scene_hl_{i + 1:D2}",
                        Start = Math.Round(start, 2),
                        End = Math.Round(end, 2),
                        Description = i == 0 ? $"Hook mở đầu: {topic}" : (i == count - 1 ? $"Đoạn kết quả: {topic}" : $"{topic} - Điểm nhấn {i + 1}"),
                        Importance = (i == 0 || i == count - 1) ? "Very High" : "High",
                        ImportanceScore = (i == 0 || i == count - 1) ? 1.0 : 0.85,
                        FocusX = 0.5,
                        FocusY = 0.5
                    });

                    prevEnd = end;
                }
            }

            // BẢO ĐẢM TỔNG THỜI LƯỢNG SCENES >= TARGET DURATION + 0.5 GIÂY (TUYỆT ĐỐI KHÔNG BỊ HỤT GIÂY NÀO)
            double requiredTarget = targetDuration + 0.5;
            double currentSum = result.Sum(s => s.End - s.Start);

            // Giai đoạn 1: Kéo dài Start lùi về trước hoặc End tiến tới nếu còn khoảng trống
            if (currentSum < requiredTarget)
            {
                for (int k = result.Count - 1; k >= 0 && currentSum < requiredTarget; k--)
                {
                    var sc = result[k];
                    double maxEnd = (k == result.Count - 1) ? sourceDuration : result[k + 1].Start;
                    if (sc.End < maxEnd - 0.1)
                    {
                        double add = Math.Min(maxEnd - sc.End, requiredTarget - currentSum);
                        sc.End = Math.Round(sc.End + add, 2);
                        currentSum = result.Sum(s => s.End - s.Start);
                    }
                }
            }

            if (currentSum < requiredTarget)
            {
                for (int k = 0; k < result.Count && currentSum < requiredTarget; k++)
                {
                    var sc = result[k];
                    double minStart = (k == 0) ? 0.0 : result[k - 1].End;
                    if (sc.Start > minStart + 0.1)
                    {
                        double sub = Math.Min(sc.Start - minStart, requiredTarget - currentSum);
                        sc.Start = Math.Round(sc.Start - sub, 2);
                        currentSum = result.Sum(s => s.End - s.Start);
                    }
                }
            }

            // Giai đoạn 2: Nếu video gốc ngắn hơn bài thuyết minh, lặp/thêm phân cảnh từ đầu
            int loopIndex = 1;
            while (currentSum < requiredTarget)
            {
                double needed = requiredTarget - currentSum;
                double segLen = Math.Min(needed, Math.Min(sourceDuration, 5.0));
                if (segLen <= 0.5) segLen = Math.Max(1.0, sourceDuration);

                result.Add(new VideoScene
                {
                    Id = $"scene_loop_{loopIndex:D2}",
                    Start = 0.0,
                    End = Math.Round(Math.Min(sourceDuration, segLen), 2),
                    Description = $"{topic} - Khung cảnh tiếp nối {loopIndex}",
                    Importance = "Medium",
                    ImportanceScore = 0.8,
                    FocusX = 0.5,
                    FocusY = 0.5
                });
                loopIndex++;
                currentSum = result.Sum(s => s.End - s.Start);
                if (loopIndex > 20) break;
            }

            return result;
        }

        public static List<VideoScene> AlignScenesToSpeech(
            EditingPlan plan,
            TtsResult ttsResult,
            double sourceVideoDuration,
            List<VideoScene>? detectedScenes,
            double outroPadding = 1.5)
        {
            if (sourceVideoDuration <= 0) sourceVideoDuration = 60.0;
            var result = new List<VideoScene>();

            // 1. Phân tách kịch bản thành các câu hoặc phân đoạn riêng biệt
            string cleanScript = TtsService.StripTimestampsAndFormatText(plan.NarrationScript);
            if (string.IsNullOrWhiteSpace(cleanScript))
            {
                return SelectHighlightScenes(sourceVideoDuration, ttsResult.DurationSeconds + outroPadding, detectedScenes);
            }

            var rawSentences = Regex.Split(cleanScript, @"(?<=[.!?\n])\s+")
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            if (rawSentences.Count <= 2)
            {
                var expanded = new List<string>();
                foreach (var s in rawSentences)
                {
                    if (s.Contains(','))
                    {
                        var clauses = Regex.Split(s, @"(?<=,)\s+")
                            .Select(c => c.Trim())
                            .Where(c => !string.IsNullOrEmpty(c));
                        expanded.AddRange(clauses);
                    }
                    else if (Regex.IsMatch(s, @"(?<!\A)(?=\s+(?:Đang\s+|Đến\s+khi\s+|Đến\s+giờ\s+|Đứng\s+|Và\s+màn\s+|Mở\s+đầu\s+|Tiếp\s+theo\s+|Kế\s+tiếp\s+|Cuối\s+cùng\s+|Bỗng\s+|Đột\s+nhiên\s+))", RegexOptions.IgnoreCase))
                    {
                        var clauses = Regex.Split(s, @"(?<!\A)(?=\s+(?:Đang\s+|Đến\s+khi\s+|Đến\s+giờ\s+|Đứng\s+|Và\s+màn\s+|Mở\s+đầu\s+|Tiếp\s+theo\s+|Kế\s+tiếp\s+|Cuối\s+cùng\s+|Bỗng\s+|Đột\s+nhiên\s+))", RegexOptions.IgnoreCase)
                            .Select(c => c.Trim())
                            .Where(c => !string.IsNullOrEmpty(c));
                        expanded.AddRange(clauses);
                    }
                    else
                    {
                        expanded.Add(s);
                    }
                }
                if (expanded.Count > rawSentences.Count)
                {
                    rawSentences = expanded;
                }
            }

            if (rawSentences.Count == 0)
            {
                rawSentences.Add(cleanScript.Trim());
            }

            int N = rawSentences.Count;

            // 2. Tính thời lượng âm thanh tương ứng cho từng câu nói
            var sentenceTimings = new List<(double start, double end, double dur)>();

            if (ttsResult.WordTimestamps != null && ttsResult.WordTimestamps.Count > 0)
            {
                var words = ttsResult.WordTimestamps;
                int totalScriptWords = 0;
                var sentWordCounts = new List<int>();
                for (int i = 0; i < N; i++)
                {
                    int wc = rawSentences[i].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                    if (wc <= 0) wc = 1;
                    sentWordCounts.Add(wc);
                    totalScriptWords += wc;
                }

                int currentWordIdx = 0;
                for (int i = 0; i < N; i++)
                {
                    double ratio = (double)sentWordCounts[i] / totalScriptWords;
                    int wordCountForSentence = (i == N - 1)
                        ? (words.Count - currentWordIdx)
                        : (int)Math.Round(ratio * words.Count);

                    if (wordCountForSentence < 1) wordCountForSentence = 1;
                    if (currentWordIdx + wordCountForSentence > words.Count)
                    {
                        wordCountForSentence = Math.Max(1, words.Count - currentWordIdx);
                    }

                    int startI = currentWordIdx;
                    int endI = Math.Min(words.Count - 1, startI + wordCountForSentence - 1);

                    double sStart = (i == 0) ? 0.0 : words[startI].Start;
                    double sEnd = (i == N - 1)
                        ? Math.Max(words[^1].End, ttsResult.DurationSeconds)
                        : ((endI + 1 < words.Count) ? words[endI + 1].Start : words[endI].End);

                    double sDur = Math.Max(1.0, sEnd - sStart);
                    sentenceTimings.Add((sStart, sEnd, sDur));

                    currentWordIdx += wordCountForSentence;
                }
            }
            else
            {
                double totalAudioDur = ttsResult.DurationSeconds > 0 ? ttsResult.DurationSeconds : 30.0;
                int totalWords = cleanScript.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                if (totalWords <= 0) totalWords = 1;

                double timePerWord = totalAudioDur / totalWords;
                double curTime = 0.0;

                for (int i = 0; i < N; i++)
                {
                    int wCount = rawSentences[i].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                    double sDur = Math.Max(1.2, wCount * timePerWord);
                    sentenceTimings.Add((curTime, curTime + sDur, sDur));
                    curTime += sDur;
                }
            }

            // 3. Xác định mốc thời gian cảnh nguồn (source video anchor) khớp hành động cho từng câu
            var candidateScenes = (plan.Scenes != null && plan.Scenes.Count > 1) 
                ? plan.Scenes 
                : ((detectedScenes != null && detectedScenes.Count > 1) ? detectedScenes : plan.Scenes);

            if (candidateScenes == null || candidateScenes.Count <= 1)
            {
                candidateScenes = SelectHighlightScenes(sourceVideoDuration, Math.Min(42.0, sourceVideoDuration), detectedScenes, plan.Hook);
            }

            var sourceAnchors = MatchSentencesToSourceScenes(rawSentences, candidateScenes, sourceVideoDuration);

            // 4. Tạo các phân cảnh video cắt ghép khớp chính xác với từng câu nói
            double prevClipEnd = 0.0;
            bool isContinuousTimeline = sourceVideoDuration <= Math.Max(75.0, ttsResult.DurationSeconds + outroPadding + 8.0);

            for (int i = 0; i < N; i++)
            {
                double anchor = sourceAnchors[i];
                double speechDur = sentenceTimings[i].dur;

                // Câu cuối cùng được cộng thêm outroPadding (1.5s) để hình ảnh kết thúc tự nhiên
                double clipDur = (i == N - 1) ? (speechDur + outroPadding) : speechDur;
                clipDur = Math.Max(1.2, clipDur);

                double clipStart;
                double clipEnd;

                if (isContinuousTimeline)
                {
                    // Video nguồn ngắn (<= 75s): Giữ mạch chảy tự nhiên liên tục từ đầu đến cuối,
                    // tuyệt đối không cắt vụn hay nhảy cóc làm hỏng trải nghiệm người xem
                    clipStart = prevClipEnd;
                    clipEnd = Math.Min(sourceVideoDuration, clipStart + clipDur);
                    if (i == N - 1 && sourceVideoDuration > clipEnd)
                    {
                        // Chỉ kéo dài tới cuối video nếu khoảng dư còn lại rất nhỏ (<= 1.5s outro tự nhiên)
                        // Tuyệt đối không kéo dài 5-15s gây chết lặng âm thanh!
                        if (sourceVideoDuration - clipEnd <= 1.5)
                        {
                            clipEnd = sourceVideoDuration;
                        }
                    }
                }
                else
                {
                    // Video nguồn dài: Chọn lọc phân cảnh điểm nhấn, bảo đảm tiến trình thời gian luôn đi tới (monotonic)
                    if (i == 0)
                    {
                        clipStart = 0.0;
                    }
                    else
                    {
                        clipStart = Math.Max(anchor, prevClipEnd + 0.3);
                        if (clipStart >= sourceVideoDuration - 1.5)
                        {
                            clipStart = Math.Max(0.0, sourceVideoDuration - clipDur);
                        }
                    }

                    clipEnd = Math.Min(sourceVideoDuration, clipStart + clipDur);
                }

                if (clipEnd <= clipStart)
                {
                    clipEnd = Math.Min(sourceVideoDuration, clipStart + 1.5);
                }

                prevClipEnd = clipEnd;

                // Lấy focusX, focusY từ phân cảnh gần mốc thời gian này nhất
                var matchedCandidate = candidateScenes?
                    .OrderBy(s => Math.Abs(s.Start - clipStart))
                    .FirstOrDefault();
                double focusX = matchedCandidate != null && matchedCandidate.FocusX > 0 ? matchedCandidate.FocusX : plan.FocusX;
                double focusY = matchedCandidate != null && matchedCandidate.FocusY > 0 ? matchedCandidate.FocusY : plan.FocusY;

                result.Add(new VideoScene
                {
                    Id = $"scene_synced_{i + 1:D2}",
                    Start = Math.Round(clipStart, 2),
                    End = Math.Round(clipEnd, 2),
                    Description = $"Khớp câu {i + 1}: {rawSentences[i]}",
                    Importance = (i == 0 || i == N - 1) ? "Very High" : "High",
                    ImportanceScore = 1.0,
                    FocusX = focusX > 0 ? focusX : 0.5,
                    FocusY = focusY > 0 ? focusY : 0.5
                });
            }

            // Trả về trực tiếp danh sách phân cảnh mà không gộp để bảo đảm thời lượng khớp 100% bài thuyết minh
            return result;
        }

        private static List<double> MatchSentencesToSourceScenes(
            List<string> sentences,
            List<VideoScene>? candidateScenes,
            double sourceVideoDuration)
        {
            int N = sentences.Count;
            var anchors = new List<double>();
            if (N == 0) return anchors;

            // Câu đầu tiên luôn bắt đầu từ giây 0.0 của video gốc
            anchors.Add(0.0);
            if (N == 1) return anchors;

            double prevAnchor = 0.0;

            for (int i = 1; i < N; i++)
            {
                string sent = sentences[i].ToLowerInvariant();
                double chosenAnchor = -1.0;

                if (candidateScenes != null && candidateScenes.Count > 0)
                {
                    double bestAffinity = 0.0;
                    VideoScene? bestScene = null;

                    for (int sIdx = 0; sIdx < candidateScenes.Count; sIdx++)
                    {
                        var sc = candidateScenes[sIdx];
                        if (sc.Start < prevAnchor - 0.5) continue;

                        double score = CalculateSceneSentenceAffinity(sent, sc);
                        if (score > bestAffinity)
                        {
                            bestAffinity = score;
                            bestScene = sc;
                        }
                    }

                    // Nếu có sự tương đồng ngữ nghĩa rõ ràng (> 1.5) và mốc thời gian cách biệt (> 1.5s)
                    if (bestScene != null && bestAffinity >= 1.5 && bestScene.Start > prevAnchor + 1.5)
                    {
                        chosenAnchor = bestScene.Start;
                    }
                    else
                    {
                        // PHƯƠNG ÁN HIGHLIGHT ĐẮT GIÁ: Nếu không khớp từ khóa cụ thể,
                        // phân bổ điểm nhấn đều theo tiến trình video gốc để người xem thấy trọn vẹn mạch truyện từ mở đầu đến cao trào!
                        double targetRatio = (double)i / N;
                        double targetTime = Math.Clamp(targetRatio * sourceVideoDuration, prevAnchor + 1.5, Math.Max(prevAnchor + 1.5, sourceVideoDuration - 2.0));

                        // Tìm phân cảnh phát hiện gần mốc targetTime nhất
                        var nearby = candidateScenes
                            .Where(s => s.Start >= prevAnchor + 1.5 && s.Start <= sourceVideoDuration - 1.5)
                            .OrderBy(s => Math.Abs(s.Start - targetTime))
                            .FirstOrDefault();

                        chosenAnchor = nearby != null ? nearby.Start : targetTime;
                    }
                }
                else
                {
                    double targetRatio = (double)i / N;
                    chosenAnchor = Math.Clamp(targetRatio * sourceVideoDuration, prevAnchor + 1.5, Math.Max(prevAnchor + 1.5, sourceVideoDuration - 2.0));
                }

                prevAnchor = chosenAnchor;
                anchors.Add(chosenAnchor);
            }

            return anchors;
        }

        private static double CalculateSceneSentenceAffinity(string sentence, VideoScene scene)
        {
            double score = 0.0;
            string desc = (scene.Description + " " + scene.Action + " " + scene.Subject).ToLowerInvariant();

            // 1. Nhóm từ khóa Cao trào / Cá cắn câu / Cần giật mạnh / Bất ngờ
            string[] biteKeywords = { "cắn câu", "cắn", "giật", "uốn cong", "giật mạnh", "quẫy", "bất ngờ", "bỗng", "đột nhiên", "kéo mạnh", "mắc câu" };
            string[] biteSceneWords = { "giật", "uốn", "cắn", "cong", "quẫy", "câu được", "cắn câu" };
            if (biteKeywords.Any(k => sentence.Contains(k)) && biteSceneWords.Any(w => desc.Contains(w)))
            {
                score += 15.0;
            }

            // 2. Nhóm từ khóa Dùng vợt / Vớt cá / Bắt gọn / Trợ giúp
            string[] netKeywords = { "vợt", "bắt", "vớt", "lao tới", "giúp", "kéo lên", "ôm", "túm", "vớt cá", "bắt gọn" };
            string[] netSceneWords = { "vợt", "vớt", "bắt", "lưới", "giúp", "kéo" };
            if (netKeywords.Any(k => sentence.Contains(k)) && netSceneWords.Any(w => desc.Contains(w)))
            {
                score += 15.0;
            }

            // 3. Nhóm từ khóa Thành quả / Cá to / Khổng lồ / Nụ cười / Hạnh phúc
            string[] trophyKeywords = { "thành quả", "kết quả", "cá to", "cá lớn", "khổng lồ", "hạnh phúc", "cười", "vui sướng", "tuyệt vời", "chiến thắng" };
            string[] trophySceneWords = { "thành quả", "kết quả", "to", "lớn", "khổng lồ", "cười", "vui", "thành công" };
            if (trophyKeywords.Any(k => sentence.Contains(k)) && trophySceneWords.Any(w => desc.Contains(w)))
            {
                score += 15.0;
            }

            // 4. Nhóm từ khóa Nhân vật / Diễn viên / Chân dung / Quân bài / Biến đổi
            string[] actorKeywords = { "diễn viên", "nam diễn viên", "điển trai", "lãng tử", "quân bài", "bài", "cờ bạc", "ảo thuật", "chân dung", "ánh mắt", "mỉm cười", "nụ cười", "nhân vật", "gương mặt", "khuôn mặt" };
            string[] actorSceneWords = { "diễn viên", "trai", "bài", "chân dung", "mặt", "người", "lãng tử", "cười", "ảo thuật" };
            if (actorKeywords.Any(k => sentence.Contains(k)) && actorSceneWords.Any(w => desc.Contains(w)))
            {
                score += 15.0;
            }

            // 5. Nhóm từ khóa Chờ đợi / Kiên nhẫn / Chuẩn bị / Khung cảnh ban đầu
            string[] waitKeywords = { "chờ", "đợi", "kiên nhẫn", "yên lặng", "ngồi", "đứng", "bờ sông", "thuyền" };
            string[] waitSceneWords = { "chờ", "đợi", "câu cá", "ngồi", "đứng", "thuyền", "bờ", "yên" };
            if (waitKeywords.Any(k => sentence.Contains(k)) && waitSceneWords.Any(w => desc.Contains(w)))
            {
                score += 8.0;
            }

            // 6. Nhóm từ khóa Học đường / Lớp học / Lau bảng / Bàn ghế
            string[] schoolKeywords = { "học trò", "học sinh", "lớp", "bảng", "lau bảng", "xóa chữ", "bàn", "ghế", "cửa lớp", "trường", "phòng học" };
            string[] schoolSceneWords = { "học sinh", "lớp", "bảng", "lau", "phòng học", "trường", "bàn", "ghế", "cửa" };
            if (schoolKeywords.Any(k => sentence.Contains(k)) && schoolSceneWords.Any(w => desc.Contains(w)))
            {
                score += 15.0;
            }

            // 7. Nhóm từ khóa Trêu đùa / Chai nước / Chọc ghẹo
            string[] prankKeywords = { "chai nước", "chọc", "ghẹo", "trêu", "giỡn", "nghịch", "chuyền", "chọc ghẹo", "bất ổn" };
            string[] prankSceneWords = { "chai", "nước", "chọc", "ghẹo", "trêu", "cười", "nghịch", "đuổi" };
            if (prankKeywords.Any(k => sentence.Contains(k)) && prankSceneWords.Any(w => desc.Contains(w)))
            {
                score += 15.0;
            }

            // 8. Nhóm từ khóa Tình cảm / Lãng mạn / Ngắm mưa / Ánh mắt / Chờ đợi
            string[] romanceKeywords = { "tình cảm", "drama", "ngắm mưa", "mưa", "chờ", "đợi", "bước lại", "lại gần", "crush", "ngơ ngác", "ánh mắt", "nhìn" };
            string[] romanceSceneWords = { "mưa", "cửa sổ", "đứng", "nhìn", "chờ", "ngắm", "mắt", "lại gần" };
            if (romanceKeywords.Any(k => sentence.Contains(k)) && romanceSceneWords.Any(w => desc.Contains(w)))
            {
                score += 15.0;
            }

            // 9. Nhóm từ khóa Vũ đạo / Nhảy múa / Quẩy / Cửa lớp
            string[] danceKeywords = { "vũ đạo", "nhảy", "múa", "xào nấu", "bất ổn", "quẩy", "cửa lớp", "diễn", "tạo dáng" };
            string[] danceSceneWords = { "nhảy", "múa", "vũ đạo", "cửa", "quẩy", "động tác", "hài" };
            if (danceKeywords.Any(k => sentence.Contains(k)) && danceSceneWords.Any(w => desc.Contains(w)))
            {
                score += 15.0;
            }

            // 10. Trùng lặp cụm từ ghép 2 từ (Bigrams) đặc trưng tiếng Việt
            var rawWords = sentence.Split(new[] { ' ', ',', '.', '!', '?', ';', ':' }, StringSplitOptions.RemoveEmptyEntries);
            for (int w = 0; w < rawWords.Length - 1; w++)
            {
                string bigram = $"{rawWords[w]} {rawWords[w + 1]}";
                if (bigram.Length >= 5 && desc.Contains(bigram))
                {
                    score += 5.0;
                }
            }

            // 7. Trùng lặp từ vựng tự nhiên (loại trừ từ dừng phổ biến)
            string[] stopWords = { "và", "của", "cho", "thì", "là", "ở", "trong", "đã", "đang", "được", "rất", "này", "đó", "một", "những", "các", "với", "như", "khi" };
            var sentWords = rawWords.Where(w => w.Length >= 3 && !stopWords.Contains(w));
            foreach (var w in sentWords)
            {
                if (desc.Contains(w)) score += 2.0;
            }

            return score;
        }

        public static bool IsKeepOriginalUncutMode(string? durationMode, double durationSeconds)
        {
            if (string.IsNullOrWhiteSpace(durationMode)) return durationSeconds <= 90.0;
            if (durationMode.Contains("Toàn bộ") || durationMode.Contains("Không cắt")) return true;
            if (durationMode.Contains("Highlight") || durationMode.Contains("Cắt ghép")) return false;
            return durationSeconds <= 90.0;
        }

        private string BuildEditingPrompt(
            double durationSeconds,
            string stylePreset,
            List<VideoScene>? detectedScenes,
            AppSettings settings,
            string topic,
            string? customScript,
            VideoProbeResult? probeResult = null)
        {
            string durStr = durationSeconds.ToString("0.0", CultureInfo.InvariantCulture);
            bool isKeepOriginalUncut = IsKeepOriginalUncutMode(settings.DurationMode, durationSeconds);
            bool isHighlightMode = !isKeepOriginalUncut;

            double speechRate = settings.SpeechRate > 0 ? settings.SpeechRate : 1.20;
            double wordsPerSec = 4.15 * speechRate;

            // Target words calibrated densely based on speech rate to fill timeline completely and avoid trailing dead silence.
            double effectiveTargetDuration = isKeepOriginalUncut ? durationSeconds : Math.Min(45.0, durationSeconds);
            double speechTargetSeconds = Math.Max(12.0, effectiveTargetDuration - 0.5);
            int minWords = Math.Max(60, (int)Math.Round(speechTargetSeconds * (wordsPerSec * 0.98)));
            int targetWords = Math.Max(70, (int)Math.Round(speechTargetSeconds * (wordsPerSec * 1.03)));
            int maxWords = (int)Math.Round(speechTargetSeconds * (wordsPerSec * 1.08));
            int targetSentences = Math.Max(4, (int)Math.Round(speechTargetSeconds / 3.0));

            // Lấy thông tin Persona nếu Personality Engine được kích hoạt
            string personaDirectives = "";
            string fewShotSection = "";
            if (_personaManager != null)
            {
                var persona = _personaManager.GetActivePersona();
                if (persona != null)
                {
                    var pSb = new StringBuilder();
                    pSb.AppendLine($"=== HÌNH TƯỢNG VÀ TÍNH CÁCH (ACTIVE PERSONA: {persona.Name.ToUpperInvariant()}) ===");
                    pSb.AppendLine($"- Mô tả: {persona.Description}");
                    pSb.AppendLine($"- Giọng đọc & tông văn: {persona.VoiceTone}");
                    if (!string.IsNullOrWhiteSpace(persona.SystemPromptOverride))
                    {
                        pSb.AppendLine($"- Chỉ thị riêng: {persona.SystemPromptOverride.Trim()}");
                    }
                    if (persona.CustomRules != null && persona.CustomRules.Count > 0)
                    {
                        pSb.AppendLine("- Nguyên tắc văn phong của Persona:");
                        foreach (var r in persona.CustomRules)
                        {
                            pSb.AppendLine($"  + {r}");
                        }
                    }
                    personaDirectives = pSb.ToString().Trim();

                    // TRUY XUẤT CÁC VÍ DỤ FEW-SHOT CHUẨN XÁC THEO CHỦ ĐỀ VÀ PERSONA
                    if (_fewShotManager != null)
                    {
                        var examples = _fewShotManager.GetRelevantExamples(topic, persona.Id, 2);
                        if (examples != null && examples.Count > 0)
                        {
                            var fsb = new StringBuilder();
                            fsb.AppendLine("=== VÍ DỤ MINH HỌA MẪU TIÊU CHUẨN (FEW-SHOT LEARNING) ===");
                            fsb.AppendLine("HÃY HỌC TẬP CHÍNH XÁC CÁCH KỂ CHUYỆN, ĐỘ DÀI VÀ CẤU TRÚC TỪ CÁC VÍ DỤ MẪU SAU:");
                            fsb.AppendLine(_fewShotManager.FormatFewShotPromptSection(examples));
                            fewShotSection = fsb.ToString().Trim();
                        }
                    }
                }
            }

            string tone = !string.IsNullOrWhiteSpace(settings.NarrationTone)
                ? settings.NarrationTone
                : "🤖 Tự Động Phân Tích (Theo nội dung video)";

            string toneGuide;
            if (tone.Contains("Thế Giới Có Gì Hay") || tone.Contains("Sự Thật") || tone.Contains("Mẹo Độc Lạ"))
            {
                toneGuide = @"- VĂN PHONG CHỦ ĐẠO: CHUẨN KÊNH 'THẾ GIỚI CÓ GÌ HAY?' & ZACK D. FILMS (TRIỆU VIEW VIRAL).
  + BƯỚC 1: Hook trực diện đập thẳng vào mắt và tai (0-2s): 'Đây là cách...', 'Tại sao...', 'Điều gì sẽ xảy ra nếu...'. Cấm chào hỏi, cấm triết lý đạo lý.
  + BƯỚC 2: Nêu ngắn gọn luật lệ, hoàn cảnh hoặc áp lực thực tế (2-5s) như thời gian, quy định, rào cản vật lý.
  + BƯỚC 3: Giải mã cơ chế, mánh khóe, từng chuyển động vi mô trên màn hình (5-12s). Dùng từ ngữ đắt giá, giàu hình ảnh thị giác và cơ chế thực tế.
  + BƯỚC 4: Kết thúc bằng CTA sáng tạo: 'Biến nút thích của tôi thành [đồ vật]' hoặc câu hỏi thăm dò quan điểm.";
            }
            else if (tone.Contains("Tự Động") || tone.Contains("Tự động") || string.IsNullOrWhiteSpace(tone))
            {
                toneGuide = @"- VĂN PHONG THUYẾT MINH: TỰ ĐỘNG PHÂN TÍCH VÀ QUYẾT ĐỊNH THEO NỘI DUNG THỰC TẾ CỦA TỪNG VIDEO.
  + Luôn tuân thủ quy tắc 4 bước vàng: Hook trực diện 0-2s ('Đây là cách...', 'Tại sao...') -> Bối cảnh/quy luật -> Giải mã cơ chế/hành động vi mô -> Cú chốt CTA sáng tạo.
  + Nếu video là mẹo vặt, cơ chế, sự thật đời sống hoặc 3D animation: Áp dụng văn phong CHUẨN THẾ GIỚI CÓ GÌ HAY / ZACK D. FILMS, phân tích cơ chế rõ ràng, kích thích tò mò cao độ.
  + Nếu video mang không khí học đường, troll bạn bè, hài hước: Áp dụng văn phong DÍ DỎM, HÀI HƯỚC, BẮT TREND GEN Z với các nhận xét duyên dáng, chêm từ tiếng Anh hiện đại (flex, vibe, crush, visual, chemistry, chill, match, slay).
  + Nếu video mang tính gay cấn, bất ngờ, hành động: Áp dụng văn phong KỊCH TÍNH, NGHẸT THỞ, dồn dập kích thích tò mò.
  + Nếu video mang tính sâu lắng, ấm áp, tình cảm: Áp dụng văn phong TRUYỀN CẢM HỨNG, CHẠM VÀO CẢM XÚC.
  + Nếu video mang tính trải nghiệm, khám phá, độc lạ: Áp dụng văn phong REVIEW SẮC BÉN, KHÁM PHÁ KỲ THÚ.";
            }
            else if (tone.Contains("Hài hước") || tone.Contains("trend"))
            {
                toneGuide = @"- VĂN PHONG CHỦ ĐẠO: HÀI HƯỚC, DÍ DỎM, BẮT TREND TIKTOK VIỆT NAM.
  + Bình luận dí dỏm, duyên dáng, cà khịa đáng yêu các chi tiết đắt giá hoặc hài hước trong video.
  + Áp dụng các từ ngữ và lối ví von đời thường, dí dỏm của giới trẻ (ví dụ: 'pha xử lý đi vào lòng đất', 'đúng là bạn thân nhà người ta', 'tiếp tế siêu to khổng lồ', 'không trượt phát nào', 'ai làm lại bạn nữa', 'đỉnh nóc kịch trần').
  + Lối kể chuyện như một người bạn thân đang ngồi bình luận trực tiếp bên cạnh, tạo tiếng cười tự nhiên, làm người xem thích thú và muốn xem đi xem lại.";
            }
            else if (tone.Contains("Kịch tính") || tone.Contains("Gay cấn"))
            {
                toneGuide = @"- VĂN PHONG CHỦ ĐẠO: KỊCH TÍNH, NGHẸT THỞ, CAO TRÀO DỒN DẬP.
  + Dùng câu ngắn, nhịp điệu dồn dập, động từ mạnh, kích thích tột cùng sự hồi hộp và tò mò.
  + Tạo cảm giác mỗi giây trôi qua đều là khoảnh khắc nghẹt thở, không thể đoán trước được điều gì sẽ xảy ra tiếp theo.";
            }
            else if (tone.Contains("Review") || tone.Contains("Chân thực"))
            {
                toneGuide = @"- VĂN PHONG CHỦ ĐẠO: REVIEW SẮC BÉN, CHÂN THỰC, SÂU SẮC.
  + Bình luận trực diện, đi thẳng vào bản chất của tình huống, phân tích hành vi và biểu cảm của các nhân vật một cách sắc sảo, hóm hỉnh và thông minh.";
            }
            else if (tone.Contains("Cảm xúc") || tone.Contains("Truyền cảm hứng"))
            {
                toneGuide = @"- VĂN PHONG CHỦ ĐẠO: ẤM ÁP, SÂU LẮNG, CHẠM VÀO CẢM XÚC.
  + Lời văn nhẹ nhàng, truyền cảm hứng, tập trung vào tình bạn, tình người và những giá trị tích cực đằng sau từng cử chỉ, ánh mắt.";
            }
            else
            {
                toneGuide = @"- VĂN PHONG CHỦ ĐẠO: KHÁM PHÁ KỲ THÚ, KÍCH THÍCH TÒ MÒ.
  + Đặt ra câu hỏi mở kích thích trí tưởng tượng, dẫn dắt người xem khám phá từng diễn biến bất ngờ trên màn hình.";
            }

            var sb = new StringBuilder();
            sb.AppendLine("BẠN LÀ BIÊN KỊCH SHORTS/TIKTOK TRIỆU VIEW CHUYÊN NGHIỆP HÀNG ĐẦU VIỆT NAM.");
            sb.AppendLine("Nhiệm vụ của bạn là tạo một kịch bản thuyết minh tiếng Việt đỉnh cao, lôi cuốn người xem từ giây đầu tiên đến giây cuối cùng.");
            sb.AppendLine();
            sb.AppendLine($"THÔNG TIN VIDEO:");
            sb.AppendLine($"- Thời lượng video gốc: {durStr} giây.");
            sb.AppendLine($"- Phong cách biên tập: {stylePreset}.");
            sb.AppendLine($"- Văn phong thuyết minh yêu cầu: {tone}.");

            if (probeResult != null && probeResult.Success)
            {
                sb.AppendLine();
                sb.AppendLine("=== THÔNG TIN BỐI CẢNH THỰC TẾ TRONG VIDEO (TỰ ĐỘNG PHÁT HIỆN) ===");
                if (!string.IsNullOrWhiteSpace(probeResult.AudioText))
                {
                    sb.AppendLine($"- ÂM THANH / LỜI BÀI HÁT PHÁT HIỆN TRONG VIDEO: \"{probeResult.AudioText}\"");
                    sb.AppendLine("  👉 Video có giai điệu / ca khúc này! Hãy lồng ghép thông minh, dí dỏm, bình luận bám sát tâm trạng bài hát hoặc tạo sự tương phản hài hước!");
                }
                if (!string.IsNullOrWhiteSpace(probeResult.OcrText))
                {
                    sb.AppendLine($"- VĂN BẢN / CHỮ NHẬN DIỆN TRÊN MÀN HÌNH (OCR): \"{probeResult.OcrText}\"");
                }
                if (!string.IsNullOrWhiteSpace(probeResult.SuggestedTopic))
                {
                    sb.AppendLine($"- GỢI Ý BỐI CẢNH: \"{probeResult.SuggestedTopic}\"");
                }
                sb.AppendLine("============================================================");
            }

            bool hasSpecificTopic = !string.IsNullOrWhiteSpace(topic);
            if (hasSpecificTopic)
            {
                sb.AppendLine($"- CHỦ ĐỀ & BỐI CẢNH BẮT BUỘC: \"{topic}\".");
                sb.AppendLine($"  🚨 Kịch bản PHẢI BÁM SÁT 100% bối cảnh chủ đề \"{topic}\" này! Dùng từ ngữ chính xác, miêu tả sinh động từng chi tiết liên quan đến chủ đề này!");
            }
            else
            {
                sb.AppendLine("- CHỦ ĐỀ: Tự động quan sát và suy luận tình huống diễn ra trên màn hình.");
                sb.AppendLine("  🚨 CẢNH BÁO CỐT TỬ: TUYỆT ĐỐI CẤM viết văn mẫu triết lý đạo lý chung chung, cấm tự bịa ra lái xe hay kỹ năng chuyên nghiệp nếu video không liên quan!");
                sb.AppendLine("  👉 Hãy viết bằng phong cách dí dỏm, châm biếm hài hước hoặc kịch tính cuốn hút theo phong cách TikTok/Shorts hiện đại!");
            }
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(personaDirectives))
            {
                sb.AppendLine("============================================================");
                sb.AppendLine(personaDirectives);
                sb.AppendLine("============================================================");
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(fewShotSection))
            {
                sb.AppendLine("============================================================");
                sb.AppendLine(fewShotSection);
                sb.AppendLine("============================================================");
                sb.AppendLine();
            }

            sb.AppendLine("============================================================");
            sb.AppendLine("🔥 BÍ QUYẾT KỊCH BẢN THUYẾT MINH VIRAL TRIỆU VIEW 🔥");
            sb.AppendLine("1. NGUYÊN TẮC BÁM SÁT THỰC TẾ & KHÔNG ĐẠO LÝ SÁO RỖNG:");
            sb.AppendLine("   - BÁM SÁT TÌNH HUỐNG THỰC TẾ: Dù là video hài hước, troll, đời sống, công nghệ hay kỹ năng đỉnh cao, kịch bản phải nói trúng những gì đang diễn ra, miêu tả sinh động hành động, biểu cảm, công cụ, không gian.");
            sb.AppendLine("   - LỐI VIẾT SẮC BÉN, CUỐN HÚT: Dùng câu từ giàu hình ảnh, nhịp điệu dồn dập, ví von bắt tai, gây tò mò hoặc tạo tiếng cười sảng khoái.");
            sb.AppendLine("   - ❌ TUYỆT ĐỐI CẤM: Không mở đầu kiểu 'Chào mừng các bạn', không nói triết lý sáo rỗng giáo điều.");
            sb.AppendLine("   - ❌ CẤM CÂU LIKE RẺ TIỀN: Tuyệt đối KHÔNG ĐƯỢC có câu 'thả tim', 'follow kênh', 'bấm like', 'đăng ký kênh'. Người xem rất ghét điều này!");
            sb.AppendLine();
            sb.AppendLine("2. CẤU TRÚC GIỮ CHÂN NGƯỜI XEM (RETENTION ĐỈNH CAO):");
            sb.AppendLine("   - HOOK 3 GIÂY ĐẦU: Đập thẳng vào vấn đề hoặc đưa ra một so sánh bất ngờ, một câu hỏi xoáy, một lời nhận xét sắc lẹm khiến người xem phải dừng ngón tay lại xem tiếp.");
            sb.AppendLine("   - PHẦN THÂN: Từng câu thoại bám theo từng diễn biến, nhịp điệu tương phản, từ ngữ sinh động.");
            sb.AppendLine("   - CÚ CHỐT CUỐI: Kết thúc bằng một câu đúc kết đắt giá, một câu bông đùa hóm hỉnh hoặc một cú twist bất ngờ để lại dư âm mạnh mẽ.");
            sb.AppendLine();
            sb.AppendLine("3. CÁC MẪU KỂ CHUYỆN ĐỈNH CAO TRIỆU VIEW (HỌC HỎI CẤU TRÚC, TUYỆT ĐỐI KHÔNG CHÉP NGUYÊN VĂN):");
            sb.AppendLine("   - [Chuẩn phong cách Thế Giới Có Gì Hay / Zack D. Films / Mẹo & Sự Thật Độc Lạ]:");
            sb.AppendLine("     \"Đây là cách một số người gian lận trên sân golf. Sau khi đánh bóng thì bạn chỉ có khoảng 3 phút để tìm bóng. Nếu không tìm thấy, bóng sẽ chính thức được tính là bị mất. Vì vậy, một số người chơi sử dụng một thiết bị nhỏ được giấu ở chân để chứa thêm một quả bóng. Chỉ với một động tác đơn giản, họ bí mật thả quả bóng ra khiến mọi người tưởng rằng họ vừa tìm thấy quả bóng ban đầu. Biến nút thích của tôi thành bóng golf.\"");
            sb.AppendLine("   - [Chuẩn phong cách Giải mã cơ chế / Khoa học & Đời sống]:");
            sb.AppendLine("     \"Tại sao thành phố này lại đổ 96 triệu quả bóng nhựa màu đen xuống hồ chứa nước? Thoạt nhìn ai cũng tưởng đây là sự lãng phí khủng khiếp. Nhưng thực tế, ánh nắng mặt trời kết hợp Clo sẽ tạo ra chất gây ung thư nguy hiểm. Những quả bóng này đóng vai trò như một tấm khiên khổng lồ, ngăn chặn hoàn toàn tia cực tím và giảm thiểu tối đa sự bốc hơi nước. Biến nút thích của tôi thành màu đen của quả bóng.\"");
            sb.AppendLine("   - [Chuẩn phong cách Đời sống / Tình huống troll hóm hỉnh]:");
            sb.AppendLine("     \"Đừng bao giờ để bạn thân trông hộ đồ nếu không muốn nhận cái kết đắng ngắt. Vừa quay lưng đi hai phút, thanh niên ở lại đã nhanh tay uống sạch nửa cốc rồi đổ thêm nước lọc kèm mấy viên sỏi vào đáy ly. Nhìn quả mặt ngơ ngác khi hút trúng viên sỏi mà cười ra nước mắt!\"");
            sb.AppendLine();
            sb.AppendLine("🚨 NGUYÊN TẮC CỐT TỬ - TUYỆT ĐỐI CẤM CÁC VĂN MẪU AI SAU ĐÂY (NGƯỜI XEM SẼ THẤY RẤT NHẠT VÀ LƯỚT QUA):");
            sb.AppendLine("   - ❌ TUYỆT ĐỐI CẤM: 'Mới nhìn cứ ngỡ cảnh tượng bình thường, nhưng nếu để ý kỹ...', 'Bạn sẽ không tin vào mắt mình...', 'Chào mừng các bạn đến với video...'.");
            sb.AppendLine("   - ❌ CẤM các tính từ sáo rỗng vô hồn: 'đẳng cấp của sự chuyên nghiệp', 'tính toán tỉ mỉ đến từng milimét', 'thao tác dứt khoát', 'vô cùng tuyệt vời', 'khoảnh khắc kỳ diệu'.");
            sb.AppendLine("   - ❌ CẤM câu like ăn xin sáo rỗng: 'hãy like và đăng ký kênh'. Hãy dùng CTA tương tác sáng tạo: 'Biến nút thích của tôi thành [đồ vật trong video]' hoặc câu hỏi mở tò mò.");
            sb.AppendLine("   - Nội dung video có gì thì miêu tả đúng sự thật, dí dỏm, hóm hỉnh hoặc kịch tính đúng bối cảnh đó!");
            sb.AppendLine();
            sb.AppendLine("4. NHỊP ĐIỆU CÂU TƯƠNG PHẢN & PHÁT ÂM AI CHUẨN XÁC:");
            sb.AppendLine("   - Xen kẽ câu ngắn dồn dập và câu miêu tả cảm xúc mượt mà.");
            sb.AppendLine("   - Dùng dấu chấm (.) và dấu phẩy (,) chuẩn xác để giọng AI TTS ngắt nhịp thở tự nhiên, đọc tròn vành rõ chữ, có ngữ điệu nhấn nhá truyền cảm.");
            sb.AppendLine("   - Mỗi câu phải hoàn chỉnh (có đủ chủ ngữ - vị ngữ), tránh câu cụt vô nghĩa.");
            if (!string.IsNullOrWhiteSpace(settings.AiTrainingInstruction))
            {
                sb.AppendLine();
                sb.AppendLine("5. CHỈ THỊ HUẤN LUYỆN RIÊNG TỪ NGƯỜI DÙNG:");
                sb.AppendLine($"   👉 \"{settings.AiTrainingInstruction}\"");
            }
            sb.AppendLine("============================================================");
            sb.AppendLine();
            sb.AppendLine(toneGuide);
            sb.AppendLine("============================================================");
            sb.AppendLine();

            if (isHighlightMode)
            {
                sb.AppendLine("============================================================");
                sb.AppendLine($"CHẾ ĐỘ BIÊN TẬP: CẮT GHÉP HIGHLIGHT SHORTS ({durStr}s -> VIDEO NGẮN ĐẮT GIÁ {effectiveTargetDuration:F0}s):");
                sb.AppendLine($"- ĐỘ DÀI KỊCH BẢN BẮT BUỘC: Bạn BẮT BUỘC PHẢI VIẾT từ {minWords} đến {maxWords} từ tiếng Việt (mục tiêu {targetWords} từ, khoảng {targetSentences} câu hoàn chỉnh).");
                sb.AppendLine($"  🚨 NGUYÊN TẮC BẮT BUỘC: Giọng đọc thuyết minh PHẢI NÓI LIÊN TỤC từ giây 0:00 cho đến tận giây thứ {speechTargetSeconds:F0}s của video.");
                sb.AppendLine($"  🚨 CẢNH BÁO QUAN TRỌNG: TUYỆT ĐỐI CẤM viết ngắn dưới {minWords} từ! Tốc độ đọc giọng nói được cấu hình là {speechRate:0.##}x (~{wordsPerSec:F1} từ/giây). Nếu viết ngắn dưới {minWords} từ, giọng đọc sẽ dừng sớm và video bị câm lặng âm thanh!");
                sb.AppendLine($"- Hãy chọn lọc {targetSentences} khoảnh khắc diễn biến đắt giá nhất rải đều theo mạch truyện.");
                sb.AppendLine("- MỖI CẢNH TRONG MẢNG 'scenes' PHẢI TƯƠNG ỨNG TRỰC TIẾP VỚI 1 CÂU TRONG 'narrationScript'!");
                sb.AppendLine("  + Cảnh 1 (Mở đầu): 'start' tại 0.0s -> tương ứng Câu 1 (Hook).");
                sb.AppendLine("  + Cảnh 2..N: 'start' ĐẶT CHÍNH XÁC tại giây mà hành động đó BẮT ĐẦU trong video gốc.");
                sb.AppendLine("  + Cảnh cuối (Kết quả/Payoff): 'start' ĐẶT CHÍNH XÁC tại giây kết quả hoặc pha hành động mãn nhãn xuất hiện.");
                sb.AppendLine("- start và end của mỗi cảnh phải bám đúng khoảnh khắc hành động trên màn hình, không kéo dài tĩnh.");
                sb.AppendLine("============================================================");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("============================================================");
                sb.AppendLine($"CHẾ ĐỘ BIÊN TẬP: THUYẾT MINH TOÀN BỘ VIDEO ({durStr}s KHÔNG CẮT XUYÊN SUỐT):");
                sb.AppendLine($"- Kịch bản thuyết minh và phụ đề PHẢI TRẢI DÀI BAO QUÁT TOÀN BỘ {durStr} GIÂY TỪ ĐẦU (0:00) ĐẾN TẬN GIÂY CUỐI CÙNG ({durStr}s).");
                sb.AppendLine($"- ĐỘ DÀI KỊCH BẢN BẮT BUỘC: Bạn BẮT BUỘC PHẢI VIẾT từ {minWords} đến {maxWords} từ tiếng Việt (mục tiêu {targetWords} từ, khoảng {targetSentences} câu hoàn chỉnh).");
                sb.AppendLine($"  🚨 CẢNH BÁO CỐT TỬ: TUYỆT ĐỐI CẤM VIẾT NGẮN DƯỚI {minWords} TỪ!");
                sb.AppendLine($"     Tốc độ đọc giọng nói được cấu hình là {speechRate:0.##}x (~{wordsPerSec:F1} từ/giây). Với video {durStr}s, bạn PHẢI VIẾT TỐI THIỂU {minWords} TỪ (mục tiêu {targetWords} từ). Nếu bạn chỉ viết dưới {minWords} từ (ví dụ 100-180 từ), giọng nói sẽ dừng ở giây 25-35 và để lại 15-25 giây im lặng chết chóc ở cuối video!");
                sb.AppendLine($"- PHÂN BỔ NHỊP KỂ ĐỀU ĐẶN THEO DÒNG THỜI GIAN:");
                sb.AppendLine($"  + Giọng đọc phải nói liên tục, mượt mà từ giây đầu tiên (0:00) đến sát giây cuối ({speechTargetSeconds:F0}s) của video.");
                sb.AppendLine($"  + Chia đều nội dung theo từng giai đoạn diễn biến trên màn hình: Mở đầu hook (0-3s) -> Diễn biến quan sát chi tiết 1 -> Diễn biến 2 -> Cao trào dồn dập -> Cú chốt kết thúc ở những giây cuối.");
                sb.AppendLine($"  + Tuyệt đối không được nói dồn hết ở nửa đầu rồi để nửa sau video chạy im lặng!");
                sb.AppendLine("- Miêu tả tỉ mỉ chi tiết bối cảnh, công cụ, từng cử chỉ, biểu cảm và chuyển động của nhân vật trên màn hình để giữ người xem xuyên suốt {durStr}s!");
                sb.AppendLine("- Giữ nguyên vẹn toàn bộ video gốc (scenes bao quát từ 0.0s đến hết video), không cắt ngắn video.");
                sb.AppendLine("============================================================");
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(customScript))
            {
                sb.AppendLine("============================================================");
                sb.AppendLine("!!! ĐẶC BIỆT QUAN TRỌNG: NGƯỜI DÙNG ĐÃ CHỈ ĐỊNH KỊCH BẢN THUYẾT MINH SAU ĐÂY !!!");
                sb.AppendLine($"\"{customScript}\"");
                sb.AppendLine("BẠN PHẢI BẢO LƯU NỘI DUNG VÀ THÔNG ĐIỆP CỦA KỊCH BẢN NÀY, chỉ chia tách thành các câu phụ đề (subtitles) ngắn 3-6 từ và phân cảnh (scenes) khớp với thời lượng video!");
                sb.AppendLine("============================================================");
                sb.AppendLine();
            }

            if (detectedScenes != null && detectedScenes.Count > 0)
            {
                sb.AppendLine("Các mốc phân đoạn phát hiện sơ bộ từ hình ảnh:");
                foreach (var s in detectedScenes)
                {
                    sb.AppendLine($"- [{s.Start:0.#}s - {s.End:0.#}s] {s.Description}");
                }
                sb.AppendLine();
            }

            sb.AppendLine(@"NHIỆM VỤ CỦA BẠN:
1. Tạo một Hook mở đầu bùng nổ trong 1-3 giây đầu tiên (KHÔNG mở đầu sáo rỗng như 'Chào mừng...', 'Trong video này...').
2. Viết toàn bộ bài thuyết minh 'narrationScript' bằng tiếng Việt tự nhiên, lôi cuốn theo đúng văn phong yêu cầu, các câu ngắt bằng dấu chấm rõ ràng để giọng đọc TTS phát âm chuẩn nhất.
3. Chia danh sách phụ đề (subtitles) theo câu/cụm từ ngắn (2-6 từ mỗi đoạn phụ đề để hiện to, rõ trên video dọc).
4. Xác định các TỪ KHÓA QUAN TRỌNG (highlightWords) trong mỗi câu phụ đề (như con số, hành động, tên đối tượng, tính từ đắt giá).
5. Xác định tiêu điểm quan trọng trong khung hình (focusX từ 0.0 đến 1.0; 0.5 là chính giữa) để crop khung hình 9:16 không bị mất chủ thể.
6. Đề xuất các hiệu ứng zoom nhẹ (zoomEffects: từ 1.00x đến 1.06x-1.08x) tại khoảnh khắc nhấn mạnh hoặc hook.

QUY TẮC: CHỈ trả về DUY NHẤT một JSON hợp lệ theo cấu trúc sau, không markdown, không giải thích ngoài JSON:
{
  ""stylePreset"": """ + stylePreset + @""",
  ""videoFormat"": ""9:16"",
  ""hook"": ""Câu hook mở đầu trong 1-3 giây đúng bối cảnh video..."",
  ""narrationScript"": ""Toàn bộ bài thuyết minh hấp dẫn, giàu hình tượng, đúng văn phong yêu cầu..."",
  ""musicStyle"": ""energetic"",
  ""musicVolume"": 0.12,
  ""duckingVolume"": 0.04,
  ""focusX"": 0.5,
  ""focusY"": 0.5,
  ""scenes"": [
    {
      ""id"": ""scene_01"",
      ""start"": 0.0,
      ""end"": 3.0,
      ""description"": ""Cảnh mở màn ấn tượng"",
      ""importance"": ""High"",
      ""importanceScore"": 0.95,
      ""subject"": ""Chủ thể chính"",
      ""action"": ""Hành động khởi đầu"",
      ""cameraType"": ""Zoom-in"",
      ""focusX"": 0.5,
      ""focusY"": 0.5
    }
  ],
  ""subtitles"": [
    {
      ""start"": 0.0,
      ""end"": 2.5,
      ""text"": ""Câu phụ đề 1 bám sát video"",
      ""highlightWords"": [""từ"", ""khóa""]
    }
  ]
}");

            return sb.ToString();
        }

        private static EditingPlan CreateFallbackPlan(
            double durationSeconds,
            string stylePreset,
            List<VideoScene>? detectedScenes,
            AppSettings settings,
            string topic,
            string? customScript)
        {
            return GenerateSmartOfflineEditingPlan(durationSeconds, stylePreset, detectedScenes, settings, topic, customScript);
        }

        private static EditingPlan GenerateSmartOfflineEditingPlan(
            double durationSeconds,
            string stylePreset,
            List<VideoScene>? detectedScenes,
            AppSettings settings,
            string topic,
            string? customScript = null)
        {
            if (durationSeconds <= 0) durationSeconds = 60;
            bool isKeepOriginalUncut = IsKeepOriginalUncutMode(settings.DurationMode, durationSeconds);
            bool isHighlightMode = !isKeepOriginalUncut;

            string displayTopic = !string.IsNullOrWhiteSpace(topic) ? topic : "Khoảnh khắc thú vị";

            var plan = new EditingPlan
            {
                StylePreset = stylePreset,
                VideoFormat = "9:16",
                Hook = !string.IsNullOrWhiteSpace(topic) ? $"Bạn có biết: {topic}?" : "Khám phá ngay khoảnh khắc ấn tượng này!",
                FocusX = 0.5,
                FocusY = 0.5
            };

            var scenes = new List<VideoScene>();
            if (isHighlightMode)
            {
                // Chọn lọc các phân cảnh đặc sắc nhất (Highlights) trải dài khắp video
                scenes = SelectHighlightScenes(durationSeconds, 42.0, detectedScenes, displayTopic);
            }
            else
            {
                // Giữ trọn vẹn toàn bộ video
                if (detectedScenes != null && detectedScenes.Count >= 2)
                {
                    scenes.AddRange(detectedScenes);
                }
                else
                {
                    double cur = 0;
                    int scIdx = 1;
                    while (cur < durationSeconds)
                    {
                        double end = Math.Min(cur + 5.0, durationSeconds);
                        scenes.Add(new VideoScene
                        {
                            Id = $"scene_{scIdx:D2}",
                            Start = Math.Round(cur, 2),
                            End = Math.Round(end, 2),
                            Description = $"{displayTopic} - Phân đoạn {scIdx}",
                            Importance = scIdx == 1 ? "Very High" : "High",
                            ImportanceScore = scIdx == 1 ? 1.0 : 0.85,
                            FocusX = 0.5,
                            FocusY = 0.5
                        });
                        cur = end;
                        scIdx++;
                    }
                }
            }
            plan.Scenes = scenes;

            // Subtitles, ZoomEffects, Transitions, SFX, NarrationScript
            var subs = new List<SubtitleSegment>();
            var zooms = new List<ZoomEffect>();
            var trans = new List<TransitionEvent>();
            var sfxs = new List<SoundEffectEvent>();
            var sbNarration = new StringBuilder();

            List<string> phrases = new();
            if (!string.IsNullOrWhiteSpace(customScript))
            {
                string cleanScript = TtsService.StripTimestampsAndFormatText(customScript);
                var raw = Regex.Split(cleanScript, @"(?<=[.!?\n])\s+");
                foreach (var r in raw)
                {
                    string trimmed = r.Trim();
                    if (!string.IsNullOrEmpty(trimmed)) phrases.Add(trimmed);
                }
            }

            if (phrases.Count == 0)
            {
                throw new InvalidOperationException("Chưa có nội dung kịch bản để tạo kế hoạch dựng video. Vui lòng nhập kịch bản hoặc cấu hình Gemini API Key.");
            }

            plan.Hook = phrases[0];

            // Phân bổ phụ đề và lời thoại trải đều liên tục toàn bộ thời lượng (KHÔNG BAO GIỜ để khoảng lặng 30s)
            double timelineDuration = isHighlightMode ? scenes.Sum(s => Math.Max(0, s.End - s.Start)) : durationSeconds;
            if (timelineDuration <= 0) timelineDuration = durationSeconds;

            double curSubTime = 0.2;
            int phraseIndex = 0;
            while (curSubTime + 2.0 <= timelineDuration)
            {
                double remaining = timelineDuration - curSubTime;
                double phraseLen = Math.Min(3.8, remaining - 0.4);
                if (phraseLen < 1.2) break;

                string phraseText = phrases[phraseIndex % phrases.Count];
                if (curSubTime + phraseLen + 4.5 >= timelineDuration && phrases.Count > 1)
                {
                    phraseText = phrases[^1]; // Câu chốt hạ cuối video
                }

                var words = phraseText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var highlights = new List<string>();
                if (words.Length > 0) highlights.Add(words[0]);
                if (words.Length > 2) highlights.Add(words[words.Length / 2]);

                var sub = new SubtitleSegment
                {
                    Start = Math.Round(curSubTime, 2),
                    End = Math.Round(curSubTime + phraseLen, 2),
                    Text = phraseText,
                    HighlightWords = highlights
                };
                subs.Add(sub);

                sbNarration.AppendLine(sub.Text);

                // Ngắt câu tự nhiên 0.5 giây giữa các câu, bảo đảm âm thanh liền mạch liên tục
                curSubTime = Math.Round(curSubTime + phraseLen + 0.5, 2);
                phraseIndex++;
            }

            // Dynamic Pan & Zoom xen kẽ
            for (int i = 0; i < scenes.Count; i++)
            {
                var sc = scenes[i];
                if (settings.EnableAutoZoom)
                {
                    bool zoomIn = (i % 2 == 0);
                    zooms.Add(new ZoomEffect
                    {
                        Start = Math.Round(sc.Start, 2),
                        End = Math.Round(sc.End, 2),
                        FromScale = zoomIn ? 1.0 : 1.07,
                        ToScale = zoomIn ? 1.07 : 1.0,
                        Easing = "linear"
                    });
                }

                // Transitions
                if (settings.EnableAutoTransition && i > 0)
                {
                    string tType = (i % 3 == 0) ? "DipToBlack" : ((i % 3 == 1) ? "Dissolve" : "WipeLeft");
                    trans.Add(new TransitionEvent
                    {
                        Type = tType,
                        AtTime = Math.Round(sc.Start, 2),
                        Duration = 0.3
                    });
                }

                // SFX
                if (settings.EnableSoundEffects && i > 0)
                {
                    sfxs.Add(new SoundEffectEvent
                    {
                        Start = Math.Round(sc.Start, 2),
                        Duration = 0.5,
                        Volume = 0.4,
                        Reason = "Chuyển cảnh sinh động",
                        SfxType = "whoosh"
                    });
                }
            }

            plan.Subtitles = subs;
            plan.ZoomEffects = zooms;
            plan.Transitions = trans;
            plan.SoundEffects = sfxs;
            plan.NarrationScript = sbNarration.ToString().Trim();

            return plan;
        }

        private async Task<string?> ExpandScriptToTargetDurationAsync(
            string apiUrl,
            string apiKey,
            string model,
            string currentScript,
            int currentWords,
            int targetWords,
            int minWords,
            double videoDurationSeconds,
            double speechRate,
            double wordsPerSec,
            string topic,
            CancellationToken cancellationToken)
        {
            string durStr = videoDurationSeconds.ToString("0.0", CultureInfo.InvariantCulture);
            string prompt = $@"BẠN LÀ BIÊN KỊCH VIDEO NHÉT CHỮ & VIRAL SHORTS/TIKTOK HÀNG ĐẦU.
Kịch bản thuyết minh tiếng Việt dưới đây hiện có {currentWords} từ (với tốc độ đọc giọng nói {speechRate:0.##}x tương đương ~{wordsPerSec:0.1} từ/giây, kịch bản này chỉ đọc được trong ~{currentWords / wordsPerSec:F0} giây), trong khi video dài {durStr} giây. Do đó cần mở rộng kịch bản để đạt đúng {targetWords} từ (tối thiểu {minWords} từ) nhằm giúp giọng đọc AI nói liên tục từ đầu đến sát cuối video {durStr}s, TUYỆT ĐỐI KHÔNG để lại khoảng lặng âm thanh ở cuối!

KỊCH BẢN HIỆN TẠI ({currentWords} từ):
""{currentScript}""

YÊU CẦU CHO VIDEO NHÉT CHỮ TRIỆU VIEW:
1. BẢO LƯU câu Hook mở đầu và câu chốt kết thúc của kịch bản trên.
2. VIẾT THÊM các chi tiết lôi cuốn, giải mã cơ chế, hành động vi mô chân thực vào thân bài để giữ chân người xem đến giây cuối cùng. Không giới hạn ngôn từ biểu cảm, dùng từ ngữ tự nhiên, đắt giá, cuốn hút.
3. ❌ TUYỆT ĐỐI KHÔNG DÙNG TỪ XÀM, TỪ DỊ, TỪ ÍT NGƯỜI DÙNG: Cấm từ ngữ ngớ ngẩn, dịch máy ngô nghê, hoặc từ cổ quái mà ngoài đời không ai nói!
4. BẢO LƯU câu chốt hạ đắt giá và câu CTA tương tác sáng tạo (ví dụ: 'Biến nút thích của tôi thành [đồ vật]') hoặc câu hỏi mở tò mò ở cuối kịch bản. TUYỆT ĐỐI CẤM câu xin like ăn xin nhàm chán ('nhớ like và follow kênh').
5. ĐẢM BẢO TỔNG ĐỘ DÀI TOÀN BỘ BÀI THUYẾT MINH MỚI ĐẠT TỪ {minWords} ĐẾN {targetWords + 10} TỪ.
6. CHỈ TRẢ VỀ DUY NHẤT TOÀN BỘ NỘI DUNG BÀI THUYẾT MINH MỚI (bằng tiếng Việt tự nhiên), không thêm markdown giải thích, không thêm tiêu đề.";

            bool isCustomApi = !string.IsNullOrWhiteSpace(apiUrl) && !apiUrl.Contains("googleapis.com", StringComparison.OrdinalIgnoreCase);

            var candidateModels = new List<string>();
            if (!string.IsNullOrWhiteSpace(model)) candidateModels.Add(model);

            string[] defaultFallbacks = isCustomApi
                ? new[] { "gemini-3.1-flash-lite", "fbb127bbb056c959", "9d8ca3786ebdfbea", "5bf011840784117a", "e051ce1aa80aa576" }
                : new[] { "gemini-3.1-flash-lite", "gemini-3.5-flash", "gemini-flash-lite-latest" };

            foreach (var m in defaultFallbacks)
            {
                if (!candidateModels.Contains(m)) candidateModels.Add(m);
            }

            for (int i = 0; i < candidateModels.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;
                string currentModelName = candidateModels[i];

                string url;
                string jsonBody;

                if (isCustomApi)
                {
                    url = $"{apiUrl.TrimEnd('/')}/generate";
                    var customPayload = new
                    {
                        prompt = prompt,
                        model = currentModelName
                    };
                    jsonBody = JsonSerializer.Serialize(customPayload);
                }
                else
                {
                    url = $"{BaseApiUrl}/v1beta/models/{currentModelName}:generateContent?key={apiKey}";
                    var requestPayload = new
                    {
                        contents = new[]
                        {
                            new
                            {
                                parts = new object[]
                                {
                                    new { text = prompt }
                                }
                            }
                        },
                        generationConfig = new
                        {
                            temperature = 0.7
                        }
                    };
                    jsonBody = JsonSerializer.Serialize(requestPayload);
                }

                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, url)
                    {
                        Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
                    };

                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(TimeSpan.FromSeconds(35));

                    var response = await _httpClient.SendAsync(request, cts.Token);
                    if (response.IsSuccessStatusCode)
                    {
                        string respText = await response.Content.ReadAsStringAsync(cancellationToken);
                        using var doc = JsonDocument.Parse(respText);
                        var root = doc.RootElement;
                        string? expandedText = null;

                        if (root.TryGetProperty("text", out var textProp))
                        {
                            expandedText = textProp.GetString();
                        }
                        else if (root.TryGetProperty("candidates", out var cands) && cands.GetArrayLength() > 0)
                        {
                            var cand = cands[0];
                            if (cand.TryGetProperty("content", out var cnt) &&
                                cnt.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0)
                            {
                                expandedText = parts[0].GetProperty("text").GetString();
                            }
                        }
                        else if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                        {
                            var choice = choices[0];
                            if (choice.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var cnt))
                            {
                                expandedText = cnt.GetString();
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(expandedText))
                        {
                            expandedText = TtsService.StripSocialCallToAction(expandedText);
                            return expandedText.Trim();
                        }
                    }
                }
                catch { }
            }

            return null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VideoNarrator.Helpers;
using VideoNarrator.Models;

namespace VideoNarrator.Services
{
    public interface ITopicToVideoService
    {
        Task<VideoProject> CreateProjectFromTopicAsync(
            string topic,
            string personaId,
            string aspectRatio,
            int targetSceneCount,
            string geminiApiKey,
            string geminiModel,
            AppSettings settings,
            Action<string, double>? progressCallback = null,
            CancellationToken cancellationToken = default);

        Task<string> AssembleProjectVideoAsync(
            VideoProject project,
            bool isDraftPreview,
            Action<string, double>? progressCallback = null,
            CancellationToken cancellationToken = default);
    }

    public class TopicToVideoService : ITopicToVideoService
    {
        private readonly HttpClient _httpClient;
        private readonly IFileService _fileService;
        private readonly IFfmpegService _ffmpegService;
        private readonly ITtsService _ttsService;
        private readonly ISubtitleService _subtitleService;
        private readonly IAudioMixService _audioMixService;
        private readonly IImageGenService _imageGenService;
        private readonly IProjectService _projectService;
        private readonly FfmpegFilterBuilder _filterBuilder = new();

        private const string BaseApiUrl = "https://generativelanguage.googleapis.com";

        public TopicToVideoService(
            HttpClient httpClient,
            IFileService fileService,
            IFfmpegService ffmpegService,
            ITtsService ttsService,
            ISubtitleService subtitleService,
            IAudioMixService audioMixService,
            IImageGenService imageGenService,
            IProjectService projectService)
        {
            _httpClient = httpClient;
            _fileService = fileService;
            _ffmpegService = ffmpegService;
            _ttsService = ttsService;
            _subtitleService = subtitleService;
            _audioMixService = audioMixService;
            _imageGenService = imageGenService;
            _projectService = projectService;
        }

        public async Task<VideoProject> CreateProjectFromTopicAsync(
            string topic,
            string personaId,
            string aspectRatio,
            int targetSceneCount,
            string geminiApiKey,
            string geminiModel,
            AppSettings settings,
            Action<string, double>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(topic))
            {
                throw new ArgumentException("Chủ đề video không được để trống.", nameof(topic));
            }

            var project = new VideoProject
            {
                Name = topic.Length > 40 ? topic.Substring(0, 40) + "..." : topic,
                Topic = topic,
                PersonaId = string.IsNullOrWhiteSpace(personaId) ? "mad_muse" : personaId,
                StylePreset = settings.AutoEditStylePreset ?? "TikTok Shorts"
            };
            project.ApplyAspectRatio(aspectRatio);

            progressCallback?.Invoke("AI đang lên ý tưởng và viết kịch bản phân cảnh...", 10);

            // BƯỚC 1: AI Content Director (Gemini hoặc Smart Engine)
            TopicScriptResult scriptData = await GenerateScriptBreakdownAsync(
                topic, project.PersonaId, targetSceneCount, geminiApiKey, geminiModel, settings?.GeminiApiUrl, cancellationToken);

            project.Hook = scriptData.Hook;
            project.FullScript = scriptData.FullNarration;

            string projectAssetsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VideoNarrator", "projects", project.Id, "assets");
            Directory.CreateDirectory(projectAssetsDir);

            // BƯỚC 2: Tải/Tạo hình ảnh AI cho từng phân cảnh
            progressCallback?.Invoke("AI đang vẽ và tạo hình ảnh điện ảnh cho từng cảnh...", 30);
            double progressPerScene = 40.0 / Math.Max(1, scriptData.Scenes.Count);

            for (int i = 0; i < scriptData.Scenes.Count; i++)
            {
                var s = scriptData.Scenes[i];
                double currentPct = 30 + (i * progressPerScene);
                progressCallback?.Invoke($"Đang tạo hình ảnh Cảnh {i + 1}/{scriptData.Scenes.Count}: {s.VisualPrompt}...", currentPct);

                string imgPath = await _imageGenService.GenerateImageForSceneAsync(
                    s.VisualPrompt,
                    project.OutputWidth,
                    project.OutputHeight,
                    projectAssetsDir,
                    i,
                    cancellationToken);

                string[] zoomDirs = { "zoom-in", "zoom-out", "pan-left", "pan-right" };
                string zoom = zoomDirs[i % zoomDirs.Length];

                project.Scenes.Add(new ProjectScene
                {
                    Index = i,
                    Narration = s.Narration,
                    VisualPrompt = s.VisualPrompt,
                    MediaPath = imgPath,
                    IsVideoClip = false,
                    EstimatedDuration = Math.Max(3.0, s.Narration.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length / 3.4),
                    TransitionType = "crossfade",
                    ZoomDirection = zoom
                });
            }

            // BƯỚC 3: Dựng bản xem trước tốc độ cao (Proxy Preview)
            progressCallback?.Invoke("Đang tạo giọng đọc TTS và đồng bộ phụ đề...", 75);
            string previewVideoPath = await AssembleProjectVideoAsync(project, isDraftPreview: true, progressCallback, cancellationToken);
            project.ProxyVideoPath = previewVideoPath;

            // Lưu dự án
            await _projectService.SaveProjectAsync(project);

            progressCallback?.Invoke("Hoàn thành tạo video từ chủ đề!", 100);
            return project;
        }

        private async Task<TopicScriptResult> GenerateScriptBreakdownAsync(
            string topic,
            string personaId,
            int sceneCount,
            string apiKey,
            string model,
            string? apiUrl,
            CancellationToken cancellationToken)
        {
            string baseApi = !string.IsNullOrWhiteSpace(apiUrl) ? apiUrl.Trim() : BaseApiUrl;
            bool isCustomApi = !baseApi.Contains("googleapis.com", StringComparison.OrdinalIgnoreCase);

            if (!isCustomApi && string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("Chưa cấu hình Gemini API Key. Vui lòng mở Cài Đặt (biểu tượng bánh răng) và nhập Gemini API Key để tạo kịch bản theo chủ đề.");
            }

            string targetModel = string.IsNullOrWhiteSpace(model) ? "gemini-3.1-flash-lite" : model;
            string prompt = $@"Bạn là bậc thầy biên kịch 'Video Nhét Chữ' và kể chuyện viral triệu view trên TikTok, Reels, Shorts theo chuẩn kênh 'Thế Giới Có Gì Hay' và Zack D. Films.
Hãy tạo kịch bản video hoàn chỉnh dựa trên CHỦ ĐỀ sau:
""{topic}""

QUY TẮC NỘI DUNG & GIỮ CHÂN NGƯỜI XEM (BẮT BUỘC):
1. CẤU TRÚC 4 BƯỚC VÀNG GIỮ CHÂN NGƯỜI XEM:
   - Hook trực diện (0-2s): Đập thẳng vào vấn đề hoặc hiện tượng bất thường ('Đây là cách...', 'Tại sao...'). TUYỆT ĐỐI CẤM mở đầu bằng 'Mới nhìn cứ ngỡ...', 'Trong video này...'.
   - Bối cảnh & Quy luật thực tế (2-5s): Đưa ra luật lệ, hoàn cảnh hoặc áp lực thực tế có số liệu cụ thể.
   - Bóc tách cơ chế & Hành vi vi mô: Miêu tả chi tiết công cụ, mánh khóe, từng cử chỉ chuyển động sống động.
   - Kết thúc & CTA sáng tạo: Cú chốt bất ngờ hoặc CTA liên tưởng ('Biến nút thích của tôi thành [đồ vật]'). CẤM câu like ăn xin sáo rỗng.
2. Tự do dùng từ: Thoải mái dùng từ ngữ phong phú, biểu cảm mạnh, không giới hạn khuôn mẫu; ngôn từ phải tự nhiên, đắt giá, hợp bối cảnh đời thực.
3. TUYỆT ĐỐI KHÔNG dùng từ ngữ xàm, từ ngữ sáo rỗng ('đẳng cấp chuyên nghiệp', 'tính toán tỉ mỉ'), từ hiếm gặp ít người dùng hoặc câu cú dịch thô vụng về.
4. Chia thành đúng {sceneCount} phân cảnh (Scenes).
5. Mỗi phân cảnh có:
   - ""narration"": Lời thoại thuyết minh tiếng Việt tự nhiên, giàu cảm xúc, dứt khoát, không chứa bất kỳ ghi chú đạo diễn nào.
   - ""visualPrompt"": Câu lệnh tiếng Anh chi tiết, mô tả bối cảnh thị giác để sinh ảnh AI (ví dụ: 'cinematic photo of ancient temple hidden in rainforest, morning sunbeams, 8k, photorealistic').
6. Trả về DUY NHẤT một khối JSON hợp lệ theo định dạng sau (không viết thêm markdown hay giải thích):
{{
  ""hook"": ""Câu hook mở đầu..."",
  ""scenes"": [
    {{
      ""narration"": ""Lời thoại cảnh 1..."",
      ""visualPrompt"": ""cinematic English visual prompt...""
    }}
  ]
}}";

            try
            {
                string url;
                string jsonBody;

                if (isCustomApi)
                {
                    url = $"{baseApi.TrimEnd('/')}/generate";
                    var customPayload = new
                    {
                        prompt = prompt,
                        model = targetModel
                    };
                    jsonBody = JsonSerializer.Serialize(customPayload);
                }
                else
                {
                    url = $"{BaseApiUrl}/v1beta/models/{targetModel}:generateContent?key={apiKey}";
                    var body = new
                    {
                        contents = new[] { new { parts = new[] { new { text = prompt } } } },
                        generationConfig = new { temperature = 0.7, maxOutputTokens = 2048 }
                    };
                    jsonBody = JsonSerializer.Serialize(body);
                }

                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                using var res = await _httpClient.SendAsync(req, cancellationToken);
                if (res.IsSuccessStatusCode)
                {
                    string jsonResp = await res.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(jsonResp);
                    var root = doc.RootElement;
                    string? rawText = null;

                    if (root.TryGetProperty("text", out var textProp))
                    {
                        rawText = textProp.GetString();
                    }
                    else if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                    {
                        var candidate = candidates[0];
                        if (candidate.TryGetProperty("content", out var content) &&
                            content.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0)
                        {
                            rawText = parts[0].GetProperty("text").GetString();
                        }
                    }
                    else if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                    {
                        var choice = choices[0];
                        if (choice.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var cnt))
                        {
                            rawText = cnt.GetString();
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(rawText))
                    {
                        var parsed = JsonHelper.DeserializeSafe<TopicScriptResult>(rawText);
                        if (parsed != null && parsed.Scenes != null && parsed.Scenes.Count > 0)
                        {
                            return parsed;
                        }
                    }
                }
                else
                {
                    string errContent = await res.Content.ReadAsStringAsync(cancellationToken);
                    _fileService.Log($"Lỗi gọi AI ({res.StatusCode}): {errContent}", "ERROR");
                    throw new InvalidOperationException($"Lỗi gọi AI ({res.StatusCode}): {errContent}");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _fileService.Log($"Lỗi gọi AI cho chủ đề '{topic}': {ex.Message}", "ERROR", ex);
                throw new InvalidOperationException($"Lỗi tạo kịch bản từ AI: {ex.Message}", ex);
            }

            throw new InvalidOperationException($"Không thể tạo kịch bản từ AI cho chủ đề '{topic}'.");
        }

        public async Task<string> AssembleProjectVideoAsync(
            VideoProject project,
            bool isDraftPreview,
            Action<string, double>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            string tempDir = _fileService.CreateTempDirectory();
            string outputVideoPath = isDraftPreview
                ? Path.Combine(tempDir, $"preview_{project.Id}.mp4")
                : Path.Combine(AppSettings.GetDefaultOutputDirectory(), $"Shorts_{SanitizeFileName(project.Name)}_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");

            // 1. Tạo Giọng đọc TTS cho toàn bộ kịch bản
            progressCallback?.Invoke("Đang tạo giọng đọc thuyết minh TTS...", 80);
            string fullNarration = string.Join(" ", project.Scenes.Select(s => s.Narration));
            if (!string.IsNullOrWhiteSpace(project.Hook) && !fullNarration.StartsWith(project.Hook))
            {
                fullNarration = $"{project.Hook}. {fullNarration}";
            }

            string rawAudioPath = Path.Combine(tempDir, "voice.mp3");
            var ttsSettings = new TtsSettings
            {
                Provider = project.AudioSettings.TtsProvider,
                Voice = project.AudioSettings.TtsVoice,
                Speed = project.AudioSettings.SpeechRate,
                Pitch = project.AudioSettings.SpeechPitch
            };

            var ttsResult = await _ttsService.GenerateSpeechWithTimestampsAsync(fullNarration, ttsSettings, rawAudioPath, cancellationToken);
            double totalAudioDuration = await _ffmpegService.GetMediaDurationAsync(rawAudioPath, cancellationToken);
            if (totalAudioDuration <= 0) totalAudioDuration = ttsResult.DurationSeconds > 0 ? ttsResult.DurationSeconds : 20.0;

            // Phân bổ thời lượng cho từng cảnh ăn khớp với giọng nói
            double perSceneDuration = totalAudioDuration / Math.Max(1, project.Scenes.Count);
            foreach (var s in project.Scenes)
            {
                s.ActualDuration = Math.Round(perSceneDuration, 2);
            }

            // 2. Tạo phụ đề ASS Highlight
            progressCallback?.Invoke("Đang đồng bộ phụ đề ASS...", 85);
            var subSegments = (ttsResult.WordTimestamps != null && ttsResult.WordTimestamps.Count > 0)
                ? _subtitleService.BuildSubtitlesFromWordTimestamps(ttsResult.WordTimestamps, totalAudioDuration)
                : _subtitleService.SplitNarrationToSubtitles(fullNarration, totalAudioDuration);

            string assPath = Path.Combine(tempDir, "subtitles.ass");
            _subtitleService.GenerateAssSubtitleFile(
                subSegments,
                project.SubtitleSettings.PresetName,
                assPath,
                project.OutputWidth,
                project.OutputHeight,
                project.SubtitleSettings.EnableKeywordHighlight);

            // 3. Render từng scene từ ảnh tĩnh kèm hiệu ứng Ken Burns Pan/Zoom
            progressCallback?.Invoke("Đang tạo chuyển động Pan/Zoom trên từng cảnh...", 88);
            var sceneVideoClips = new List<string>();

            int renderW = isDraftPreview ? (project.OutputWidth / 2) : project.OutputWidth;
            int renderH = isDraftPreview ? (project.OutputHeight / 2) : project.OutputHeight;
            if (renderW % 2 != 0) renderW++;
            if (renderH % 2 != 0) renderH++;

            for (int i = 0; i < project.Scenes.Count; i++)
            {
                var sc = project.Scenes[i];
                string clipPath = Path.Combine(tempDir, $"clip_{i:D2}.mp4");
                await RenderImageToKenBurnsClipAsync(sc.MediaPath, clipPath, renderW, renderH, sc.ActualDuration, sc.ZoomDirection, cancellationToken);
                if (File.Exists(clipPath))
                {
                    sceneVideoClips.Add(clipPath);
                }
            }

            // 4. Nối các clip (Concat) và Burn phụ đề
            progressCallback?.Invoke("Đang ghép các cảnh và hòa trộn âm thanh...", 92);
            string concatTxtPath = Path.Combine(tempDir, "concat.txt");
            var sbConcat = new StringBuilder();
            foreach (var clip in sceneVideoClips)
            {
                sbConcat.AppendLine($"file '{clip.Replace('\\', '/')}'");
            }
            await File.WriteAllTextAsync(concatTxtPath, sbConcat.ToString(), cancellationToken);

            string joinedVideoPath = Path.Combine(tempDir, "joined.mp4");
            string qConcatTxt = ProcessHelper.QuoteArgument(concatTxtPath);
            string qJoined = ProcessHelper.QuoteArgument(joinedVideoPath);

            string concatArgs = $"-y -f concat -safe 0 -i {qConcatTxt} -c:v copy {qJoined}";
            await ProcessHelper.RunProcessAsync(_ffmpegService.FfmpegPath, concatArgs, cancellationToken);

            // 5. Hòa trộn Nhạc nền Ducking + Voice + Subtitles ra MP4 hoàn thiện
            string qVideo = ProcessHelper.QuoteArgument(File.Exists(joinedVideoPath) ? joinedVideoPath : sceneVideoClips[0]);
            string qAudio = ProcessHelper.QuoteArgument(rawAudioPath);
            string qOut = ProcessHelper.QuoteArgument(outputVideoPath);
            string subFilter = _filterBuilder.BuildSubtitleFilter(assPath);

            string filterComplex = string.IsNullOrEmpty(subFilter) ? "" : $"-vf \"{subFilter}\"";
            string preset = isDraftPreview ? "ultrafast" : "medium";
            int crf = isDraftPreview ? 28 : 19;

            string finalArgs = $"-y -i {qVideo} -i {qAudio} {filterComplex} -map 0:v:0 -map 1:a:0 -c:v libx264 -preset {preset} -crf {crf} -pix_fmt yuv420p -c:a aac -b:a 192k -t {totalAudioDuration.ToString("0.000", CultureInfo.InvariantCulture)} {qOut}";

            _fileService.Log($"Xuất video hoàn chỉnh: ffmpeg {finalArgs}");
            await ProcessHelper.RunProcessAsync(_ffmpegService.FfmpegPath, finalArgs, cancellationToken);

            if (!File.Exists(outputVideoPath))
            {
                throw new InvalidOperationException("Không thể hoàn tất quá trình render video.");
            }

            _fileService.Log($"Xuất video thành công: {outputVideoPath}");
            return outputVideoPath;
        }

        private async Task RenderImageToKenBurnsClipAsync(
            string imagePath,
            string outputPath,
            int width,
            int height,
            double duration,
            string zoomDirection,
            CancellationToken cancellationToken)
        {
            if (!File.Exists(imagePath)) return;

            string qImg = ProcessHelper.QuoteArgument(imagePath);
            string qOut = ProcessHelper.QuoteArgument(outputPath);
            int fps = 25;
            int totalFrames = (int)Math.Max(25, Math.Round(duration * fps));
            string durStr = duration.ToString("0.000", CultureInfo.InvariantCulture);

            // Smooth Ken Burns pan/zoom filter
            string zoomExpr = zoomDirection switch
            {
                "zoom-out" => "zoom='max(1.15-0.0015*on,1.0)':x='(iw-iw/zoom)/2':y='(ih-ih/zoom)/2'",
                "pan-left" => "zoom=1.08:x='(iw-iw/zoom)*(1-on/" + totalFrames + ")':y='(ih-ih/zoom)/2'",
                "pan-right" => "zoom=1.08:x='(iw-iw/zoom)*(on/" + totalFrames + ")':y='(ih-ih/zoom)/2'",
                _ => "zoom='min(zoom+0.0015,1.15)':x='(iw-iw/zoom)/2':y='(ih-ih/zoom)/2'" // zoom-in
            };

            string filter = $"scale={width}:{height}:force_original_aspect_ratio=increase,crop={width}:{height},zoompan={zoomExpr}:d={totalFrames}:s={width}x{height}:fps={fps}";
            string args = $"-y -loop 1 -i {qImg} -vf \"{filter}\" -t {durStr} -c:v libx264 -pix_fmt yuv420p -preset ultrafast {qOut}";

            await ProcessHelper.RunProcessAsync(_ffmpegService.FfmpegPath, args, cancellationToken);
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            return Regex.Replace(clean, @"\s+", "_");
        }
    }

    public class TopicScriptResult
    {
        [JsonPropertyName("hook")]
        public string Hook { get; set; } = string.Empty;

        [JsonPropertyName("scenes")]
        public List<TopicSceneItem> Scenes { get; set; } = new();

        [JsonIgnore]
        public string FullNarration => string.Join(" ", Scenes?.Select(s => s.Narration) ?? Array.Empty<string>());
    }

    public class TopicSceneItem
    {
        [JsonPropertyName("narration")]
        public string Narration { get; set; } = string.Empty;

        [JsonPropertyName("visualPrompt")]
        public string VisualPrompt { get; set; } = string.Empty;
    }
}

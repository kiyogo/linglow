using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VideoNarrator.Helpers;
using VideoNarrator.Models;

namespace VideoNarrator.Services
{
    public interface IGeminiService
    {
        Task<bool> TestApiKeyAsync(string apiKey, string model, string? apiUrl = null, CancellationToken cancellationToken = default);
        Task<VideoAnalysisResult> AnalyzeVideoAsync(
            string apiKey,
            string model,
            string videoFilePath,
            double videoDurationSeconds,
            string? apiUrl = null,
            Action<string, double>? progressCallback = null,
            CancellationToken cancellationToken = default);

        Task<(string fileUri, string fileName)> UploadVideoFileAsync(
            string apiKey,
            string filePath,
            string mimeType,
            CancellationToken cancellationToken);

        Task WaitForFileActiveStateAsync(
            string apiKey,
            string remoteFileName,
            Action<string, double>? progressCallback,
            CancellationToken cancellationToken);

        string GetMimeType(string filePath);
    }

    public class GeminiService : IGeminiService
    {
        private readonly HttpClient _httpClient;
        private readonly IFileService _fileService;
        private const string BaseApiUrl = "https://generativelanguage.googleapis.com";

        public GeminiService(HttpClient httpClient, IFileService fileService)
        {
            _httpClient = httpClient;
            _fileService = fileService;
            if (_httpClient.Timeout.TotalSeconds < 180)
            {
                _httpClient.Timeout = TimeSpan.FromMinutes(3);
            }
        }

        public async Task<bool> TestApiKeyAsync(string apiKey, string model, string? apiUrl = null, CancellationToken cancellationToken = default)
        {
            bool isCustomApi = !string.IsNullOrWhiteSpace(apiUrl) && !apiUrl.Contains("googleapis.com", StringComparison.OrdinalIgnoreCase);

            if (isCustomApi)
            {
                try
                {
                    using var pingReq = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                    using var pingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    pingCts.CancelAfter(TimeSpan.FromSeconds(5));
                    using var pingResp = await _httpClient.SendAsync(pingReq, pingCts.Token);
                    if (pingResp.IsSuccessStatusCode)
                    {
                        return true;
                    }
                }
                catch { }

                try
                {
                    string genUrl = $"{apiUrl!.TrimEnd('/')}/generate";
                    string testModel = !string.IsNullOrWhiteSpace(model) ? model : "gemini-3.1-flash-lite";
                    var testBody = new { prompt = "Xin chào", model = testModel };
                    using var postReq = new HttpRequestMessage(HttpMethod.Post, genUrl)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(testBody), Encoding.UTF8, "application/json")
                    };
                    using var postCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    postCts.CancelAfter(TimeSpan.FromSeconds(10));
                    using var postResp = await _httpClient.SendAsync(postReq, postCts.Token);
                    if (postResp.IsSuccessStatusCode)
                    {
                        return true;
                    }
                }
                catch { }

                return false;
            }

            if (string.IsNullOrWhiteSpace(apiKey)) return false;

            var modelsToTest = new List<string>();
            if (!string.IsNullOrWhiteSpace(model)) modelsToTest.Add(model);
            foreach (var m in new[] { "gemini-3.1-flash-lite", "gemini-3.5-flash", "gemini-flash-lite-latest", "gemini-flash-latest" })
            {
                if (!modelsToTest.Contains(m)) modelsToTest.Add(m);
            }

            foreach (var currentModel in modelsToTest)
            {
                try
                {
                    string url = $"{BaseApiUrl}/v1beta/models/{currentModel}:generateContent?key={apiKey}";

                    var requestBody = new
                    {
                        contents = new[]
                        {
                            new
                            {
                                parts = new[]
                                {
                                    new { text = "Trả lời ngắn gọn chữ 'OK'." }
                                }
                            }
                        }
                    };

                    using var request = new HttpRequestMessage(HttpMethod.Post, url);
                    request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

                    using var response = await _httpClient.SendAsync(request, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        return true;
                    }

                    int code = (int)response.StatusCode;
                    if (code == 400 || code == 403)
                    {
                        return false; // API key sai hoàn toàn
                    }
                }
                catch
                {
                    // Thử model tiếp theo
                }
            }

            return false;
        }

        public async Task<VideoAnalysisResult> AnalyzeVideoAsync(
            string apiKey,
            string model,
            string videoFilePath,
            double videoDurationSeconds,
            string? apiUrl = null,
            Action<string, double>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            bool isCustomApi = !string.IsNullOrWhiteSpace(apiUrl) && !apiUrl.Contains("googleapis.com", StringComparison.OrdinalIgnoreCase);

            if (!isCustomApi && string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("Chưa cấu hình Gemini API Key. Vui lòng mở Cài Đặt (biểu tượng bánh răng) và nhập Gemini API Key để AI phân tích video.");
            }

            if (!File.Exists(videoFilePath))
            {
                throw new FileNotFoundException($"Không tìm thấy tệp video: {videoFilePath}");
            }

            string targetModel = string.IsNullOrWhiteSpace(model) ? (isCustomApi ? "gemini-3.1-flash-lite" : "gemini-3.5-flash") : model;
            string fileName = Path.GetFileName(videoFilePath);
            long fileLength = new FileInfo(videoFilePath).Length;
            string mimeType = GetMimeType(videoFilePath);

            _fileService.Log($"Bắt đầu phân tích video '{fileName}' ({fileLength} bytes) bằng model {targetModel} (API: {(isCustomApi ? apiUrl : "Google Cloud")})");

            string? fileUri = null;
            string? remoteFileName = null;

            // BƯỚC 1: Tải video lên Gemini File API (chỉ hỗ trợ trên Google Cloud chính thức)
            if (!isCustomApi && File.Exists(videoFilePath))
            {
                progressCallback?.Invoke("Đang tải video lên Gemini...", 15);
                try
                {
                    (fileUri, remoteFileName) = await UploadVideoFileAsync(apiKey, videoFilePath, mimeType, cancellationToken);
                    // BƯỚC 2: Chờ Gemini xử lý video xong
                    progressCallback?.Invoke("Đang chờ Gemini xử lý video...", 30);
                    await WaitForFileActiveStateAsync(apiKey, remoteFileName, progressCallback, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _fileService.Log($"Tải hoặc chuẩn bị video trên Gemini gặp sự cố ({ex.Message}). Tiếp tục phân tích theo kịch bản thông minh...", "WARN");
                }
            }

            // BƯỚC 3: Gửi prompt phân tích và sinh kịch bản
            progressCallback?.Invoke("Đang phân tích video và tạo kịch bản thuyết minh...", 50);

            string systemPrompt = BuildPrompt(videoDurationSeconds);
            VideoAnalysisResult result;
            try
            {
                result = await GenerateScriptWithGeminiAsync(apiKey, targetModel, fileUri, mimeType, systemPrompt, apiUrl, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _fileService.Log($"Phân tích AI trực tuyến gặp lỗi: {ex.Message}", "ERROR", ex);
                throw new InvalidOperationException($"Lỗi phân tích AI: {ex.Message}. Vui lòng kiểm tra lại cấu hình Model hoặc kết nối API.", ex);
            }

            // BƯỚC 4: Validate và tối ưu segments
            progressCallback?.Invoke("Đang hoàn thiện kịch bản...", 65);
            ValidateAndNormalizeSegments(result, videoDurationSeconds);

            _fileService.Log($"Hoàn tất phân tích video. Tổng số đoạn kịch bản: {result.Segments.Count}");
            return result;
        }

        public async Task<(string fileUri, string fileName)> UploadVideoFileAsync(
            string apiKey,
            string filePath,
            string mimeType,
            CancellationToken cancellationToken)
        {
            long fileLength = new FileInfo(filePath).Length;
            string displayName = Path.GetFileName(filePath);

            // Khởi tạo resumable upload
            string initUrl = $"{BaseApiUrl}/upload/v1beta/files?key={apiKey}";
            using var initRequest = new HttpRequestMessage(HttpMethod.Post, initUrl);
            initRequest.Headers.Add("X-Goog-Upload-Protocol", "resumable");
            initRequest.Headers.Add("X-Goog-Upload-Command", "start");
            initRequest.Headers.Add("X-Goog-Upload-Header-Content-Length", fileLength.ToString(CultureInfo.InvariantCulture));
            initRequest.Headers.Add("X-Goog-Upload-Header-Content-Type", mimeType);

            var initPayload = new
            {
                file = new
                {
                    display_name = displayName
                }
            };
            initRequest.Content = new StringContent(JsonSerializer.Serialize(initPayload), Encoding.UTF8, "application/json");

            using var initResponse = await _httpClient.SendAsync(initRequest, cancellationToken);
            if (!initResponse.IsSuccessStatusCode)
            {
                string errorBody = await initResponse.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"Không thể khởi tạo upload video: HTTP {initResponse.StatusCode} - {errorBody}");
            }

            // Lấy upload URL từ header
            string? uploadUrl = null;
            if (initResponse.Headers.TryGetValues("x-goog-upload-url", out var values))
            {
                uploadUrl = values.FirstOrDefault();
            }

            if (string.IsNullOrEmpty(uploadUrl))
            {
                throw new InvalidOperationException("Không nhận được upload URL từ Gemini File API.");
            }

            // Bắt đầu truyền dữ liệu qua Stream (không load toàn bộ file vào RAM)
            await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
            uploadRequest.Headers.Add("X-Goog-Upload-Offset", "0");
            uploadRequest.Headers.Add("X-Goog-Upload-Command", "upload, finalize");

            var streamContent = new StreamContent(fileStream);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
            streamContent.Headers.ContentLength = fileLength;
            uploadRequest.Content = streamContent;

            using var uploadResponse = await _httpClient.SendAsync(uploadRequest, cancellationToken);
            string responseJson = await uploadResponse.Content.ReadAsStringAsync(cancellationToken);

            if (!uploadResponse.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Lỗi tải dữ liệu video lên Gemini: HTTP {uploadResponse.StatusCode} - {responseJson}");
            }

            using var doc = JsonDocument.Parse(responseJson);
            var fileElem = doc.RootElement.GetProperty("file");
            string remoteName = fileElem.GetProperty("name").GetString() ?? "";
            string uri = fileElem.GetProperty("uri").GetString() ?? "";

            return (uri, remoteName);
        }

        public async Task WaitForFileActiveStateAsync(
            string apiKey,
            string remoteFileName,
            Action<string, double>? progressCallback,
            CancellationToken cancellationToken)
        {
            string url = $"{BaseApiUrl}/v1beta/{remoteFileName}?key={apiKey}";
            int attempts = 0;
            const int maxAttempts = 60; // tối đa ~3-4 phút

            while (attempts < maxAttempts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempts++;

                using var response = await _httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    await Task.Delay(3000, cancellationToken);
                    continue;
                }

                string json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                string state = "ACTIVE";
                if (doc.RootElement.TryGetProperty("state", out var stateElem))
                {
                    state = stateElem.GetString() ?? "ACTIVE";
                }

                if (state.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (state.Equals("FAILED", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Gemini thông báo xử lý tệp video thất bại (FAILED).");
                }

                progressCallback?.Invoke($"Đang xử lý video trên Gemini ({attempts * 3}s)...", 30 + (attempts * 0.3));
                await Task.Delay(3000, cancellationToken);
            }
        }

        private async Task<VideoAnalysisResult> GenerateScriptWithGeminiAsync(
            string apiKey,
            string model,
            string? fileUri,
            string mimeType,
            string prompt,
            string? apiUrl,
            CancellationToken cancellationToken)
        {
            bool isCustomApi = !string.IsNullOrWhiteSpace(apiUrl) && !apiUrl.Contains("googleapis.com", StringComparison.OrdinalIgnoreCase);

            var candidateModels = new List<string>();
            if (!string.IsNullOrWhiteSpace(model)) candidateModels.Add(model);

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

            bool includeVideo = !isCustomApi && !string.IsNullOrEmpty(fileUri);
            string responseText = "";
            bool success = false;

            for (int i = 0; i < candidateModels.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;
                string curModel = candidateModels[i];

                string url;
                string jsonBody;

                if (isCustomApi)
                {
                    url = $"{apiUrl!.TrimEnd('/')}/generate";
                    var customPayload = new
                    {
                        prompt = prompt,
                        model = curModel
                    };
                    jsonBody = JsonSerializer.Serialize(customPayload);
                }
                else
                {
                    url = $"{BaseApiUrl}/v1beta/models/{curModel}:generateContent?key={apiKey}";
                    var partsList = new List<object>();
                    if (includeVideo && !string.IsNullOrEmpty(fileUri))
                    {
                        partsList.Add(new
                        {
                            file_data = new
                            {
                                mime_type = mimeType,
                                file_uri = fileUri
                            }
                        });
                    }
                    partsList.Add(new { text = prompt });

                    var requestPayload = new
                    {
                        contents = new[]
                        {
                            new { parts = partsList.ToArray() }
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
                    int timeoutSeconds = includeVideo ? 65 : 45;
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                    using var request = new HttpRequestMessage(HttpMethod.Post, url);
                    request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                    using var response = await _httpClient.SendAsync(request, timeoutCts.Token);
                    responseText = await response.Content.ReadAsStringAsync(timeoutCts.Token);

                    if (response.IsSuccessStatusCode)
                    {
                        success = true;
                        break;
                    }

                    int code = (int)response.StatusCode;
                    if (includeVideo) includeVideo = false;

                    if ((code == 503 || code == 429 || code == 404 || code == 500) && i < candidateModels.Count - 1)
                    {
                        await Task.Delay(800, cancellationToken);
                        continue;
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    if (includeVideo) includeVideo = false;
                    if (i < candidateModels.Count - 1)
                    {
                        await Task.Delay(500, cancellationToken);
                        continue;
                    }
                }
                catch (Exception)
                {
                    if (includeVideo) includeVideo = false;
                }
            }

            if (!success)
            {
                throw new InvalidOperationException($"Không thể gọi AI GenerateContent: {responseText}");
            }

            using var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;
            string? rawAiOutput = null;

            if (root.TryGetProperty("text", out var textProp))
            {
                rawAiOutput = textProp.GetString();
            }
            else if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
            {
                var candidate = candidates[0];
                if (candidate.TryGetProperty("content", out var content) &&
                    content.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0)
                {
                    rawAiOutput = parts[0].GetProperty("text").GetString();
                }
            }
            else if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                if (choice.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var cnt))
                {
                    rawAiOutput = cnt.GetString();
                }
            }

            if (!string.IsNullOrWhiteSpace(rawAiOutput))
            {
                var result = JsonHelper.DeserializeSafe<VideoAnalysisResult>(rawAiOutput);
                if (result != null)
                {
                    return result;
                }
            }

            throw new InvalidOperationException("AI không trả về nội dung kịch bản hợp lệ.");
        }

        private string BuildPrompt(double durationSeconds)
        {
            return $@"Bạn là Bậc Thầy Biên Kịch Video Nhét Chữ và Kể Chuyện Viral (TikTok / Shorts / Reels) triệu view hàng đầu Việt Nam theo chuẩn kênh 'Thế Giới Có Gì Hay' và Zack D. Films.
Thời lượng toàn bộ video này là {durationSeconds.ToString("0.#", CultureInfo.InvariantCulture)} giây.

QUY TẮC KỊCH BẢN THUYẾT MINH VIRAL TRIỆU VIEW (CUỐN HÚT, BẮT TAI, TUYỆT ĐỐI KHÔNG NHẠT):
1. CẤU TRÚC 4 BƯỚC VÀNG GIỮ CHÂN NGƯỜI XEM (HIGH-RETENTION VIRAL FORMULA):
   - BƯỚC 1 - HOOK TRỰC DIỆN (0-2s): Đập thẳng vào vấn đề hoặc hành vi dị thường nhất trong video ('Đây là cách một số người gian lận trên...', 'Tại sao người ta lại đổ hàng triệu...', 'Điều gì sẽ xảy ra nếu...'). Tuyệt đối CẤM mở đầu vòng vo ('Mới nhìn cứ ngỡ...'), CẤM chào hỏi ('Chào mừng các bạn...'), CẤM triết lý sáo rỗng.
   - BƯỚC 2 - BỐI CẢNH & QUY LUẬT THỰC TẾ (2-5s): Nêu lý do, quy định hoặc áp lực thực tế dẫn đến tình huống trong video (ngắn gọn, logic, có số liệu thực tế như 'Sau khi đánh bóng bạn chỉ có khoảng 3 phút để tìm bóng...').
   - BƯỚC 3 - BÓC TÁCH CƠ CHẾ & HÀNH ĐỘNG VI MÔ (5-12s): Miêu tả chính xác công cụ, mánh khóe, từng cử chỉ và chuyển động mà mắt người xem đang thấy trên màn hình. Cho người xem cảm giác 'A ha, hóa ra là thế!'.
   - BƯỚC 4 - CÚ CHỐT HẠ & CTA TƯƠNG TÁC SÁNG TẠO: Kết thúc bằng câu kêu gọi hành động sáng tạo liên tưởng đến đồ vật trong video (Ví dụ: 'Biến nút thích của tôi thành bóng golf', 'Biến nút thích thành chiếc chìa khóa') hoặc câu hỏi mở tò mò. TUYỆT ĐỐI CẤM câu xin like ăn xin nhàm chán ('nhớ like và subscribe cho mình nhé').
2. BỘ LỌC CẤM SÁO RỖNG GÂY NHẠT:
   - ❌ TUYỆT ĐỐI CẤM: 'Mới nhìn cứ ngỡ...', 'Trong video này...', 'Chào mừng các bạn...', 'Bạn sẽ không tin...'.
   - ❌ CẤM các tính từ giả tạo: 'đẳng cấp chuyên nghiệp', 'tính toán tỉ mỉ', 'vô cùng tuyệt vời', 'khoảnh khắc kỳ diệu'.
3. MẬT ĐỘ THÔNG TIN CAO: Mỗi câu nói đều phải cung cấp một chi tiết mới, nhịp câu gãy gọn, giàu hình ảnh thị giác và hành vi đời thực.
4. PHÁT ÂM AI CHUẨN XÁC: Viết 'F-A' thay vì 'FA'. Dùng dấu chấm câu rõ ràng để AI TTS đọc có nhịp thở và nhấn nhá truyền cảm.
5. Chia kịch bản thành các phân đoạn (segments) với mốc thời gian start và end (tính bằng giây) phủ trọn thời lượng {durationSeconds.ToString("0.#", CultureInfo.InvariantCulture)} giây của video.

BẮT BUỘC: Bạn CHỈ ĐƯỢC trả về một đối tượng JSON hợp lệ duy nhất theo định dạng sau:
{{
  ""title"": ""Tiêu đề giật tít bắt trend tóm tắt video"",
  ""summary"": ""Tóm tắt video trong 1 câu ngắn cuốn hút"",
  ""segments"": [
    {{
      ""start"": 0.0,
      ""end"": 4.0,
      ""text"": ""Đây là cách một số người gian lận trên sân golf...""
    }}
  ]
}}";
        }

        private void ValidateAndNormalizeSegments(VideoAnalysisResult result, double videoDurationSeconds)
        {
            if (!string.IsNullOrWhiteSpace(result.Title))
            {
                result.Title = TtsService.NormalizeTextForVietnameseTts(result.Title);
            }
            if (!string.IsNullOrWhiteSpace(result.Summary))
            {
                result.Summary = TtsService.NormalizeTextForVietnameseTts(result.Summary);
            }

            if (result.Segments == null || result.Segments.Count == 0)
            {
                result.Segments = new List<ScriptSegment>
                {
                    new()
                    {
                        Start = 0,
                        End = Math.Max(1.0, videoDurationSeconds),
                        Text = string.IsNullOrWhiteSpace(result.Summary) ? "Không có nội dung thuyết minh." : result.Summary
                    }
                };
                return;
            }

            var validSegments = new List<ScriptSegment>();
            for (int i = 0; i < result.Segments.Count; i++)
            {
                var seg = result.Segments[i];
                if (string.IsNullOrWhiteSpace(seg.Text)) continue;

                seg.Text = TtsService.NormalizeTextForVietnameseTts(seg.Text.Trim());
                seg.Start = Math.Max(0, seg.Start);
                seg.End = Math.Max(seg.Start + 0.5, seg.End);

                if (videoDurationSeconds > 0 && seg.End > videoDurationSeconds)
                {
                    seg.End = videoDurationSeconds;
                }

                if (seg.End > seg.Start)
                {
                    validSegments.Add(seg);
                }
            }

            if (validSegments.Count == 0)
            {
                validSegments.Add(new ScriptSegment
                {
                    Start = 0,
                    End = Math.Max(1.0, videoDurationSeconds),
                    Text = result.Title
                });
            }

            result.Segments = validSegments.OrderBy(s => s.Start).ToList();
        }

        public string GetMimeType(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext switch
            {
                ".mp4" => "video/mp4",
                ".mkv" => "video/x-matroska",
                ".mov" => "video/quicktime",
                ".avi" => "video/x-msvideo",
                ".webm" => "video/webm",
                _ => "video/mp4"
            };
        }
    }
}

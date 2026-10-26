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

namespace VideoNarrator.Services.PersonalityEngine
{
    public interface IMultiStageNarrationEngine
    {
        Task<VideoContentAnalysis> AnalyzeContentAsync(
            string apiKey,
            string model,
            string? fileUri,
            string? mimeType,
            string videoFilePath,
            VideoProbeResult? probeResult,
            string? apiUrl = null,
            Action<string, double>? progressCallback = null,
            CancellationToken cancellationToken = default);

        Task<string> GenerateNarrativeAsync(
            string apiKey,
            string model,
            VideoContentAnalysis analysis,
            NarrationPersonaConfig personaConfig,
            PersonaProfile personaProfile,
            string? customUserInstructions = null,
            string? apiUrl = null,
            Action<string, double>? progressCallback = null,
            CancellationToken cancellationToken = default);

        Task<(string finalScript, ScriptQualityEvaluation evaluation)> EvaluateAndReviseAsync(
            string apiKey,
            string model,
            string draftScript,
            VideoContentAnalysis analysis,
            NarrationPersonaConfig personaConfig,
            PersonaProfile personaProfile,
            string? apiUrl = null,
            Action<string, double>? progressCallback = null,
            CancellationToken cancellationToken = default);

        Task<(string script, VideoContentAnalysis analysis, ScriptQualityEvaluation evaluation)> RunFullNarrationPipelineAsync(
            string apiKey,
            string model,
            string? fileUri,
            string? mimeType,
            string videoFilePath,
            double videoDurationSeconds,
            NarrationPersonaConfig personaConfig,
            PersonaProfile personaProfile,
            VideoProbeResult? probeResult = null,
            string? customUserInstructions = null,
            string? apiUrl = null,
            Action<string, double>? progressCallback = null,
            CancellationToken cancellationToken = default);
    }

    public class MultiStageNarrationEngine : IMultiStageNarrationEngine
    {
        private readonly HttpClient _httpClient;
        private readonly IFileService _fileService;
        private readonly IStyleEngine _styleEngine;
        private readonly IFewShotManager _fewShotManager;
        private const string BaseApiUrl = "https://generativelanguage.googleapis.com";

        public MultiStageNarrationEngine(
            HttpClient httpClient,
            IFileService fileService,
            IStyleEngine styleEngine,
            IFewShotManager fewShotManager)
        {
            _httpClient = httpClient;
            _fileService = fileService;
            _styleEngine = styleEngine;
            _fewShotManager = fewShotManager;
        }

        public async Task<VideoContentAnalysis> AnalyzeContentAsync(
            string apiKey,
            string model,
            string? fileUri,
            string? mimeType,
            string videoFilePath,
            VideoProbeResult? probeResult,
            string? apiUrl = null,
            Action<string, double>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            progressCallback?.Invoke("Giai đoạn A: Đang phân tích bối cảnh hình ảnh và âm thanh thực tế...", 25);
            _fileService.Log("Bắt đầu Stage A — Content Understanding (Trích xuất sự thật khách quan)");

            bool isCustomApi = !string.IsNullOrWhiteSpace(apiUrl) && !apiUrl.Contains("googleapis.com", StringComparison.OrdinalIgnoreCase);
            bool hasFileUri = !string.IsNullOrEmpty(fileUri);

            // Xây dựng prompt Stage A
            var sbPrompt = new StringBuilder();
            sbPrompt.AppendLine("BẠN LÀ CHUYÊN GIA PHÂN TÍCH ĐA PHƯƠNG THỨC VIDEO (MULTIMODAL VIDEO CONTENT ANALYST).");
            sbPrompt.AppendLine("Nhiệm vụ của bạn là trích xuất 100% SỰ THẬT KHÁCH QUAN từ video hoặc dữ liệu quan sát được (ZERO HALLUCINATION).");
            sbPrompt.AppendLine("CHỈ GHI NHẬN NHỮNG GÌ THỰC SỰ THẤY HOẶC NGHE THẤY. Không tự suy diễn hay bịa đặt.");
            sbPrompt.AppendLine();

            if (probeResult != null && probeResult.Success)
            {
                sbPrompt.AppendLine("=== DỮ LIỆU ĐO ĐẠC THỰC TẾ TỪ CẢNH QUAY ===");
                if (!string.IsNullOrWhiteSpace(probeResult.AudioText))
                    sbPrompt.AppendLine($"- Lời thoại/Âm thanh gốc trong video: \"{probeResult.AudioText}\"");
                if (!string.IsNullOrWhiteSpace(probeResult.OcrText))
                    sbPrompt.AppendLine($"- Văn bản OCR trên màn hình: \"{probeResult.OcrText}\"");
                if (!string.IsNullOrWhiteSpace(probeResult.SuggestedTopic))
                    sbPrompt.AppendLine($"- Gợi ý chủ đề quan sát: \"{probeResult.SuggestedTopic}\"");
                sbPrompt.AppendLine("===========================================");
            }

            sbPrompt.AppendLine(@"XUẤT RA DUY NHẤT MỘT ĐỐI TƯỢNG JSON VỚI CÁC TRƯỜNG SAU:
{
  ""videoTopic"": ""Chủ đề chính xác của video"",
  ""mainSubject"": ""Chủ thể và hành động trung tâm"",
  ""observedEvents"": [
    ""Hành động 1 diễn ra ở giây đầu..."",
    ""Hành động 2 tiếp theo..."",
    ""Kết quả cuối...""
  ],
  ""keyVisualDetails"": [
    ""Chi tiết vật thể, màu sắc, công cụ, trang phục, vị trí đắt giá...""
  ],
  ""curiosityPoint"": ""Điểm nghịch lý, chi tiết bất thường hoặc sự đối lập thú vị nhất"",
  ""explanation"": ""Cơ chế hoạt động hoặc lý do đằng sau hành động (nếu quan sát được)"",
  ""payoff"": ""Kết quả hoặc giá trị thực tế sau cùng mà video mang lại"",
  ""uncertainties"": [
    ""Chi tiết chưa đủ dữ liệu để khẳng định nguyên nhân chắc chắn (để bước sau không khẳng định bừa)""
  ],
  ""sceneTimeline"": [
    {
      ""startTime"": 0.0,
      ""endTime"": 3.0,
      ""visualDescription"": ""Mô tả cảnh 1..."",
      ""narrativeRole"": ""hook""
    }
  ],
  ""overallPacing"": ""medium""
}");

            string? responseJson = await CallGeminiAsync(apiKey, model, sbPrompt.ToString(), fileUri, mimeType, isCustomApi, apiUrl, cancellationToken);
            VideoContentAnalysis? analysis = null;

            if (!string.IsNullOrWhiteSpace(responseJson))
            {
                analysis = JsonHelper.DeserializeSafe<VideoContentAnalysis>(responseJson);
            }

            // Fallback an toàn nếu chưa phân tích được từ AI
            if (analysis == null || string.IsNullOrWhiteSpace(analysis.MainSubject))
            {
                _fileService.Log("Tạo VideoContentAnalysis dự phòng từ probeResult và metadata file.", "INFO");
                string topic = probeResult?.SuggestedTopic ?? Path.GetFileNameWithoutExtension(videoFilePath);
                analysis = new VideoContentAnalysis
                {
                    VideoTopic = topic,
                    MainSubject = !string.IsNullOrWhiteSpace(probeResult?.SuggestedTopic) ? probeResult.SuggestedTopic : "Chủ thể trong video",
                    ObservedEvents = new List<string>
                    {
                        "Bắt đầu hành động với tình huống bất thường",
                        "Các bước xử lý cụ thể xuất hiện liên tiếp",
                        "Khoảnh khắc cao trào và kết quả hoàn tất"
                    },
                    KeyVisualDetails = new List<string> { probeResult?.OcrText ?? "Chuyển động trên màn hình" },
                    CuriosityPoint = "Cách xử lý độc lạ và kết quả bất ngờ ngoài dự đoán",
                    Explanation = "Cơ chế phối hợp thao tác chính xác và công cụ hỗ trợ",
                    Payoff = "Mục tiêu ban đầu được giải quyết trọn vẹn",
                    Uncertainties = new List<string>(),
                    OverallPacing = "medium"
                };
            }

            _fileService.Log($"Hoàn tất Stage A: Chủ đề '{analysis.VideoTopic}', Subject: '{analysis.MainSubject}'");
            return analysis;
        }

        public async Task<string> GenerateNarrativeAsync(
            string apiKey,
            string model,
            VideoContentAnalysis analysis,
            NarrationPersonaConfig personaConfig,
            PersonaProfile personaProfile,
            string? customUserInstructions = null,
            string? apiUrl = null,
            Action<string, double>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            progressCallback?.Invoke("Giai đoạn B: Đang sinh lời thoại theo khung kể chuyện 6 phần...", 50);
            _fileService.Log($"Bắt đầu Stage B — Narrative Generation (Preset: {personaConfig.PresetId}, Target: {personaConfig.TargetDurationSeconds:F0}s)");

            bool isCustomApi = !string.IsNullOrWhiteSpace(apiUrl) && !apiUrl.Contains("googleapis.com", StringComparison.OrdinalIgnoreCase);

            // Tính toán ngân sách từ ngữ chuẩn xác
            var (minWords, maxWords, targetWords) = _styleEngine.CalculateDurationBudget(
                personaConfig.TargetDurationSeconds,
                personaConfig.NarrationSpeed,
                personaProfile.StyleConfig.SentenceRhythm);

            personaConfig.MinWords = minWords;
            personaConfig.MaxWords = maxWords;

            string styleDirectives = _styleEngine.MapLinguisticDirectives(personaConfig);

            var sbPrompt = new StringBuilder();
            sbPrompt.AppendLine("BẠN LÀ BẬC THẦY KỂ CHUYỆN (MASTER STORYTELLER) CHO VIDEO NGẮN TRIỆU VIEW.");
            sbPrompt.AppendLine("Nhiệm vụ: Viết lời kể chuyện tự nhiên như người thật đang hào hứng kể lại điều thú vị vừa phát hiện.");
            sbPrompt.AppendLine();
            sbPrompt.AppendLine("=== DỮ LIỆU SỰ THẬT KHÁCH QUAN TỪ VIDEO (STAGE A ANALYSIS) ===");
            sbPrompt.AppendLine($"- Chủ đề: {analysis.VideoTopic}");
            sbPrompt.AppendLine($"- Chủ thể & Hành động: {analysis.MainSubject}");
            sbPrompt.AppendLine($"- Các sự kiện diễn ra: {string.Join(" -> ", analysis.ObservedEvents)}");
            sbPrompt.AppendLine($"- Chi tiết thị giác đắt giá: {string.Join(", ", analysis.KeyVisualDetails)}");
            sbPrompt.AppendLine($"- Điểm gây tò mò/Nghịch lý: {analysis.CuriosityPoint}");
            sbPrompt.AppendLine($"- Cơ chế/Nguyên lý: {analysis.Explanation}");
            sbPrompt.AppendLine($"- Kết quả (Payoff): {analysis.Payoff}");
            if (analysis.Uncertainties.Count > 0)
            {
                sbPrompt.AppendLine($"- ĐIỀU CHƯA RÕ CHẮC CHẮN (CẤM TỰ BỊA ĐẶT): {string.Join("; ", analysis.Uncertainties)}");
                sbPrompt.AppendLine("  👉 Phải dùng cách nói phân biệt giữa điều nhìn thấy và giả thuyết hợp lý.");
            }
            sbPrompt.AppendLine("============================================================");
            sbPrompt.AppendLine();

            sbPrompt.AppendLine(styleDirectives);
            sbPrompt.AppendLine();

            sbPrompt.AppendLine("=== KHUNG KỂ CHUYỆN LINH HOẠT 6 PHẦN (NARRATIVE FRAMEWORK) ===");
            sbPrompt.AppendLine("1. Opening Hook: Mở đầu ngay lập tức bằng một chi tiết bất thường, hành động khó hiểu hoặc sự đối lập thú vị.");
            sbPrompt.AppendLine("   ❌ CẤM TUYỆT ĐỐI: 'Xin chào mọi người', 'Trong video hôm nay', 'Bạn có biết rằng', 'Hôm nay chúng ta sẽ tìm hiểu'.");
            sbPrompt.AppendLine("2. Establish the Situation: Cung cấp vừa đủ bối cảnh/luật lệ/rào cản để hiểu tình huống.");
            sbPrompt.AppendLine("3. Build Curiosity: Mỗi câu tiếp theo phải thêm thông tin mới hoặc làm rõ sự tương phản giữa dự đoán và thực tế.");
            sbPrompt.AppendLine("4. Reveal the Mechanism: Giải thích cơ chế điều thực sự xảy ra bằng động từ giàu hình ảnh và nhân - quả logic.");
            sbPrompt.AppendLine("5. Payoff: Đưa người xem tới kết quả thỏa mãn sự tò mò. Kết thúc tròn trịa.");
            sbPrompt.AppendLine("6. Optional CTA: Chỉ thêm nếu thực sự ăn khớp tự nhiên với vật thể trong video ('Biến nút thích thành...'). Tuyệt đối không xin like ăn xin.");
            sbPrompt.AppendLine("============================================================");
            sbPrompt.AppendLine();

            sbPrompt.AppendLine("=== QUY TẮC THỜI LƯỢNG & CHỐNG BÔI CHỮ (ANTI-PADDING) ===");
            sbPrompt.AppendLine($"- Ngân sách từ: Bạn BẮT BUỘC viết trong khoảng {minWords} đến {maxWords} từ (mục tiêu ~{targetWords:F0} từ).");
            sbPrompt.AppendLine("- NGUYÊN TẮC VÀNG: Nếu video ngắn hoặc ít thông tin, ưu tiên kịch bản ngắn hơn nhưng chất lượng, chèn khoảng nghỉ hợp lý để ngắm hình ảnh, TUYỆT ĐỐI KHÔNG BÔI CHỮ hay chèn triết lý sáo rỗng để đủ từ.");
            sbPrompt.AppendLine("- Đọc thuận miệng: Dùng dấu chấm câu (.) và phẩy (,) chuẩn xác để AI TTS ngắt nhịp thở tự nhiên.");
            sbPrompt.AppendLine();

            if (!string.IsNullOrWhiteSpace(customUserInstructions))
            {
                sbPrompt.AppendLine($"YÊU CẦU ĐẶC BIỆT TỪ NGƯỜI DÙNG: \"{customUserInstructions}\"");
                sbPrompt.AppendLine();
            }

            sbPrompt.AppendLine("CHỈ XUẤT RA DUY NHẤT LỜI THOẠI THUYẾT MINH HOÀN CHỈNH BẰNG TIẾNG VIỆT, KHÔNG THÊM LỜI DẪN, KHÔNG THÊM NGOẶC VUÔNG GIẢI THÍCH.");

            string? draft = await CallGeminiAsync(apiKey, model, sbPrompt.ToString(), null, null, isCustomApi, apiUrl, cancellationToken);
            if (string.IsNullOrWhiteSpace(draft))
            {
                draft = GenerateFallbackScript(analysis, personaConfig);
            }

            draft = CleanNarrationScript(draft);
            _fileService.Log($"Hoàn tất Stage B: Lời thoại nháp gồm {CountWords(draft)} từ.");
            return draft;
        }

        public async Task<(string finalScript, ScriptQualityEvaluation evaluation)> EvaluateAndReviseAsync(
            string apiKey,
            string model,
            string draftScript,
            VideoContentAnalysis analysis,
            NarrationPersonaConfig personaConfig,
            PersonaProfile personaProfile,
            string? apiUrl = null,
            Action<string, double>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            progressCallback?.Invoke("Giai đoạn C: Đang kiểm duyệt chất lượng 8 tiêu chuẩn...", 75);
            _fileService.Log("Bắt đầu Stage C — Quality Evaluation & Targeted Revision");

            bool isCustomApi = !string.IsNullOrWhiteSpace(apiUrl) && !apiUrl.Contains("googleapis.com", StringComparison.OrdinalIgnoreCase);

            var sbEval = new StringBuilder();
            sbEval.AppendLine("BẠN LÀ CHUYÊN GIA BIÊN TẬP & ĐẢM BẢO CHẤT LƯỢNG NỘI DUNG (QUALITY ASSURANCE EDITOR).");
            sbEval.AppendLine("Hãy chấm điểm kịch bản sau trên thang điểm 1-10 theo 8 tiêu chí chuẩn mực:");
            sbEval.AppendLine("1. hookSpecificity: Hook có cụ thể, bám sát hình ảnh đầu và tránh văn mẫu không?");
            sbEval.AppendLine("2. logicalFlow: Các câu có nối tiếp mạch lạc nhân - quả không?");
            sbEval.AppendLine("3. informationDensity: Mỗi câu có mang lại dữ kiện mới không?");
            sbEval.AppendLine("4. clicheFree: Sạch bóng từ ngữ sáo rỗng (điều bất ngờ, không thể tin nổi, ít ai biết, hãy like)?");
            sbEval.AppendLine("5. spokenNaturalness: Đọc thành tiếng có thuận miệng, đúng ngữ điệu người thật không?");
            sbEval.AppendLine("6. visualAccuracy: Có trung thực với bối cảnh video và không bịa đặt không?");
            sbEval.AppendLine("7. payoffSatisfaction: Kết thúc có thỏa mãn sự tò mò không?");
            sbEval.AppendLine($"8. durationFit: Số từ ({CountWords(draftScript)} từ) có nằm trong khoảng [{personaConfig.MinWords} - {personaConfig.MaxWords}] từ không?");
            sbEval.AppendLine();
            sbEval.AppendLine($"KỊCH BẢN CẦN ĐÁNH GIÁ:\n\"{draftScript}\"");
            sbEval.AppendLine();
            sbEval.AppendLine(@"XUẤT RA DUY NHẤT MỘT JSON:
{
  ""hookSpecificity"": 9.0,
  ""logicalFlow"": 8.5,
  ""informationDensity"": 8.5,
  ""clicheFree"": 9.0,
  ""spokenNaturalness"": 8.5,
  ""visualAccuracy"": 9.0,
  ""payoffSatisfaction"": 8.5,
  ""durationFit"": 8.5,
  ""averageScore"": 8.7,
  ""passed"": true,
  ""weakSentences"": [
    {
      ""originalSentence"": ""câu bị lỗi nếu có..."",
      ""reason"": ""lý do bị lỗi..."",
      ""suggestedFix"": ""hướng sửa...""
    }
  ],
  ""revisionInstructions"": ""chỉ thị chỉnh sửa nếu cần...""
}");

            string? evalJson = await CallGeminiAsync(apiKey, model, sbEval.ToString(), null, null, isCustomApi, apiUrl, cancellationToken);
            ScriptQualityEvaluation? evaluation = null;

            if (!string.IsNullOrWhiteSpace(evalJson))
            {
                evaluation = JsonHelper.DeserializeSafe<ScriptQualityEvaluation>(evalJson);
            }

            if (evaluation == null)
            {
                evaluation = EvaluateHeuristically(draftScript, personaConfig);
            }

            // Tính điểm trung bình thực tế
            evaluation.AverageScore = (evaluation.HookSpecificity + evaluation.LogicalFlow +
                                       evaluation.InformationDensity + evaluation.ClicheFree +
                                       evaluation.SpokenNaturalness + evaluation.VisualAccuracy +
                                       evaluation.PayoffSatisfaction + evaluation.DurationFit) / 8.0;

            evaluation.Passed = evaluation.AverageScore >= 7.5 &&
                                evaluation.HookSpecificity >= 7.0 &&
                                evaluation.ClicheFree >= 7.0 &&
                                evaluation.VisualAccuracy >= 7.0;

            _fileService.Log($"Điểm đánh giá Stage C: {evaluation.AverageScore:F1}/10 (Passed: {evaluation.Passed})");

            // Nếu kịch bản chưa đạt và có câu yếu -> thực hiện Targeted Revision duy nhất 1 lần
            if (!evaluation.Passed && evaluation.WeakSentences.Count > 0)
            {
                progressCallback?.Invoke("Đang tinh chỉnh có mục tiêu các câu chưa đạt chuẩn...", 85);
                _fileService.Log($"Thực hiện Targeted Revision cho {evaluation.WeakSentences.Count} câu yếu...");

                var sbRevise = new StringBuilder();
                sbRevise.AppendLine("BẠN LÀ CHUYÊN GIA SỬA LỖI KỊCH BẢN CÓ MỤC TIÊU (TARGETED REVISION EDITOR).");
                sbRevise.AppendLine("Dưới đây là kịch bản gốc và danh sách các câu cần sửa:");
                sbRevise.AppendLine($"KỊCH BẢN GỐC:\n\"{draftScript}\"");
                sbRevise.AppendLine();
                sbRevise.AppendLine("DANH SÁCH CÂU LỖI CẦN SỬA:");
                foreach (var ws in evaluation.WeakSentences)
                {
                    sbRevise.AppendLine($"- Câu lỗi: \"{ws.OriginalSentence}\"");
                    sbRevise.AppendLine($"  + Lý do: {ws.Reason}");
                    sbRevise.AppendLine($"  + Hướng sửa: {ws.SuggestedFix}");
                }
                sbRevise.AppendLine();
                sbRevise.AppendLine("YÊU CẦU: CHỈ VIẾT LẠI ĐÚNG CÁC CÂU LỖI, GIỮ NGUYÊN CÁC CÂU ĐÃ ĐẠT CHUẨN VÀ GIỮ TRỌN VẸN CÁ TÍNH PHONG CÁCH. Xuất ra toàn bộ kịch bản hoàn chỉnh sau khi thay thế.");

                string? revised = await CallGeminiAsync(apiKey, model, sbRevise.ToString(), null, null, isCustomApi, apiUrl, cancellationToken);
                if (!string.IsNullOrWhiteSpace(revised))
                {
                    draftScript = CleanNarrationScript(revised);
                    _fileService.Log("Targeted Revision hoàn tất thành công.");
                }
            }

            return (draftScript, evaluation);
        }

        public async Task<(string script, VideoContentAnalysis analysis, ScriptQualityEvaluation evaluation)> RunFullNarrationPipelineAsync(
            string apiKey,
            string model,
            string? fileUri,
            string? mimeType,
            string videoFilePath,
            double videoDurationSeconds,
            NarrationPersonaConfig personaConfig,
            PersonaProfile personaProfile,
            VideoProbeResult? probeResult = null,
            string? customUserInstructions = null,
            string? apiUrl = null,
            Action<string, double>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            personaConfig.TargetDurationSeconds = videoDurationSeconds;

            // STAGE A
            var analysis = await AnalyzeContentAsync(apiKey, model, fileUri, mimeType, videoFilePath, probeResult, apiUrl, progressCallback, cancellationToken);

            // STAGE B
            string draft = await GenerateNarrativeAsync(apiKey, model, analysis, personaConfig, personaProfile, customUserInstructions, apiUrl, progressCallback, cancellationToken);

            // STAGE C
            var (finalScript, evaluation) = await EvaluateAndReviseAsync(apiKey, model, draft, analysis, personaConfig, personaProfile, apiUrl, progressCallback, cancellationToken);

            return (finalScript, analysis, evaluation);
        }

        private async Task<string?> CallGeminiAsync(
            string apiKey,
            string model,
            string prompt,
            string? fileUri,
            string? mimeType,
            bool isCustomApi,
            string? apiUrl,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(apiKey) && !isCustomApi)
            {
                return null;
            }

            string targetModel = !string.IsNullOrWhiteSpace(model) ? model : (isCustomApi ? "gemini-3.1-flash-lite" : "gemini-3.5-flash");
            string url = isCustomApi
                ? $"{apiUrl!.TrimEnd('/')}/generate"
                : $"{BaseApiUrl}/v1beta/models/{targetModel}:generateContent?key={apiKey}";

            object requestPayload;
            if (isCustomApi)
            {
                requestPayload = new { prompt, model = targetModel };
            }
            else
            {
                var partsList = new List<object>();
                if (!string.IsNullOrEmpty(fileUri) && !string.IsNullOrEmpty(mimeType))
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

                requestPayload = new
                {
                    contents = new[]
                    {
                        new { parts = partsList.ToArray() }
                    },
                    generationConfig = new
                    {
                        temperature = 0.72
                    }
                };
            }

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(50));

                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Content = new StringContent(JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json");

                using var resp = await _httpClient.SendAsync(req, cts.Token);
                string text = await resp.Content.ReadAsStringAsync(cts.Token);

                if (resp.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(text);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("text", out var tp)) return tp.GetString();
                    if (root.TryGetProperty("candidates", out var cand) && cand.GetArrayLength() > 0)
                    {
                        var first = cand[0];
                        if (first.TryGetProperty("content", out var cnt) &&
                            cnt.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0)
                        {
                            return parts[0].GetProperty("text").GetString();
                        }
                    }
                    if (root.TryGetProperty("choices", out var ch) && ch.GetArrayLength() > 0)
                    {
                        return ch[0].GetProperty("message").GetProperty("content").GetString();
                    }
                }
            }
            catch (Exception ex)
            {
                _fileService.Log($"CallGeminiAsync ngoại lệ: {ex.Message}", "WARN");
            }

            return null;
        }

        private static string GenerateFallbackScript(VideoContentAnalysis analysis, NarrationPersonaConfig config)
        {
            var sb = new StringBuilder();
            sb.Append($"Đây là cách {analysis.MainSubject.ToLowerInvariant()} tạo nên điều bất ngờ. ");
            sb.Append("Thay vì làm theo cách thông thường, từng bước được thực hiện với độ chuẩn xác đáng nể. ");
            sb.Append($"{analysis.Explanation}. ");
            sb.Append($"Và kết quả là {analysis.Payoff.ToLowerInvariant()}. ");
            sb.Append("Biến nút thích thành một điều may mắn nếu bạn thấy điều này thú vị!");
            return sb.ToString();
        }

        private static ScriptQualityEvaluation EvaluateHeuristically(string script, NarrationPersonaConfig config)
        {
            var eval = new ScriptQualityEvaluation();
            int wordCount = CountWords(script);

            eval.DurationFit = (wordCount >= config.MinWords && wordCount <= config.MaxWords) ? 9.0 : 7.0;

            string lower = script.ToLowerInvariant();
            bool hasCliche = lower.Contains("chào mừng") || lower.Contains("bạn sẽ không tin") || lower.Contains("hãy like");
            eval.ClicheFree = hasCliche ? 6.0 : 9.0;
            eval.HookSpecificity = (lower.StartsWith("đây là") || lower.StartsWith("tại sao") || lower.StartsWith("nhìn")) ? 9.5 : 8.0;

            eval.AverageScore = (eval.HookSpecificity + eval.LogicalFlow + eval.InformationDensity +
                                 eval.ClicheFree + eval.SpokenNaturalness + eval.VisualAccuracy +
                                 eval.PayoffSatisfaction + eval.DurationFit) / 8.0;

            eval.Passed = eval.AverageScore >= 7.5;
            return eval;
        }

        public static int CountWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            return text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        }

        public static string CleanNarrationScript(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            string s = text.Trim();
            // Xóa code block markdown ```json hoặc ```
            s = Regex.Replace(s, @"```[a-zA-Z]*\s*", "", RegexOptions.Multiline);
            s = Regex.Replace(s, @"```\s*$", "", RegexOptions.Multiline);

            // Xóa tiền tố kịch bản như "Lời thoại:", "Kịch bản:"
            s = Regex.Replace(s, @"^(Lời thoại|Kịch bản|Narration|Voiceover|Script)\s*:\s*", "", RegexOptions.IgnoreCase);

            // Bỏ các ngoặc vuông ghi chú thời gian nếu có [00:00 - 00:05]
            s = Regex.Replace(s, @"\[\d{2}:\d{2}\s*-\s*\d{2}:\d{2}\]", "").Trim();

            return s.Trim();
        }
    }
}

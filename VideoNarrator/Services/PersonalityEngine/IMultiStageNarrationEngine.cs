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
            string? topic = null,
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
            string? topic = null,
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

            if (!string.IsNullOrWhiteSpace(topic))
            {
                sbPrompt.AppendLine($"- Chủ đề hoặc tiêu đề gốc của video: \"{topic.Trim()}\"");
            }

            if (probeResult != null)
            {
                if (probeResult.KeyframeBase64List != null && probeResult.KeyframeBase64List.Count > 0)
                {
                    sbPrompt.AppendLine($"- Đã đính kèm {probeResult.KeyframeBase64List.Count} khung hình thực tế từ các mốc thời gian trong video. Hãy quan sát thật kỹ các ảnh này để nhận diện chính xác chủ thể, bối cảnh, công cụ, hành động và không gian.");
                }

                if (probeResult.Success)
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

            string? responseJson = await CallGeminiAsync(apiKey, model, sbPrompt.ToString(), fileUri, mimeType, isCustomApi, apiUrl, probeResult?.KeyframeBase64List, cancellationToken, forceJson: true);
            VideoContentAnalysis? analysis = null;

            if (!string.IsNullOrWhiteSpace(responseJson))
            {
                analysis = JsonHelper.DeserializeSafe<VideoContentAnalysis>(responseJson);
            }

            // Fallback an toàn nếu chưa phân tích được từ AI hoặc AI trả về cụm từ rác "chưa xác định"
            bool isInvalidAnalysis = analysis == null ||
                                     string.IsNullOrWhiteSpace(analysis.MainSubject) ||
                                     analysis.MainSubject.Contains("chưa xác định", StringComparison.OrdinalIgnoreCase) ||
                                     analysis.MainSubject.Contains("chưa có", StringComparison.OrdinalIgnoreCase) ||
                                     analysis.VideoTopic.Contains("chưa có dữ liệu", StringComparison.OrdinalIgnoreCase) ||
                                     analysis.VideoTopic.Contains("chưa xác định", StringComparison.OrdinalIgnoreCase);

            if (isInvalidAnalysis)
            {
                _fileService.Log("Tạo VideoContentAnalysis dự phòng từ metadata file và dữ liệu đo đạc.", "INFO");
                string resolvedTopic = !string.IsNullOrWhiteSpace(topic)
                    ? topic.Trim()
                    : (!string.IsNullOrWhiteSpace(probeResult?.SuggestedTopic)
                        ? probeResult.SuggestedTopic
                        : Path.GetFileNameWithoutExtension(videoFilePath));

                analysis = new VideoContentAnalysis
                {
                    VideoTopic = resolvedTopic,
                    MainSubject = resolvedTopic,
                    ObservedEvents = new List<string>
                    {
                        "Bắt đầu với tình huống gây chú ý ngay từ giây đầu",
                        "Diễn biến hành động nối tiếp theo nhịp điệu hình ảnh",
                        "Khoảnh khắc ấn tượng và kết quả cuối cùng"
                    },
                    KeyVisualDetails = new List<string> { probeResult?.OcrText ?? "Hình ảnh thực tế từ cảnh quay" },
                    CuriosityPoint = "Chi tiết đối lập hoặc diễn biến bất ngờ thu hút ánh nhìn",
                    Explanation = "Diễn biến thực tế được ghi lại trọn vẹn qua từng khung hình",
                    Payoff = "Mang lại trải nghiệm cuốn hút và thỏa mãn trí tò mò của người xem",
                    Uncertainties = new List<string>(),
                    OverallPacing = "medium"
                };
            }

            // Chuẩn hóa triệt để: Tuyệt đối không để sót chuỗi rác placeholder "chưa xác định" trong bất kỳ trường nào
            if (analysis != null)
            {
                if (string.IsNullOrWhiteSpace(analysis.Explanation) || analysis.Explanation.Contains("chưa xác định", StringComparison.OrdinalIgnoreCase))
                    analysis.Explanation = "Cơ chế hoạt động được thể hiện chi tiết qua từng chuyển động trên khung hình";

                if (string.IsNullOrWhiteSpace(analysis.Payoff) || analysis.Payoff.Contains("chưa xác định", StringComparison.OrdinalIgnoreCase))
                    analysis.Payoff = "Kết quả thuyết phục và thỏa mãn sự tò mò của người xem";

                if (string.IsNullOrWhiteSpace(analysis.CuriosityPoint) || analysis.CuriosityPoint.Contains("chưa xác định", StringComparison.OrdinalIgnoreCase))
                    analysis.CuriosityPoint = "Chi tiết đối lập hoặc diễn biến bất ngờ thu hút sự chú ý";

                if (analysis.ObservedEvents != null)
                {
                    analysis.ObservedEvents = analysis.ObservedEvents
                        .Where(e => !string.IsNullOrWhiteSpace(e) && !e.Contains("chưa xác định", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (analysis.ObservedEvents.Count == 0)
                    {
                        analysis.ObservedEvents.Add("Diễn biến hành động nối tiếp theo nhịp điệu hình ảnh");
                    }
                }
            }

            _fileService.Log($"Hoàn tất Stage A: Chủ đề '{analysis?.VideoTopic}', Subject: '{analysis?.MainSubject}'");
            return analysis!;
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

            // 1. Tích hợp Few-Shot Learning các mẫu kịch bản vàng chuẩn triệu view
            var relevantExamples = _fewShotManager.GetRelevantExamples(analysis.VideoTopic, personaConfig.PresetId, 2);
            if (relevantExamples != null && relevantExamples.Count > 0)
            {
                sbPrompt.AppendLine(_fewShotManager.FormatFewShotPromptSection(relevantExamples));
                sbPrompt.AppendLine();
            }

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

            if (personaConfig.IsEnglish)
            {
                sbPrompt.AppendLine("=== MANDATORY LANGUAGE REQUIREMENT: 100% FLUENT AMERICAN ENGLISH ===");
                sbPrompt.AppendLine("You MUST write the entire voiceover script exclusively in punchy, highly engaging, conversational American English (style of Zack D. Films / MrBeast).");
                sbPrompt.AppendLine("Hook viewers directly in the first 2 seconds ('Look closely at this...', 'This is why...', 'Here is what happens when...').");
                sbPrompt.AppendLine("Use crisp, vivid, natural English words. NO cringe, NO cliché greetings ('Welcome back', 'In today's video').");
                sbPrompt.AppendLine("RICH INFORMATION & FACTS: Deliver deep, intriguing value (explain the real scientific principles, precise mechanics, exact numbers, and surprising insights).");
                sbPrompt.AppendLine("MANDATORY ENDING: Conclude the script with an engaging subscribe Call-To-Action: 'Make sure to subscribe for more fascinating stories!' or 'Hit subscribe for more awesome videos!'.");
                sbPrompt.AppendLine("=========================================================================");
                sbPrompt.AppendLine();
            }

            sbPrompt.AppendLine("=== YÊU CẦU ĐẶC BIỆT: HÀM LƯỢNG THÔNG TIN SÂU & GIÀU TRI THỨC (RICH INFORMATION & FACTS) ===");
            sbPrompt.AppendLine("1. ĐI SÂU VÀO BẢN CHẤT & NGUYÊN LÝ: Không dừng lại ở việc tả cảnh hời hợt, phải giải thích rõ TẠI SAO điều này lại xảy ra, cơ chế khoa học, vật lý, kỹ thuật hay phản xạ nào đứng sau hiện tượng.");
            sbPrompt.AppendLine("2. DẪN CHỨNG SỐ LIỆU & THỰC TẾ: Nếu có, lồng ghép các thông số thú vị (tốc độ, nhiệt độ, áp suất, độ bền, độ chính xác cơ khí, niên đại, hoặc điểm độc lạ).");
            sbPrompt.AppendLine("3. MANG LẠI GIÁ TRỊ CHO NGƯỜI XEM: Đảm bảo người xem sau khi xem xong sẽ cảm thấy mình vừa 'học thêm được một kiến thức mới lạ' hoặc 'hiểu thấu một bí quyết thú vị'.");
            sbPrompt.AppendLine("==========================================================================================");
            sbPrompt.AppendLine();

            sbPrompt.AppendLine("=== TIÊU CHUẨN VĂN PHONG: NÓI THẬT, ĐỜI THƯỜNG, TUYỆT ĐỐI KHÔNG XÀM XÍ (ZERO CRINGE) ===");
            sbPrompt.AppendLine("1. ❌ CẤM TUYỆT ĐỐI TỪ NGỮ VĂN MẪU XÀM XÍ, PHÓNG ĐẠI TRẺ TRÂU:");
            sbPrompt.AppendLine("   - CẤM các từ giật gân rẻ tiền, khoa trương lố lăng: 'cú hack cực đỉnh', 'cú hack vật lý', 'như tay đua chuyên nghiệp', 'tốc độ xé gió', 'lột xác khó tin', 'não được chữa lành', 'bùng nổ khoang miệng', 'đỉnh chóp', 'ảo diệu', 'kỳ diệu', 'thánh đường', 'tâm hồn', 'che chở', 'thiên tài đại dương'.");
            sbPrompt.AppendLine("   - CẤM TUYỆT ĐỐI MỌI CÂU XIN LIKE NHẢM NHÍ: 'Biến nút thích thành...', 'Bấm like để...', 'Thả tim nếu...'.");
            sbPrompt.AppendLine("2. ❌ CẤM SPAM CÂU HỎI TU TỪ GIẢ TẠO:");
            sbPrompt.AppendLine("   - CẤM dùng câu hỏi để mở đầu: 'Nhìn kìa!', 'Bạn có thấy... không?'.");
            sbPrompt.AppendLine("   - CẤM các câu hỏi chatbot rập khuôn: 'Nhưng đợi đã, tại sao...?', 'Bạn có bao giờ thắc mắc...?', 'Chỉ là để... thôi sao?'.");
            sbPrompt.AppendLine("3. ✔️ NÓI CHUYỆN NHƯ MỘT NGƯỜI ĐÀNG HOÀNG, AM HIỂU, ĐỜI THƯỜNG (CONVERSATIONAL & AUTHENTIC):");
            sbPrompt.AppendLine("   - Giọng điệu điềm đạm, gãy gọn, khách quan, tự nhiên như lời thuyết minh phóng sự ngắn hoặc một người bạn hiểu chuyện kể lại.");
            sbPrompt.AppendLine("   - Đi thẳng vào tình huống thực tế hoặc sự đối lập ngay giây đầu tiên.");
            sbPrompt.AppendLine("   - Dùng từ ngữ tiếng Việt giản dị, chính xác, giàu hình ảnh thực tế (như 'nương theo luồng nước', 'đứng trên thang cuốn', 'thuộc lòng giao diện', 'đợi cả nửa tiếng').");
            sbPrompt.AppendLine("   - Bóc tách nguyên nhân - cơ chế mạch lạc, kết thúc gọn gàng ngay khi câu chuyện ngã ngũ.");
            sbPrompt.AppendLine("==========================================================================================");
            sbPrompt.AppendLine();

            sbPrompt.AppendLine(styleDirectives);
            sbPrompt.AppendLine();

            sbPrompt.AppendLine("=== KHUNG KỂ CHUYỆN LINH HOẠT (NARRATIVE FRAMEWORK) ===");
            sbPrompt.AppendLine("1. Opening Hook: Mở đầu ngay lập tức bằng một chi tiết bất thường, hành động khó hiểu hoặc sự đối lập thực tế.");
            sbPrompt.AppendLine("   ❌ CẤM TUYỆT ĐỐI: 'Xin chào mọi người', 'Trong video hôm nay', 'Bạn có biết rằng', 'Nhìn kìa', 'Chào mừng'.");
            sbPrompt.AppendLine("2. Establish the Situation: Cung cấp vừa đủ bối cảnh/rào cản để hiểu tình huống.");
            sbPrompt.AppendLine("3. Build Progression: Mỗi câu tiếp theo đưa ra dữ kiện mới hoặc làm rõ diễn biến tiếp theo.");
            sbPrompt.AppendLine("4. Reveal the Mechanism: Giải thích cơ chế điều thực sự xảy ra bằng sự thật khách quan và quan hệ nhân - quả logic.");
            sbPrompt.AppendLine("5. Payoff & Subscribe Call-To-Action (BẮT BUỘC):");
            sbPrompt.AppendLine("   - Kết thúc câu chuyện gọn gàng, giải đáp thỏa mãn sự tò mò.");
            sbPrompt.AppendLine("   - BẮT BUỘC kết thúc kịch bản bằng 1 câu kêu gọi đăng ký kênh tự nhiên, văn minh (Ví dụ: 'Đừng quên bấm đăng ký kênh để đón xem những video thú vị tiếp theo nhé!' hoặc 'Hãy bấm đăng ký kênh để không bỏ lỡ những điều bất ngờ tiếp theo!').");
            sbPrompt.AppendLine("   ❌ CẤM các câu xin like nhảm nhí dạng 'Biến nút thích thành...'.");
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

            sbPrompt.AppendLine("=== NGUYÊN TẮC ĐẦU RA BẮT BUỘC (CRITICAL) ===");
            sbPrompt.AppendLine("- CHỈ XUẤT RA DUY NHẤT LỜI THOẠI THUYẾT MINH TIẾNG VIỆT ĐỂ ĐỌC THÀNH TIẾNG. MỌI TỪ BẠN XUẤT RA ĐỀU SẼ ĐƯỢC ĐỌC LÊN VIDEO.");
            sbPrompt.AppendLine("- TUYỆT ĐỐI KHÔNG CHÀO HỎI (Cấm: 'Chào bạn', 'Xin chào', 'Chào mừng').");
            sbPrompt.AppendLine("- TUYỆT ĐỐI KHÔNG XƯNG DANH HAY GIỚI THIỆU VAI TRÒ (Cấm: 'Với tư cách là...', 'Tôi là...').");
            sbPrompt.AppendLine("- TUYỆT ĐỐI KHÔNG THÊM TIÊU ĐỀ ('Kịch bản hoàn chỉnh:', 'Lời thoại:', 'Kịch bản:').");
            sbPrompt.AppendLine("- BẮT ĐẦU NGAY LẬP TỨC BẰNG CÂU HOOK MỞ ĐẦU.");

            string? draft = await CallGeminiAsync(apiKey, model, sbPrompt.ToString(), null, null, isCustomApi, apiUrl, null, cancellationToken);
            if (string.IsNullOrWhiteSpace(draft))
            {
                draft = GenerateFallbackScript(analysis, personaConfig);
            }

            draft = CleanNarrationScript(draft);
            draft = EnsureSubscribeCallToAction(draft, personaConfig.IsEnglish);
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
            sbEval.AppendLine("4. clicheFree: Sạch bóng từ ngữ sáo rỗng, triết lý giáo điều (thánh đường, tâm hồn, che chở, yêu thương, kỳ diệu, ảo diệu, điều bất ngờ, không thể tin nổi) và CẤM spam câu hỏi tu từ (Nhìn kìa, Nhưng đợi đã, Bạn có bao giờ thắc mắc)? Nếu có bất kỳ câu nào mang tính giáo điều/sến súa hoặc câu hỏi tu từ sáo rỗng, PHẢI chấm clicheFree < 6.0 và đưa câu lỗi vào weakSentences.");
            sbEval.AppendLine("5. spokenNaturalness: Đọc thành tiếng có thuận miệng, đúng ngữ điệu người thật kể chuyện đời thường không? Hay giống bài tập làm văn / chatbot?");
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

            string? evalJson = await CallGeminiAsync(apiKey, model, sbEval.ToString(), null, null, isCustomApi, apiUrl, null, cancellationToken, forceJson: true);
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
                sbRevise.AppendLine("BẠN LÀ BỘ CHỈNH SỬA KỊCH BẢN TỰ ĐỘNG (SCRIPT REVISION ENGINE).");
                sbRevise.AppendLine("Dưới đây là kịch bản gốc và danh sách các câu cần thay thế:");
                sbRevise.AppendLine($"KỊCH BẢN GỐC:\n\"{draftScript}\"");
                sbRevise.AppendLine();
                sbRevise.AppendLine("DANH SÁCH CÂU LỖI CẦN SỬA:");
                foreach (var ws in evaluation.WeakSentences)
                {
                    sbRevise.AppendLine($"- Câu lỗi: \"{ws.OriginalSentence}\"");
                    sbRevise.AppendLine($"  + Lý do: {ws.Reason}");
                    sbRevise.AppendLine($"  + Gợi ý sửa: {ws.SuggestedFix}");
                }
                sbRevise.AppendLine();
                sbRevise.AppendLine("=== QUY TẮC ĐẦU RA BẮT BUỘC (CRITICAL) ===");
                sbRevise.AppendLine("1. Thay thế chính xác các câu lỗi bằng câu mới hay hơn: dùng động từ hành động cụ thể, bám sát thị giác, giọng điệu người thật, giữ nguyên các câu đạt chuẩn.");
                sbRevise.AppendLine("2. TUYỆT ĐỐI CẤM văn mẫu triết lý, sến súa (thánh đường, tâm hồn, che chở, kỳ diệu) và CẤM câu hỏi tu từ sáo rỗng.");
                sbRevise.AppendLine("3. TUYỆT ĐỐI KHÔNG CHÀO HỎI (Cấm: 'Chào bạn', 'Xin chào', 'Chào mừng').");
                sbRevise.AppendLine("4. TUYỆT ĐỐI KHÔNG XƯNG DANH HAY NÓI LỜI MỞ ĐẦU (Cấm: 'với tư cách là...', 'tôi đã thay thế...', 'dưới đây là...').");
                sbRevise.AppendLine("5. TUYỆT ĐỐI KHÔNG CÓ TIÊU ĐỀ ('KỊCH BẢN HOÀN CHỈNH:', 'Kịch bản:').");
                sbRevise.AppendLine("6. Xuất ra duy nhất đối tượng JSON:");
                sbRevise.AppendLine("{");
                sbRevise.AppendLine("  \"revisedScript\": \"Toàn bộ kịch bản hoàn chỉnh sau khi sửa, chỉ chứa lời đọc thành tiếng, bắt đầu ngay lập tức bằng câu Hook\"");
                sbRevise.AppendLine("}");

                string? revised = await CallGeminiAsync(apiKey, model, sbRevise.ToString(), null, null, isCustomApi, apiUrl, null, cancellationToken, forceJson: true);
                if (!string.IsNullOrWhiteSpace(revised))
                {
                    draftScript = CleanNarrationScript(revised);
                    _fileService.Log("Targeted Revision hoàn tất thành công.");
                }
            }

            draftScript = EnsureSubscribeCallToAction(draftScript, personaConfig.IsEnglish);
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
            var analysis = await AnalyzeContentAsync(apiKey, model, fileUri, mimeType, videoFilePath, probeResult, customUserInstructions, apiUrl, progressCallback, cancellationToken);

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
            List<string>? keyframeBase64List,
            CancellationToken cancellationToken,
            bool forceJson = false)
        {
            if (string.IsNullOrWhiteSpace(apiKey) && !isCustomApi)
            {
                return null;
            }

            // Kiểm tra nếu local proxy chưa đăng nhập (UNAUTHENTICATED) thì dừng ngay không để timeout
            if (isCustomApi && !string.IsNullOrWhiteSpace(apiUrl))
            {
                try
                {
                    using var pingReq = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                    using var pingCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    using var pingResp = await _httpClient.SendAsync(pingReq, pingCts.Token);
                    if (pingResp.IsSuccessStatusCode)
                    {
                        string body = await pingResp.Content.ReadAsStringAsync(pingCts.Token);
                        if (body.Contains("UNAUTHENTICATED", StringComparison.OrdinalIgnoreCase))
                        {
                            _fileService.Log($"[Gemini] Server local {apiUrl} báo UNAUTHENTICATED (chưa đăng nhập). Bỏ qua để không treo ứng dụng.", "WARN");
                            return null;
                        }
                    }
                }
                catch { }
            }

            string[] candidateModels;
            if (isCustomApi)
            {
                candidateModels = new[] { !string.IsNullOrWhiteSpace(model) ? model : "gemini-3.1-flash-lite", "fbb127bbb056c959", "9d8ca3786ebdfbea" };
            }
            else
            {
                // Danh sách model ưu tiên cho Google Gemini API chính thức (Gemini 3.1 & 3.5 & Flash Lite)
                string preferred = !string.IsNullOrWhiteSpace(model) ? model : "gemini-3.1-flash-lite";
                candidateModels = new[] {
                    preferred,
                    "gemini-3.1-flash-lite",
                    "gemini-3.5-flash",
                    "gemini-flash-lite-latest",
                    "gemini-2.5-flash",
                    "gemini-1.5-flash",
                    "gemini-flash-latest"
                }.Distinct().ToArray();
            }

            foreach (var targetModel in candidateModels)
            {
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

                    // 1. Gửi trực tiếp các khung hình video (Multimodal Vision) nếu có (tối đa 4 ảnh để phản hồi nhanh)
                    if (keyframeBase64List != null && keyframeBase64List.Count > 0)
                    {
                        foreach (var b64 in keyframeBase64List.Take(4))
                        {
                            partsList.Add(new
                            {
                                inline_data = new
                                {
                                    mime_type = "image/jpeg",
                                    data = b64
                                }
                            });
                        }
                    }
                    else if (!string.IsNullOrEmpty(fileUri) && !string.IsNullOrEmpty(mimeType))
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

                    object genConfig = forceJson
                        ? new { temperature = 0.72, response_mime_type = "application/json" }
                        : new { temperature = 0.72 };

                    requestPayload = new
                    {
                        contents = new[]
                        {
                            new { parts = partsList.ToArray() }
                        },
                        generationConfig = genConfig
                    };
                }

                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    int timeoutSeconds = (keyframeBase64List != null && keyframeBase64List.Count > 0) || !string.IsNullOrEmpty(fileUri) ? 55 : 35;
                    cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

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
                    else
                    {
                        _fileService.Log($"[Gemini] Model {targetModel} trả về mã {resp.StatusCode}: {text}", "WARN");
                    }
                }
                catch (Exception ex)
                {
                    _fileService.Log($"CallGeminiAsync model {targetModel} ngoại lệ: {ex.Message}", "WARN");
                }
            }

            return null;
        }

        public static string CleanAndTranslateSubject(string? rawTopic)
        {
            if (string.IsNullOrWhiteSpace(rawTopic))
            {
                return "khoảnh khắc độc đáo này";
            }

            string s = rawTopic.Trim();
            // Xóa phần mở rộng tệp (.mp4, .mov,...)
            s = Regex.Replace(s, @"\.(mp4|mov|avi|mkv|webm)$", "", RegexOptions.IgnoreCase).Trim();
            // Xóa các hashtag phổ biến (#shorts, #viral, #tiktok, #foryou, #fyp,...)
            s = Regex.Replace(s, @"#\w+", "").Trim();
            // Xóa các ký tự phân cách rác ở cuối như |, -, _
            s = Regex.Replace(s, @"[\s\|\-_]+$", "").Trim();

            if (string.IsNullOrWhiteSpace(s))
            {
                return "khoảnh khắc độc đáo này";
            }

            // Nếu có chữ "chưa xác định" hoặc "chưa có"
            if (s.Contains("chưa xác định", StringComparison.OrdinalIgnoreCase) || s.Contains("chưa có", StringComparison.OrdinalIgnoreCase))
            {
                return "khoảnh khắc thú vị này";
            }

            // Kiểm tra xem chuỗi có chứa dấu tiếng Việt hay không
            bool hasVietnameseDiacritics = Regex.IsMatch(s, @"[àáảãạăằắẳẵặâầấẩẫậèéẻẽẹêềếểễệìíỉĩịòóỏõọôồốổỗộơờớởỡợùúủũụưừứửữựỳýỷỹỵđ]", RegexOptions.IgnoreCase);
            if (hasVietnameseDiacritics)
            {
                return s;
            }

            // Nếu là tiếng Anh hoặc slug không dấu, chuyển ngữ thông minh dựa trên từ khóa nhận diện
            string lower = s.ToLowerInvariant();
            if (lower.Contains("moment") || lower.Contains("caught on camera") || lower.Contains("incredible"))
                return "khoảnh khắc đáng kinh ngạc được camera ghi lại này";
            if (lower.Contains("parkour") || lower.Contains("game"))
                return "màn trình diễn kỹ năng đỉnh cao như trong game thực tế này";
            if (lower.Contains("satisfying") || lower.Contains("oddly") || lower.Contains("crystal") || lower.Contains("fruit"))
                return "màn cắt gọt cực kỳ thỏa mãn thị giác này";
            if (lower.Contains("domino") || lower.Contains("brick") || lower.Contains("wall") || lower.Contains("paving"))
                return "kỹ thuật xếp gạch hiệu ứng domino siêu chuẩn xác này";
            if (lower.Contains("craft") || lower.Contains("diy") || lower.Contains("wood") || lower.Contains("handmade") || lower.Contains("build"))
                return "quá trình chế tác thủ công tinh xảo này";
            if (lower.Contains("magic") || lower.Contains("trick") || lower.Contains("illusion"))
                return "mẹo ảo thuật và tài năng độc lạ này";
            if (lower.Contains("science") || lower.Contains("experiment") || lower.Contains("physics"))
                return "thí nghiệm khoa học đầy bất ngờ này";
            if (lower.Contains("animal") || lower.Contains("cat") || lower.Contains("dog") || lower.Contains("pet"))
                return "hành động siêu đáng yêu của loài động vật này";
            if (lower.Contains("snaptik") || lower.Contains("tiktok") || lower.Length < 4)
                return "tình huống đầy bất ngờ này";

            return "cảnh tượng độc đáo này";
        }

        private static string GenerateFallbackScript(VideoContentAnalysis analysis, NarrationPersonaConfig config)
        {
            if (config.IsEnglish)
            {
                string topicClean = !string.IsNullOrWhiteSpace(analysis.VideoTopic) ? analysis.VideoTopic.Trim() : "this scene";
                topicClean = Regex.Replace(topicClean, @"#\w+", "").Trim();
                if (string.IsNullOrWhiteSpace(topicClean)) topicClean = "this incredible moment";

                var engSb = new StringBuilder();
                engSb.Append($"Look closely at {topicClean.ToLowerInvariant()}, and you will see something truly extraordinary right from the start. ");
                engSb.Append("Every single movement is executed with unbelievable precision and skill. ");
                engSb.Append("What looks effortless on the surface actually requires immense practice and dedication. ");
                engSb.Append("And the final outcome is just so satisfying to watch! ");
                engSb.Append("Make sure to subscribe for more fascinating videos!");
                return engSb.ToString();
            }

            string vietnameseSubject = CleanAndTranslateSubject(analysis.MainSubject);
            if (vietnameseSubject.Equals("khoảnh khắc độc đáo này", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(analysis.VideoTopic))
            {
                vietnameseSubject = CleanAndTranslateSubject(analysis.VideoTopic);
            }

            var sb = new StringBuilder();
            sb.Append($"Nhìn vào {vietnameseSubject}, bạn sẽ thấy sự cuốn hút bắt đầu ngay từ những giây đầu tiên. ");
            sb.Append("Từng thao tác được thực hiện một cách cực kỳ chuẩn xác và dứt khoát, khiến người xem khó có thể rời mắt. ");
            sb.Append("Không chỉ đơn thuần là một khoảnh khắc ấn tượng, mà đằng sau đó là sự khéo léo và tính toán vô cùng tỉ mỉ. ");
            sb.Append("Và kết quả cuối cùng chắc chắn sẽ khiến bạn phải trầm trồ vì độ mượt mà đến khó tin! ");
            sb.Append("Đừng quên bấm đăng ký kênh để đón xem những video thú vị tiếp theo nhé!");
            return sb.ToString();
        }

        public static string EnsureSubscribeCallToAction(string script, bool isEnglish = false)
        {
            if (string.IsNullOrWhiteSpace(script))
            {
                return isEnglish
                    ? "Make sure to subscribe for more fascinating videos!"
                    : "Đừng quên bấm đăng ký kênh để đón xem những video thú vị tiếp theo nhé!";
            }

            string clean = script.Trim();
            string lower = clean.ToLowerInvariant();

            // Nếu đã có lời kêu gọi đăng ký kênh hoặc follow thì giữ nguyên
            if (lower.Contains("đăng ký") || lower.Contains("dang ky") || lower.Contains("follow") || lower.Contains("theo dõi") || lower.Contains("subscribe"))
            {
                return clean;
            }

            // Đảm bảo dấu câu hợp lệ trước khi ghép câu CTA
            if (!clean.EndsWith(".") && !clean.EndsWith("!") && !clean.EndsWith("?"))
            {
                clean += ".";
            }

            string cta = isEnglish
                ? "Make sure to subscribe for more fascinating videos!"
                : "Đừng quên bấm đăng ký kênh để đón xem những video thú vị tiếp theo nhé!";

            return $"{clean} {cta}";
        }

        private static ScriptQualityEvaluation EvaluateHeuristically(string script, NarrationPersonaConfig config)
        {
            var eval = new ScriptQualityEvaluation();
            int wordCount = CountWords(script);

            eval.DurationFit = (wordCount >= config.MinWords && wordCount <= config.MaxWords) ? 9.0 : 7.0;

            string lower = script.ToLowerInvariant();
            bool hasCliche = lower.Contains("chào mừng") || lower.Contains("bạn sẽ không tin") || lower.Contains("hãy like") ||
                             lower.Contains("thánh đường") || lower.Contains("tâm hồn") || lower.Contains("che chở") ||
                             lower.Contains("kỳ diệu") || lower.Contains("ảo diệu") || lower.Contains("thiên tài của");
            eval.ClicheFree = hasCliche ? 5.5 : 9.0;
            eval.HookSpecificity = (lower.StartsWith("đây là") || lower.StartsWith("tại sao") || lower.StartsWith("nhìn") || lower.StartsWith("không phải") || lower.StartsWith("vừa")) ? 9.5 : 8.0;

            eval.AverageScore = (eval.HookSpecificity + eval.LogicalFlow + eval.InformationDensity +
                                 eval.ClicheFree + eval.SpokenNaturalness + eval.VisualAccuracy +
                                 eval.PayoffSatisfaction + eval.DurationFit) / 8.0;

            eval.Passed = eval.AverageScore >= 7.5 && eval.ClicheFree >= 7.0;
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

            // 1. Kiểm tra nếu phản hồi trả về đối tượng JSON hoặc chứa khối JSON
            int firstBrace = s.IndexOf('{');
            int lastBrace = s.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                try
                {
                    string jsonCandidate = s.Substring(firstBrace, lastBrace - firstBrace + 1);
                    using var doc = JsonDocument.Parse(jsonCandidate);
                    var root = doc.RootElement;
                    string[] candidateKeys = { "revisedScript", "finalScript", "script", "narration", "text", "content" };
                    foreach (var key in candidateKeys)
                    {
                        if (root.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
                        {
                            string? val = prop.GetString();
                            if (!string.IsNullOrWhiteSpace(val) && val.Trim().Length > 10)
                            {
                                return CleanNarrationScript(val);
                            }
                        }
                    }
                }
                catch { }
            }

            // 2. Xóa code block markdown ```json hoặc ```
            s = Regex.Replace(s, @"```[a-zA-Z]*\s*", "", RegexOptions.Multiline);
            s = Regex.Replace(s, @"```\s*$", "", RegexOptions.Multiline);

            // 3. Xóa dấu ngoặc kép bao bọc toàn bộ chuỗi nếu có
            if ((s.StartsWith("\"") && s.EndsWith("\"")) || (s.StartsWith("'") && s.EndsWith("'")))
            {
                s = s.Substring(1, s.Length - 2).Trim();
            }

            // 4. Loại bỏ lời chào hỏi / lời dẫn xã giao của trợ lý AI (AI Chatbot Preamble)
            // Ví dụ: "Chào bạn, với tư cách là Chuyên gia sửa lỗi kịch bản... Dưới đây là kịch bản hoàn chỉnh... KỊCH BẢN HOÀN CHỈNH, "
            string lower = s.ToLowerInvariant();
            if (lower.StartsWith("chào ") || lower.StartsWith("xin chào") ||
                lower.StartsWith("với tư cách") || lower.StartsWith("tôi là") ||
                lower.StartsWith("dưới đây là") || lower.StartsWith("sau đây là") ||
                lower.StartsWith("đây là kịch bản"))
            {
                string[] markerPatterns = new[]
                {
                    @"(?:dưới\s+đây|sau\s+đây|đây)\s+là\s+kịch\s+bản[^\.,\n\?!]*[\.,:\n\?!]*",
                    @"kịch\s+bản\s+(?:hoàn\s+chỉnh|chi\s+tiết|mới|sau\s+khi\s+(?:tinh\s+chỉnh|chỉnh\s+sửa))[^\.,\n\?!]*[\.,:\n\?!]*",
                    @"lời\s+thoại\s+(?:hoàn\s+chỉnh|mới)[^\.,\n\?!]*[\.,:\n\?!]*",
                    @"kịch\s+bản\s*:\s*",
                    @"lời\s+thoại\s*:\s*",
                    @"narration\s*:\s*",
                    @"voiceover\s*:\s*"
                };

                int bestCutIdx = -1;
                int searchWindow = Math.Min(s.Length, 600);
                string sub = s.Substring(0, searchWindow);

                foreach (var mp in markerPatterns)
                {
                    var matches = Regex.Matches(sub, mp, RegexOptions.IgnoreCase);
                    foreach (Match m in matches)
                    {
                        if (m.Index + m.Length > bestCutIdx)
                        {
                            bestCutIdx = m.Index + m.Length;
                        }
                    }
                }

                if (bestCutIdx > 0 && bestCutIdx < s.Length)
                {
                    s = s.Substring(bestCutIdx).Trim();
                }
                else
                {
                    var matchFirstSent = Regex.Match(sub, @"^.*?(?:[\.\n\?!]+)\s*");
                    if (matchFirstSent.Success && matchFirstSent.Length < s.Length)
                    {
                        s = s.Substring(matchFirstSent.Length).Trim();
                    }
                }
            }

            // 5. Xóa các tiền tố, tiêu đề thừa ở đầu kịch bản
            s = Regex.Replace(s, @"^(?:(?:\*\*|###?|===)?\s*(?:kịch\s+bản\s+(?:hoàn\s+chỉnh|chi\s+tiết|mới)?|lời\s+thoại\s+(?:hoàn\s+chỉnh|mới)?|kịch\s+bản|lời\s+thoại|narration|script)\s*(?:\*\*|###?|===)?[:,\-–\.]*\s*)+", "", RegexOptions.IgnoreCase).Trim();
            s = Regex.Replace(s, @"^[:,\-–\.]+\s*", "").Trim();

            // 6. Bỏ các ngoặc vuông ghi chú thời gian nếu có [00:00 - 00:05]
            s = Regex.Replace(s, @"\[\s*\d{1,2}:\d{2}(?:\.\d+)?\s*(?:-|–|to)\s*\d{1,2}:\d{2}(?:\.\d+)?\s*\]", "").Trim();

            return s.Trim();
        }
    }
}

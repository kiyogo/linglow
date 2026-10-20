using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using VideoNarrator.Models;

namespace VideoNarrator.Services.PersonalityEngine
{
    public interface ISystemPromptBuilder
    {
        CompiledPrompt BuildSystemPrompt(
            PersonaProfile persona,
            string outputMode,
            double durationSeconds,
            string? topic,
            string? customUserInstructions,
            List<VideoScene>? detectedScenes = null,
            List<FewShotExample>? fewShotExamples = null);

        int EstimateTokenCount(string text);
        double EstimateCostUsd(int inputTokens, int outputTokens, string model);
        string RedactSensitiveData(string text);
    }

    public class SystemPromptBuilder : ISystemPromptBuilder
    {
        private readonly IStyleEngine _styleEngine;
        private readonly IFewShotManager _fewShotManager;

        public SystemPromptBuilder(IStyleEngine styleEngine, IFewShotManager fewShotManager)
        {
            _styleEngine = styleEngine;
            _fewShotManager = fewShotManager;
        }

        public CompiledPrompt BuildSystemPrompt(
            PersonaProfile persona,
            string outputMode,
            double durationSeconds,
            string? topic,
            string? customUserInstructions,
            List<VideoScene>? detectedScenes = null,
            List<FewShotExample>? fewShotExamples = null)
        {
            if (persona == null) throw new ArgumentNullException(nameof(persona));
            if (durationSeconds <= 0) durationSeconds = 30.0;

            var sbSystem = new StringBuilder();
            var sbUser = new StringBuilder();

            // =========================================================================
            // LAYER 1: QUY TẮC NỀN TẢNG HỆ THỐNG & 4 TRỤ CỘT VÀNG (CORE SYSTEM & 4 PILLARS)
            // =========================================================================
            sbSystem.AppendLine("BẠN LÀ MỘT HỆ THỐNG BIÊN KỊCH VIDEO SHORTS/TIKTOK TRIỆU VIEW HÀNG ĐẦU.");
            sbSystem.AppendLine("BỘ 4 TRỤ CỘT VĂN PHONG BẮT BUỘC (MANDATORY 4 CORE PILLARS):");
            sbSystem.AppendLine("1. TRỤ CỘT 1: BỎ HOÀN TOÀN GIỌNG MARKETING / REVIEW CÔNG NGHIỆP / VĂN MẪU AI:");
            sbSystem.AppendLine("   ❌ TUYỆT ĐỐI CẤM các câu triết lý mở đầu sáo rỗng: 'Gia vị tình cảm tuổi học trò...', 'Hành trình khám phá...', 'Cuộc sống xung quanh ta...', 'Mới nhìn cứ ngỡ cảnh tượng bình thường, nhưng nếu để ý kỹ...'.");
            sbSystem.AppendLine("   ❌ CẤM các từ PR sáo rỗng, trừu tượng: 'đỉnh cao', 'vô cùng thú vị', 'cực kỳ bất ổn', 'đầy bất ngờ', 'tuyệt vời', 'kỳ diệu', 'không thể tin nổi', 'chắc chắn bạn sẽ thích', 'đẳng cấp của sự chuyên nghiệp', 'tính toán tỉ mỉ đến từng milimét'.");
            sbSystem.AppendLine("   ❌ CẤM các câu robot cliches: 'Chào mừng các bạn đến với video...', 'Trong video này chúng ta có thể thấy...', 'Hãy cùng theo dõi đến cuối...', 'Bạn sẽ không tin vào mắt mình đâu...', 'Đừng chớp mắt...', 'Nhưng rồi điều bất ngờ đã xảy ra...'.");
            sbSystem.AppendLine("   ❌ TUYỆT ĐỐI CẤM các từ ngữ xàm, từ tối nghĩa, từ ngữ gượng gạo, từ ngữ hiếm gặp ít người dùng hoặc dịch thô vụng về. Chỉ dùng từ ngữ tự nhiên, đắt giá, dễ hiểu và cuốn hút của đời sống thực.");
            sbSystem.AppendLine("2. TRỤ CỘT 2: THÊM GÓC NHÌN CÁ NHÂN NGƯỜI THẬT & TUYỆT ĐỐI CẤM RẬP KHUÔN (AUTHENTIC SPECTATOR POV & ANTI-CLICHÉ):");
            sbSystem.AppendLine("   ✔️ Hãy nhập vai một người bạn thật đang ngồi cạnh người xem, vừa nhìn vào màn hình vừa thốt lên bình luận tự nhiên, táo bạo, trong video có gì nói thẳng cái đó.");
            sbSystem.AppendLine("   ❌ TUYỆT ĐỐI CẤM RẬP KHUÔN MẪU CÂU CỐ ĐỊNH TRÊN CÁC VIDEO:");
            sbSystem.AppendLine("      - CẤM mọi video đều bắt đầu bằng 'Nhìn quả... kìa!' hoặc 'Nhìn cái... kia kìa!'.");
            sbSystem.AppendLine("      - CẤM lạm dụng liên từ khuôn mẫu 'Cứ ngỡ... ai dè...', 'Ban đầu cứ tưởng... ai ngờ...'.");
            sbSystem.AppendLine("      - CẤM lặp đi lặp lại các câu kết máy móc: 'cười không ngậm được mồm', 'bất lực giùm', 'cười nghiêng ngả', 'quê độ'.");
            sbSystem.AppendLine("      - HÃY ĐA DẠNG HÓA LỐI MỞ ĐẦU VÀ KỂ CHUYỆN: Mở đầu bằng phản ứng tự nhiên trước tình huống, một lời cảm thán, hoặc dẫn dắt thẳng vào hành động như người thật đang trò chuyện.");
            sbSystem.AppendLine("3. TRỤ CỘT 3: CHỈNH NHỊP CÂU DYNAMIC & CHUẨN ĐOẠN NGẮT NGHỈ (STACCATO VS. FLOW & TIMING):");
            sbSystem.AppendLine("   ✔️ Tuyệt đối không viết một chuỗi các câu dài đều đều.");
            sbSystem.AppendLine("   ✔️ Áp dụng nhịp điệu tương phản tự nhiên: Câu ngắn mở màn giật mình xen kẽ câu miêu tả hành động chi tiết và cú chốt hạ đắt giá.");
            sbSystem.AppendLine("   ✔️ Dùng dấu chấm (.) và dấu phẩy (,) chuẩn xác để giọng AI TTS ngắt nhịp thở tự nhiên, phát âm rõ từng từ, không bị nuốt chữ hay đọc liến thoắng.");
            sbSystem.AppendLine("4. TRỤ CỘT 4: PHONG CÁCH 'THẾ GIỚI CÓ GÌ HAY' & GIỮ CHÂN NGƯỜI XEM ĐỈNH CAO (HIGH-RETENTION CURIOSITY STORYTELLING):");
            sbSystem.AppendLine("   ✔️ Lời thoại thuyết minh PHẢI PHÙ HỢP HOÀN TOÀN VỚI BỐI CẢNH CỦA VIDEO, phân tích cơ chế, mánh khóe hoặc sự thật đời sống một cách gãy gọn, thông minh, giàu hình ảnh.");
            sbSystem.AppendLine("   ❌ TUYỆT ĐỐI CẤM NÓI ĐẠO LÝ, VĂN MẪU TRIẾT LÝ DẠY ĐỜI: Cấm triệt để các kiểu 'kẻ bình thường và một bậc thầy', 'mọi giới hạn sinh ra để phá vỡ', 'đỉnh cao không nằm ở đỉnh núi'. Đây là văn mẫu rác gây nhàm chán.");
            sbSystem.AppendLine("   ✔️ Tự do dùng từ ngữ phong phú, phóng khoáng, biểu cảm mạnh, không giới hạn khuôn mẫu; ngôn từ đắt giá, bóc tách cơ chế và hiện tượng khiến người xem phải 'A ha, hóa ra là thế!'.");
            sbSystem.AppendLine("   ✔️ Giữ nhịp kịch tính liên tục: Mở đầu mở nút (Open Loop), giữa video giải mã cơ chế, và kết thúc bằng CTA sáng tạo hoặc cú chốt bất ngờ.");
            sbSystem.AppendLine();

            // =========================================================================
            // LAYER 2: ĐỊNH DẠNG ĐẦU RA BẮT BUỘC (MANDATORY OUTPUT FORMAT RULES)
            // =========================================================================
            sbSystem.AppendLine("QUY ĐỊNH ĐỊNH DẠNG ĐẦU RA (OUTPUT FORMAT RULES):");
            AppendOutputFormatRules(sbSystem, outputMode);
            sbSystem.AppendLine();

            // =========================================================================
            // LAYER 3: THIẾT LẬP PERSONA ĐƯỢC CHỌN (PERSONA IDENTITY & DIRECTIVES)
            // =========================================================================
            sbSystem.AppendLine("=== ĐẶC TRƯNG PERSONA HIỆN TẠI (ACTIVE PERSONA PROFILE) ===");
            sbSystem.AppendLine($"• TÊN PERSONA: {persona.Name}");
            sbSystem.AppendLine($"• MÔ TẢ TÍNH CÁCH: {persona.Description}");
            sbSystem.AppendLine($"• GIỌNG ĐỌC & TÔNG VĂN: {persona.VoiceTone}");

            if (!string.IsNullOrWhiteSpace(persona.SystemPromptOverride))
            {
                sbSystem.AppendLine();
                sbSystem.AppendLine("• CHỈ THỊ RIÊNG CỦA PERSONA NÀY:");
                sbSystem.AppendLine(persona.SystemPromptOverride.Trim());
            }

            if (persona.CustomRules != null && persona.CustomRules.Count > 0)
            {
                sbSystem.AppendLine();
                sbSystem.AppendLine("• CÁC NGUYÊN TẮC HÀNH VI ĐẶC BIỆT:");
                foreach (var rule in persona.CustomRules)
                {
                    sbSystem.AppendLine($"  - {rule}");
                }
            }
            sbSystem.AppendLine();

            // =========================================================================
            // LAYER 4: CHỈ DẪN TỪ BỘ STYLE ENGINE (SLIDERS & RHYTHM DIRECTIVES)
            // =========================================================================
            string styleDirectives = _styleEngine.CompileStyleDirectives(persona.StyleConfig);
            sbSystem.AppendLine(styleDirectives);
            sbSystem.AppendLine();

            // =========================================================================
            // LAYER 5: QUY TẮC CẤU TRÚC KỂ CHUYỆN 4 BƯỚC VÀNG (THẾ GIỚI CÓ GÌ HAY / SHORTS TRIỆU VIEW)
            // =========================================================================
            sbSystem.AppendLine("=== NGUYÊN TẮC CẤU TRÚC KỂ CHUYỆN 4 BƯỚC VÀNG (THẾ GIỚI CÓ GÌ HAY / SHORTS TRIỆU VIEW) ===");
            sbSystem.AppendLine("1. HOOK (0-2s): Mở đầu trực diện bằng sự thật hoặc hành vi dị thường ('Đây là cách một số người gian lận trên...', 'Tại sao người ta lại đổ hàng triệu...', 'Điều gì sẽ xảy ra nếu...'). CẤM chào hỏi, CẤM triết lý sáo rỗng, CẤM 'Mới nhìn cứ ngỡ...'.");
            sbSystem.AppendLine("2. CONTEXT & CONSTRAINT (2-5s): Nêu lý do, quy định, rào cản thời gian hoặc áp lực thực tế dẫn đến tình huống (ngắn gọn, logic, chính xác).");
            sbSystem.AppendLine("3. MECHANISM & REVEAL (5-12s): Bóc tách chi tiết công cụ, mánh khóe, từng cử chỉ và chuyển động vi mô trên màn hình một cách chân thực, sinh động.");
            sbSystem.AppendLine("4. ENDING & CREATIVE CTA (12-15s): Cú chốt hạ đắt giá, hoặc câu kêu gọi hành động sáng tạo gắn liền với đồ vật ('Biến nút thích của tôi thành bóng golf', 'Biến nút thích thành thanh socola'). TUYỆT ĐỐI CẤM câu xin like ăn xin sáo rỗng ('nhớ like và subscribe cho mình nhé')!");
            sbSystem.AppendLine();

            // =========================================================================
            // LAYER 6: VÍ DỤ MINH HỌA (FEW-SHOT LEARNING)
            // =========================================================================
            var examples = fewShotExamples ?? _fewShotManager.GetRelevantExamples(topic, persona.Id, 2);
            if (examples != null && examples.Count > 0)
            {
                string fewShotSection = _fewShotManager.FormatFewShotPromptSection(examples);
                sbSystem.AppendLine(fewShotSection);
                sbSystem.AppendLine();
            }

            // =========================================================================
            // LAYER 7: USER PROMPT & YÊU CẦU CỤ THỂ CỦA NGƯỜI DÙNG (CONTEXT & PROMPT)
            // =========================================================================
            sbUser.AppendLine($"THÔNG TIN DỰ ÁN VIDEO:");
            sbUser.AppendLine($"- Thời lượng video mục tiêu: {durationSeconds:F1} giây.");
            double speechTargetSeconds = Math.Max(12.0, durationSeconds - 1.2);
            int minWords = Math.Max(60, (int)Math.Round(speechTargetSeconds * 3.95));
            int targetWords = Math.Max(70, (int)Math.Round(speechTargetSeconds * 4.20));
            int maxWords = (int)Math.Round(speechTargetSeconds * 4.50);
            int targetSentences = Math.Max(4, (int)Math.Round(speechTargetSeconds / 3.4));
            sbUser.AppendLine($"- ĐỘ DÀI LỜI THOẠI BẮT BUỘC: PHẢI DÀI TỪ {minWords} ĐẾN {maxWords} TỪ tiếng Việt (mục tiêu {targetWords} từ, khoảng {targetSentences} câu hoàn chỉnh, tốc độ đọc tự nhiên ~4.1 từ/giây). Giọng đọc PHẢI NÓI LIÊN TỤC từ đầu đến sát cuối video ({speechTargetSeconds:F0}s). CẤM viết ngắn dưới {minWords} từ làm hụt giọng đọc!");
            if (!string.IsNullOrWhiteSpace(topic))
            {
                sbUser.AppendLine($"- Chủ đề video: \"{topic.Trim()}\"");
            }
            else
            {
                sbUser.AppendLine("- Chủ đề: Tự động quan sát và phân tích nội dung trực quan từ video thực tế.");
            }

            if (detectedScenes != null && detectedScenes.Count > 0)
            {
                sbUser.AppendLine("- Các mốc phân cảnh phát hiện từ video:");
                foreach (var sc in detectedScenes)
                {
                    sbUser.AppendLine($"  + [{sc.Start:0.#}s - {sc.End:0.#}s]: {sc.Description}");
                }
            }

            if (!string.IsNullOrWhiteSpace(customUserInstructions))
            {
                sbUser.AppendLine();
                sbUser.AppendLine($"👉 YÊU CẦU RIÊNG TỪ NGƯỜI DÙNG: \"{customUserInstructions.Trim()}\"");
            }

            sbUser.AppendLine();
            sbUser.AppendLine($"HÃY TẠO NỘI DUNG THEO ĐÚNG PERSONA \"{persona.Name}\" VÀ ĐỊNH DẠNG YÊU CẦU BÊN TRÊN.");

            string fullSystemInstruction = sbSystem.ToString().Trim();
            string fullUserPrompt = sbUser.ToString().Trim();

            int estTokens = EstimateTokenCount(fullSystemInstruction + " " + fullUserPrompt);
            double estCost = EstimateCostUsd(estTokens, 500, "gemini-3.8-flash");

            return new CompiledPrompt
            {
                PersonaId = persona.Id,
                PersonaName = persona.Name,
                SystemInstruction = fullSystemInstruction,
                UserPrompt = fullUserPrompt,
                OutputMode = outputMode,
                EstimatedTokens = estTokens,
                EstimatedCostUsd = estCost,
                RedactedPreview = RedactSensitiveData(fullSystemInstruction)
            };
        }

        private static void AppendOutputFormatRules(StringBuilder sb, string outputMode)
        {
            switch (outputMode)
            {
                case "script_only":
                    sb.AppendLine("• CHẾ ĐỘ: script_only (CHỈ TRẢ LỜI LỜI THOẠI)");
                    sb.AppendLine("• YÊU CẦU TUYỆT ĐỐI:");
                    sb.AppendLine("  - CHỈ TRẢ VỀ DUY NHẤT LỜI THOẠI THUYẾT MINH THUẦN TÚY (RAW SCRIPT TEXT).");
                    sb.AppendLine("  - KHÔNG viết lời chào mừng, KHÔNG giải thích ngoài lề, KHÔNG gắn tiêu đề như 'Kịch bản:', KHÔNG dùng dấu ngoặc chú thích [nhạc nền].");
                    sb.AppendLine("  - Lời thoại viết thành một luồng chữ liên tục, bắt tai, giàu hình ảnh.");
                    break;

                case "structured_script":
                    sb.AppendLine("• CHẾ ĐỘ: structured_script (KỊCH BẢN CẤU TRÚC JSON)");
                    sb.AppendLine("• YÊU CẦU: Trả về DUY NHẤT một chuỗi JSON hợp lệ (không markdown bao quanh) theo cấu trúc sau:");
                    sb.AppendLine(@"{
  ""hook"": ""Câu hook mở đầu 0-3 giây gây tò mò cực độ..."",
  ""buildup"": ""Đoạn dẫn dắt bối cảnh và trì hoãn hé lộ thông tin..."",
  ""escalation"": ""Đoạn cao trào dồn dập, đẩy mạnh sự kịch tính hoặc chi tiết độc lạ..."",
  ""payoff"": ""Khoảnh khắc hé lộ kết quả đắt giá, thỏa mãn thị giác..."",
  ""ending"": ""Câu kết hạ màn bất ngờ hoặc móc nối vòng lặp..."",
  ""full_narration"": ""Toàn bộ bài thuyết minh hoàn chỉnh ghép từ các phần trên..."",
  ""estimated_duration_seconds"": 35.0
}");
                    break;

                case "video_generation_prompt":
                    sb.AppendLine("• CHẾ ĐỘ: video_generation_prompt (PROMPT TẠO VIDEO CHO MODEL AI)");
                    sb.AppendLine("• YÊU CẦU: Trả về DUY NHẤT một chuỗi JSON hợp lệ theo cấu trúc sau:");
                    sb.AppendLine(@"{
  ""video_title"": ""Tiêu đề video"",
  ""overall_visual_style"": ""Phong cách hình ảnh tổng thể (cinematic, photorealistic, 4k, moody lighting...)"",
  ""aspect_ratio"": ""9:16"",
  ""scenes"": [
    {
      ""scene_index"": 1,
      ""timestamp_start"": 0.0,
      ""timestamp_end"": 3.0,
      ""visual_prompt"": ""Mô tả hình ảnh chi tiết bằng tiếng Anh chuẩn cho Runway/Sora/Kling..."",
      ""camera_motion"": ""Camera zoom-in chậm, góc cận cảnh..."",
      ""lighting_mood"": ""Ánh sáng kịch tính, tương phản cao..."",
      ""narration_line"": ""Câu thuyết minh tương ứng của cảnh này""
    }
  ]
}");
                    break;

                case "full_video_package":
                default:
                    sb.AppendLine("• CHẾ ĐỘ: full_video_package (GÓI BIÊN TẬP ĐẦY ĐỦ VIDEO SHORTS)");
                    sb.AppendLine("• YÊU CẦU: Trả về DUY NHẤT một chuỗi JSON hợp lệ theo cấu trúc EditingPlan:");
                    sb.AppendLine(@"{
  ""stylePreset"": ""TikTok Shorts"",
  ""videoFormat"": ""9:16"",
  ""hook"": ""Câu hook mở đầu trong 1-3 giây..."",
  ""narrationScript"": ""Toàn bộ kịch bản thuyết minh liền mạch..."",
  ""focusX"": 0.5,
  ""focusY"": 0.5,
  ""scenes"": [
    {
      ""id"": ""scene_01"",
      ""start"": 0.0,
      ""end"": 3.5,
      ""description"": ""Cảnh mở màn"",
      ""importance"": ""High"",
      ""importanceScore"": 0.95,
      ""focusX"": 0.5,
      ""focusY"": 0.5
    }
  ],
  ""subtitles"": [
    {
      ""start"": 0.0,
      ""end"": 2.5,
      ""text"": ""Câu phụ đề 1"",
      ""highlightWords"": [""từ"", ""khóa""]
    }
  ]
}");
                    break;
            }
        }

        public int EstimateTokenCount(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            // Vietnamese & English average ~ 3.5 characters per token with punctuation
            return (int)Math.Ceiling(text.Length / 3.5);
        }

        public double EstimateCostUsd(int inputTokens, int outputTokens, string model)
        {
            // Gemini 1.5 / 2.0 / Flash pricing ballpark: ~$0.075 per 1M input, ~$0.30 per 1M output
            double inputCost = (inputTokens / 1_000_000.0) * 0.075;
            double outputCost = (outputTokens / 1_000_000.0) * 0.30;
            return Math.Round(inputCost + outputCost, 6);
        }

        public string RedactSensitiveData(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            // Redact API keys: AIzaSy..., sk-..., etc.
            string redacted = Regex.Replace(text, @"AIza[0-9A-Za-z\-_]{35}", "[AI_KEY_REDACTED]");
            redacted = Regex.Replace(redacted, @"sk-[0-9A-Za-z\-_]{20,}", "[AI_KEY_REDACTED]");
            return redacted;
        }
    }
}

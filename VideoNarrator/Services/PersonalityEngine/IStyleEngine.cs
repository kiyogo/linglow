using System;
using System.Text;
using VideoNarrator.Models;

namespace VideoNarrator.Services.PersonalityEngine
{
    public interface IStyleEngine
    {
        string CompileStyleDirectives(StyleEngineConfig config);
        string GetCuriosityDirective(int level);
        string GetDramaticDirective(int level);
        string GetWeirdnessDirective(int level);
        string GetSarcasmDirective(int level);
        string GetVisualLanguageDirective(int level);
        string GetHookIntensityDirective(int level);
        string GetSuspenseDirective(int level);
        string GetSatisfactionDirective(int level);
        string GetRhythmDirective(string rhythm);
        string GetEndingDirective(string endingStyle);
        string GetVerbosityDirective(string verbosity);
    }

    public class StyleEngine : IStyleEngine
    {
        public string CompileStyleDirectives(StyleEngineConfig config)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== CHỈ DẪN VĂN PHONG VÀ THỦ THÁP ĐIỀU KHIỂN (STYLE ENGINE DIRECTIVES) ===");

            // 1. Curiosity & Hook
            sb.AppendLine($"[1. MỨC ĐỘ KÍCH THÍCH TÒ MÒ ({config.CuriosityLevel}/100)]:");
            sb.AppendLine(GetCuriosityDirective(config.CuriosityLevel));

            sb.AppendLine($"[2. CƯỜNG ĐỘ MỞ ĐẦU HOOK ({config.HookIntensity}/100)]:");
            sb.AppendLine(GetHookIntensityDirective(config.HookIntensity));

            // 2. Dramatic & Suspense
            sb.AppendLine($"[3. MỨC ĐỘ KỊCH TÍNH ({config.DramaticLevel}/100)]:");
            sb.AppendLine(GetDramaticDirective(config.DramaticLevel));

            sb.AppendLine($"[4. MỨC ĐỘ HỒI HỘP & TRÌ HOÃN THÔNG TIN ({config.SuspenseLevel}/100)]:");
            sb.AppendLine(GetSuspenseDirective(config.SuspenseLevel));

            // 3. Weirdness & Sarcasm
            sb.AppendLine($"[5. ĐỘ LẬP DỊ & KHÓ ĐOÁN ({config.WeirdnessLevel}/100)]:");
            sb.AppendLine(GetWeirdnessDirective(config.WeirdnessLevel));

            sb.AppendLine($"[6. MỨC ĐỘ CHÂM BIẾM & CÀ KHỊA ({config.SarcasmLevel}/100)]:");
            sb.AppendLine(GetSarcasmDirective(config.SarcasmLevel));

            // 4. Visual Language & Satisfaction
            sb.AppendLine($"[7. MỨC ĐỘ GIÀU HÌNH TƯỢNG THỊ GIÁC ({config.VisualLanguageLevel}/100)]:");
            sb.AppendLine(GetVisualLanguageDirective(config.VisualLanguageLevel));

            sb.AppendLine($"[8. ĐỘ THỎA MÃN THỊ GIÁC - ODDLY SATISFYING ({config.SatisfactionLevel}/100)]:");
            sb.AppendLine(GetSatisfactionDirective(config.SatisfactionLevel));

            // 5. Rhythm, Speed, Ending & Verbosity
            sb.AppendLine($"[9. NHỊP ĐIỆU CÂU (RHYTHM: {config.SentenceRhythm})]:");
            sb.AppendLine(GetRhythmDirective(config.SentenceRhythm));

            sb.AppendLine($"[10. KIỂU KẾT THÚC (ENDING: {config.EndingStyle})]:");
            sb.AppendLine(GetEndingDirective(config.EndingStyle));

            sb.AppendLine($"[11. ĐỘ DÀI LỜI THOẠI (VERBOSITY: {config.Verbosity})]:");
            sb.AppendLine(GetVerbosityDirective(config.Verbosity));

            return sb.ToString().Trim();
        }

        public string GetCuriosityDirective(int level)
        {
            if (level >= 80)
                return "• Tò mò cực đại: Luôn giấu kín nguyên nhân cốt lõi trong những giây đầu. Đặt ra những chi tiết bất thường khiến người xem không thể rời mắt và buộc phải xem đến giây cuối cùng để biết sự thật.";
            if (level >= 50)
                return "• Tò mò vừa phải: Tạo gợi ý mở đầu hấp dẫn, hé lộ dần diễn biến theo từng nấc thang.";
            return "• Trực diện: Đi thẳng vào trọng tâm diễn biến, ít che giấu thông tin.";
        }

        public string GetDramaticDirective(int level)
        {
            if (level >= 80)
                return "• Kịch tính đỉnh chóp: Dùng động từ mạnh, nhịp kể dồn dập, đẩy mức độ nguy cơ hoặc sự bất ngờ lên mức cao trào cao nhất.";
            if (level >= 50)
                return "• Kịch tính cân bằng: Tăng dần tốc độ kể ở đoạn giữa, tạo điểm nhấn cao trào rõ ràng.";
            return "• Nhẹ nhàng, thư thái: Giữ tông giọng bình ổn, phân tích khách quan.";
        }

        public string GetWeirdnessDirective(int level)
        {
            if (level >= 75)
                return "• Điên rồ & Khó đoán: Liên tưởng táo bạo, ví von siêu thực hoặc góc nhìn độc lạ mà người thường không bao giờ nghĩ tới. Phá vỡ mọi khuôn mẫu suy nghĩ thông thường.";
            if (level >= 40)
                return "• Độc đáo, hóm hỉnh: Có những góc nhìn dí dỏm, bất ngờ nhưng vẫn dễ hiểu với số đông.";
            return "• Thực tế, chuẩn mực: Bám sát logic tự nhiên của đời sống thường nhật.";
        }

        public string GetSarcasmDirective(int level)
        {
            if (level >= 70)
                return "• Châm biếm sắc lẹm: Cà khịa tinh tế, bóc phốt hài hước những pha xử lý cồng kềnh hoặc biểu cảm khó đỡ, không giữ kẽ.";
            if (level >= 30)
                return "• Hài hước duyên dáng: Nhận xét đáng yêu, tạo tiếng cười tự nhiên như bạn bè trêu nhau.";
            return "• Tôn trọng, nghiêm túc: Hoàn toàn không châm biếm, giữ thái độ trung lập.";
        }

        public string GetVisualLanguageDirective(int level)
        {
            if (level >= 80)
                return "• Giàu hình ảnh cụ thể 100%: Gọi tên chính xác từng vật thể thực tế (màu sắc, nắp chai, hạt bụi, mép bàn), cử chỉ vi mô và hành động liên hoàn. Tuyệt đối CẤM dùng các tính từ trừu tượng sáo rỗng như 'bất ổn', 'thú vị', 'tuyệt vời'.";
            if (level >= 50)
                return "• Tả thực rõ nét: Nêu rõ các hành động và vật dụng cụ thể xuất hiện trong khung hình, tránh phán đoán chung chung.";
            return "• Khái quát: Tập trung vào ý nghĩa hành động hơn là chi tiết bề mặt.";
        }

        public string GetHookIntensityDirective(int level)
        {
            if (level >= 80)
                return "• Hook sấm sét (0-3s): Mở màn giật gân, đập thẳng vào mắt điểm dị biệt nhất của khung hình. TUYỆT ĐỐI CẤM chào hỏi hoặc rào đón!";
            if (level >= 50)
                return "• Hook gợi mở: Đặt câu hỏi hoặc lời dẫn khơi gợi sự tò mò trong 3 giây đầu.";
            return "• Mở đầu tuần tự: Dẫn dắt từ bối cảnh đến nhân vật.";
        }

        public string GetSuspenseDirective(int level)
        {
            if (level >= 75)
                return "• Trì hoãn & Thắt nút: Kéo căng sự chú ý bằng cách chia nhỏ quá trình chuyển giao, chỉ trao cho người xem kết quả ở giây áp chót.";
            if (level >= 40)
                return "• Hồi hộp vừa phải: Giữ nhịp tăng dần đến khoảnh khắc kết quả.";
            return "• Nhịp đều: Không dùng kỹ thuật trì hoãn.";
        }

        public string GetSatisfactionDirective(int level)
        {
            if (level >= 80)
                return "• Thỏa mãn thị giác cực độ (Oddly Satisfying): Tôn vinh khoảnh khắc chính xác, vết cắt hoàn hảo, sự khớp cạ mượt mà hoặc cảm giác giải tỏa trọn vẹn.";
            if (level >= 50)
                return "• Kết quả trọn vẹn: Đem lại sự hài lòng rõ rệt khi kết thúc diễn biến.";
            return "• Tự nhiên: Theo sát kết quả thực tế của câu chuyện.";
        }

        public string GetRhythmDirective(string rhythm)
        {
            return rhythm switch
            {
                "rapid_fire" => "• Nhịp dồn dập (Rapid-fire): Chuỗi câu nói nhanh, liền mạch, động từ mạnh, kết hợp câu ngắn giật mình và câu chốt hạ bất ngờ.",
                "slow_burn" => "• Nhịp rình rập (Slow-burn): Câu từ điềm tĩnh, bí ẩn, nhả từng chi tiết để tạo sức nặng tâm lý.",
                "syncopated" => "• Nhịp đảo phách (Syncopated): Đang nhanh bỗng giật khựng lại ở từ khóa đắt giá hoặc thốt lên cảm xúc cá nhân, tạo hiệu ứng giật mình cuốn hút.",
                _ => "• Nhịp xen kẽ (Alternating): Câu ngắn đanh thép (3-6 từ) mở màn xen kẽ câu dài miêu tả sinh động (10-18 từ) và cú chốt hạ cá nhân, ngắt nghỉ bằng dấu câu chuẩn xác."
            };
        }

        public string GetEndingDirective(string endingStyle)
        {
            return endingStyle switch
            {
                "punchline" => "• Chốt hạ hài hước: Kết thúc bằng một câu bóc phốt hoặc cú twist cười nghiêng ngả.",
                "loop_seamless" => "• Vòng lặp vô tận (Perfect Loop): Câu cuối cùng nối trực tiếp mượt mà vào câu đầu tiên của video để người xem replay liên tục.",
                "cliffhanger" => "• Lửng lơ gây ngẫm: Để lại một câu hỏi mở hoặc thắc mắc lửng lơ kích thích người xem suy ngẫm và tranh luận.",
                "call_to_action" or "payoff_climax" => "• Kêu gọi tương tác triệu view (Viral CTA): Chốt hạ bằng câu đúc kết sâu sắc và câu cầu khiến kêu gọi hành động (thả tim, follow kênh, để lại bình luận) cực bén, đúng chuẩn các kênh Shorts/TikTok triệu view.",
                _ => "• Hack não & Khó đoán (Mind-bending): Kết thúc bằng một nhận xét xoay chuyển 180 độ góc nhìn của người xem."
            };
        }

        public string GetVerbosityDirective(string verbosity)
        {
            return verbosity switch
            {
                "concise" => "• Cô đọng: Rất ít từ, câu ngắn, chắt lọc từng chữ, để không gian cho hình ảnh tự lên tiếng.",
                "detailed" => "• Tỉ mỉ: Miêu tả sâu sắc, giàu liên tưởng, diễn giải trọn vẹn từng cung bậc.",
                _ => "• Cân đối: Vừa đủ số từ tương ứng thời lượng video (trung bình 2.5 - 3 từ/giây), không dày đặc cũng không thừa thãi."
            };
        }
    }
}

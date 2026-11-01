using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using VideoNarrator.Models;

namespace VideoNarrator.Services.PersonalityEngine
{
    public interface IFewShotManager
    {
        IReadOnlyList<FewShotExample> GetAllExamples();
        void AddOrUpdateExample(FewShotExample example);
        void DeleteExample(string id);
        List<FewShotExample> GetRelevantExamples(string? topic, string? personaId, int maxCount = 2);
        string FormatFewShotPromptSection(List<FewShotExample> examples);
        void Save();
        void Load();
    }

    public class FewShotManager : IFewShotManager
    {
        private readonly List<FewShotExample> _examples = new();
        private readonly string _storagePath;
        private readonly object _lock = new();

        public FewShotManager()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "VideoNarrator");
            if (!Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); } catch { }
            }
            _storagePath = Path.Combine(dir, "few_shots.json");
            Load();
        }

        public IReadOnlyList<FewShotExample> GetAllExamples()
        {
            lock (_lock)
            {
                return _examples.ToList();
            }
        }

        public void AddOrUpdateExample(FewShotExample example)
        {
            if (example == null) return;
            lock (_lock)
            {
                int idx = _examples.FindIndex(e => e.Id == example.Id);
                if (idx >= 0)
                {
                    _examples[idx] = example;
                }
                else
                {
                    _examples.Add(example);
                }
                Save();
            }
        }

        public void DeleteExample(string id)
        {
            lock (_lock)
            {
                _examples.RemoveAll(e => e.Id == id);
                Save();
            }
        }

        public List<FewShotExample> GetRelevantExamples(string? topic, string? personaId, int maxCount = 2)
        {
            lock (_lock)
            {
                if (_examples.Count == 0) return new List<FewShotExample>();

                string topicNorm = (topic ?? "").ToLowerInvariant().Trim();
                if (string.IsNullOrWhiteSpace(topicNorm))
                {
                    // Khi không có chủ đề cụ thể, ưu tiên trả về các ví dụ cấu trúc vàng chuẩn
                    // thuộc Persona đang chọn (hoặc the_gioi_co_gi_hay) để AI luôn học theo cấu trúc hấp dẫn!
                    string targetPid = string.IsNullOrWhiteSpace(personaId) ? "the_gioi_co_gi_hay" : personaId;
                    var personaExamples = _examples
                        .Where(e => !string.IsNullOrEmpty(e.PersonaId) && (e.PersonaId == targetPid || (targetPid == "the_gioi_co_gi_hay" && e.PersonaId == "doi_co_gi_hay")))
                        .Take(maxCount)
                        .ToList();

                    if (personaExamples.Count > 0) return personaExamples;

                    var fallback = _examples
                        .Where(e => e.Id.Contains("the_gioi_co_gi_hay") || e.Id.Contains("seed_school_drama") || e.Id.Contains("seed_curiosity"))
                        .Take(maxCount)
                        .ToList();

                    return fallback.Count > 0 ? fallback : _examples.Take(maxCount).ToList();
                }

                var scored = new List<(FewShotExample ex, double score)>();

                foreach (var ex in _examples)
                {
                    double score = 0.0;
                    if (!string.IsNullOrEmpty(ex.Topic))
                    {
                        string exTopicNorm = ex.Topic.ToLowerInvariant();
                        // Kiểm tra trùng lặp từ khóa
                        var words = topicNorm.Split(new[] { ' ', ',', '/', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
                        int matchCount = words.Count(w => w.Length >= 3 && exTopicNorm.Contains(w));

                        if (topicNorm.Contains(exTopicNorm) || exTopicNorm.Contains(topicNorm))
                        {
                            score += 10.0;
                        }
                        else if (matchCount > 0)
                        {
                            score += matchCount * 3.0;
                        }
                    }

                    if (score > 0 && !string.IsNullOrEmpty(personaId) && ex.PersonaId == personaId)
                    {
                        score += 2.0;
                    }

                    if (score >= 3.0)
                    {
                        scored.Add((ex, score));
                    }
                }

                if (scored.Count == 0)
                {
                    var fallback = _examples
                        .Where(e => string.IsNullOrEmpty(personaId) || e.PersonaId == personaId || e.PersonaId == "the_gioi_co_gi_hay" || e.PersonaId == "mad_muse")
                        .Take(Math.Max(1, maxCount))
                        .ToList();
                    return fallback.Count > 0 ? fallback : _examples.Take(Math.Max(1, maxCount)).ToList();
                }

                return scored.OrderByDescending(s => s.score)
                             .Take(Math.Max(1, maxCount))
                             .Select(s => s.ex)
                             .ToList();
            }
        }

        public string FormatFewShotPromptSection(List<FewShotExample> examples)
        {
            if (examples == null || examples.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("=== VÍ DỤ MINH HỌA VĂN PHONG (FEW-SHOT LEARNING EXAMPLES) ===");
            sb.AppendLine("Dưới đây là các ví dụ minh họa cách viết ĐẠT TIÊU CHUẨN và KHÔNG ĐẠT TIÊU CHUẨN để bạn học theo phong cách này:");
            sb.AppendLine();

            for (int i = 0; i < examples.Count; i++)
            {
                var ex = examples[i];
                sb.AppendLine($"--- [VÍ DỤ {i + 1}: Chủ đề \"{ex.Topic}\"] ---");
                sb.AppendLine("❌ MẪU SAI (Văn phong trợ lý AI đại trà / sáo rỗng):");
                sb.AppendLine($"\"{ex.NegativeExample.Trim()}\"");
                sb.AppendLine();
                sb.AppendLine("✔️ MẪU ĐÚNG (Chuẩn phong cách độc đáo, cuốn hút, giàu hình ảnh):");
                sb.AppendLine($"\"{ex.PositiveExample.Trim()}\"");
                sb.AppendLine();
                if (!string.IsNullOrWhiteSpace(ex.Explanation))
                {
                    sb.AppendLine($"💡 GIẢI THÍCH LÝ DO: {ex.Explanation.Trim()}");
                }
                sb.AppendLine();
            }

            return sb.ToString().Trim();
        }

        public void Save()
        {
            try
            {
                var opts = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_examples, opts);
                File.WriteAllText(_storagePath, json, Encoding.UTF8);
            }
            catch { }
        }

        public void Load()
        {
            lock (_lock)
            {
                _examples.Clear();
                if (File.Exists(_storagePath))
                {
                    try
                    {
                        string json = File.ReadAllText(_storagePath, Encoding.UTF8);
                        var loaded = JsonSerializer.Deserialize<List<FewShotExample>>(json);
                        if (loaded != null && loaded.Count > 0)
                        {
                            _examples.AddRange(loaded);
                            // Đồng bộ và nâng cấp các seed examples chuẩn 4 trụ cột
                            var seeds = GetSeedExamples();
                            bool changed = false;
                            foreach (var s in seeds)
                            {
                                int exIdx = _examples.FindIndex(e => e.Id == s.Id);
                                if (exIdx < 0)
                                {
                                    _examples.Add(s);
                                    changed = true;
                                }
                                else
                                {
                                    _examples[exIdx] = s;
                                    changed = true;
                                }
                            }
                            if (changed) Save();
                            return;
                        }
                    }
                    catch { }
                }

                // Default seed examples
                _examples.AddRange(GetSeedExamples());
                Save();
            }
        }

        private static List<FewShotExample> GetSeedExamples()
        {
            return new List<FewShotExample>
            {
                new()
                {
                    Id = "seed_benchmark_ticket_machine",
                    PersonaId = "the_gioi_co_gi_hay",
                    Topic = "Người bán vé tàu tự động / Phản xạ đỉnh cao / Cuộc sống đường phố / Ấn Độ",
                    PositiveExample = "Người đàn ông này không phải nhân viên nhà ga, nhưng ai muốn mua vé tàu nhanh đều phải tìm đến anh. Ở các ga tàu đông đúc tại Ấn Độ, hệ thống máy bán vé tự động có giao diện rất phức tạp khiến nhiều hành khách, nhất là người lớn tuổi, lúng túng không biết thao tác. Đứng túc trực ngay trước máy, anh thuộc lòng từng bước trên hệ thống để thao tác hộ. Khách chỉ cần đưa tiền mặt và báo ga đến, chưa đầy vài giây sau chiếc vé đã được in ra chuẩn xác.",
                    NegativeExample = "Chào mừng các bạn đến với video ngày hôm nay. Ở Ấn Độ máy bán vé rất khó dùng nên một người đàn ông tốt bụng đứng đây giúp mọi người mua vé tàu. Hành động của anh ấy thật ấm áp và đáng khâm phục. Biến nút thích của tôi thành chiếc vé tàu nhé.",
                    Explanation = "Mẫu chuẩn triệu view: Hook tương phản trực diện (Không phải nhân viên nhưng ai cũng cần) -> Điểm nghẽn thực tế (máy phức tạp, khách lúng túng) -> Thao tác cụ thể (thuộc lòng giao diện, bấm hộ) -> Payoff định lượng (chưa đầy vài giây vé in ra). Kết thúc gọn gàng, tuyệt đối không xin like hay dùng câu sáo rỗng."
                },
                new()
                {
                    Id = "seed_benchmark_dolphins_surfing",
                    PersonaId = "the_gioi_co_gi_hay",
                    Topic = "Đàn cá heo lướt sóng mũi tàu / Động vật thông minh / Vật lý đại dương / Cá heo",
                    PositiveExample = "Nhiều người đi tàu biển chắc chắn từng thấy cảnh đàn cá heo cứ thích bơi sát ngay trước mũi thuyền. Nhìn thì tưởng chúng đang đùa giỡn hoặc muốn đua tốc độ, nhưng thực ra đây là cách để chúng tiết kiệm sức. Mũi tàu khổng lồ khi rẽ nước sẽ tạo ra một luồng sóng đẩy cực mạnh về phía trước. Cá heo chỉ việc áp sát thân mình vào luồng nước này để lao đi mà hầu như không cần quẫy đuôi, giống hệt như chúng ta đứng trên một chiếc thang cuốn vậy.",
                    NegativeExample = "Nhìn kìa! Bạn có thấy hàng chục sinh vật đang chuyển động như một thực thể duy nhất không? Chúng ta đang chiêm ngưỡng những thiên tài của đại dương. Chúng là sinh vật có tâm hồn, biết yêu thương. Đại dương chính là thánh đường cần che chở. Biến nút thích của tôi thành cú nhảy cá heo.",
                    Explanation = "Mẫu chuẩn viral: Dẹp bỏ hoàn toàn văn mẫu triết lý đạo đức sến súa (thánh đường, tâm hồn, che chở) và từ ngữ khoa trương lố lăng ('cú hack cực đỉnh', 'tay đua chuyên nghiệp'). Giải thích cơ chế vật lý đẩy của sóng tàu rõ ràng bằng hình ảnh so sánh đời thường (chiếc thang cuốn)."
                },
                new()
                {
                    Id = "seed_benchmark_street_food",
                    PersonaId = "the_gioi_co_gi_hay",
                    Topic = "Xiên que sốt Thái / Ẩm thực đường phố / Món ngon độc lạ / Nấu ăn",
                    PositiveExample = "Xe xiên que này ở chợ Hồ Thị Kỷ lúc nào cũng đông nghịt khách, nhiều khi phải đợi cả nửa tiếng mới tới lượt. Xiên que sau khi chiên vàng giòn sẽ được vớt ra để ráo, rồi rưới đẫm thứ nước sốt Thái chua ngọt sánh kẹo. Nước sốt cay nồng mùi ớt rim và sả phi, quyện vào lớp vỏ ngoài giòn rụm ăn cực kỳ cuốn. Ai ăn cay kém thì vừa ăn vừa hít hà, nhưng đã ăn một que là khó mà dừng lại được.",
                    NegativeExample = "Chào các bạn hôm nay chúng ta cùng ăn xiên que sốt Thái nhé. Món ăn này vô cùng thơm ngon và hấp dẫn, bùng nổ khoang miệng. Hãy biến nút thích thành xiên que để ủng hộ quán nhé.",
                    Explanation = "Tập trung vào miêu tả xúc giác và vị giác chân thực đời thường (chiên vàng giòn, sốt sánh kẹo, cay nồng ớt rim, vừa ăn vừa hít hà), bóc tách điểm nhấn của món ăn, dứt khoát không chào hỏi sáo rỗng hay xin like xàm xí."
                },
                new()
                {
                    Id = "seed_benchmark_satisfying_restoration",
                    PersonaId = "Oddly Satisfying",
                    Topic = "Phục hồi đồ cổ / Sửa chữa cơ khí / Oddly Satisfying / Làm sạch",
                    PositiveExample = "Thoạt nhìn chỉ là một cục sắt gỉ sét đen sì tưởng như vứt đi, nhưng bên trong lại là một chiếc bật lửa cổ từ nửa thế kỷ trước. Người thợ ngâm từng chi tiết vào dung dịch tẩy rỉ, rồi dùng máy đánh bóng tỉ mỉ để thổi bay lớp oxy hóa bám chặt suốt hàng chục năm. Khi lắp ráp lại, từng bánh răng cơ khí khớp vào nhau trơn tru không một tiếng kêu, và ngọn lửa lại bùng lên như vừa xuất xưởng.",
                    NegativeExample = "Trong video này người thợ sẽ phục chế đồ cũ. Đây là màn lột xác khó tin khiến não bạn được chữa lành. Biến nút thích của tôi thành ngọn lửa.",
                    Explanation = "Sự tương phản thị giác rõ ràng (từ gỉ sét thành bóng loáng), miêu tả chuyển động cơ khí tỉ mỉ, kết thúc đẹp mắt, sạch bóng từ ngữ xàm xí."
                },
                new()
                {
                    Id = "seed_the_gioi_co_gi_hay_golf_cheat",
                    PersonaId = "the_gioi_co_gi_hay",
                    Topic = "Gian lận trên sân golf / Mẹo độc lạ / Zack D. Films / Thế Giới Có Gì Hay",
                    PositiveExample = "Đây là cách một số người gian lận trên sân golf mà đối thủ rất khó phát hiện. Theo luật, người chơi chỉ có khoảng 3 phút để tìm bóng, nếu không tìm thấy sẽ bị tính là mất bóng và phạt gậy. Lợi dụng điều đó, họ giấu một thiết bị nhỏ chứa quả bóng phụ ở cổ chân. Giả vờ cúi xuống tìm kiếm trong bụi cỏ, họ chỉ cần khẽ gạt chốt để quả bóng rơi ra, khiến mọi người tin rằng họ vừa tìm thấy bóng thật.",
                    NegativeExample = "Chào mừng các bạn đến với video ngày hôm nay. Đánh golf là một bộ môn thể thao vô cùng đẳng cấp và chuyên nghiệp đòi hỏi sự tính toán tỉ mỉ đến từng milimét. Mới nhìn cứ ngỡ cảnh tượng bình thường nhưng nếu để ý kỹ bạn sẽ thấy điều kỳ diệu. Biến nút thích của tôi thành bóng golf.",
                    Explanation = "Hook mở đầu trực diện bằng sự thật gây tò mò ('Đây là cách...'), nêu ngắn gọn luật chơi thực tế (3 phút tìm bóng), chỉ rõ thiết bị/công cụ độc lạ trên màn hình (thiết bị giấu ở chân), kết thúc tròn trịa ở mánh khóe thực tế."
                },
                new()
                {
                    Id = "seed_the_gioi_co_gi_hay_shade_balls",
                    PersonaId = "the_gioi_co_gi_hay",
                    Topic = "Khoa học kỳ thú / Cơ chế hoạt động / 96 triệu quả bóng đen / Thế Giới Có Gì Hay",
                    PositiveExample = "Tại sao thành phố này lại đổ tới 96 triệu quả bóng nhựa màu đen xuống hồ chứa nước? Thoạt nhìn ai cũng tưởng đây là một sự lãng phí kỳ lạ. Nhưng thực tế, ánh nắng mặt trời khi chiếu trực tiếp vào nước chứa hợp chất Clo sẽ sinh ra chất gây ung thư. Những quả bóng đen này đóng vai trò như một mái che khổng lồ, vừa chặn hoàn toàn tia cực tím, vừa giữ cho hàng triệu lít nước không bị bốc hơi dưới cái nắng sa mạc.",
                    NegativeExample = "Trong video này chúng ta sẽ cùng khám phá một hiện tượng vô cùng thú vị và độc đáo. Các công nhân đang đổ những quả bóng xuống hồ nước. Đây là một phát minh tuyệt vời đỉnh cao công nghệ. Biến nút thích của tôi thành màu đen của quả bóng.",
                    Explanation = "Hook đặt câu hỏi nghịch lý gây chú ý ('Tại sao...'), giải thích bản chất hóa học/vật lý ngắn gọn, giải mã cơ chế hoạt động thực tế như mái che khổng lồ."
                },
                new()
                {
                    Id = "seed_the_gioi_co_gi_hay_lockpick",
                    PersonaId = "the_gioi_co_gi_hay",
                    Topic = "Kỹ năng chuyên gia / Mẹo mở khóa / Đời sống độc lạ",
                    PositiveExample = "Đây là cách một người thợ sửa khóa chuyên nghiệp mở toang cánh cửa chỉ trong vòng 5 giây mà không cần chìa. Những ổ khóa hiện đại thường có nhiều tầng lẫy bi chống trộm cực kỳ phức tạp. Thay vì cố xoay ổ khóa, anh ấy sử dụng một thanh gạt tạo áp lực siêu mỏng kết hợp súng gõ rung điện tử. Mỗi nhịp rung đẩy từng viên bi nhảy lên đúng rãnh chỉ trong tích tắc khiến ổ khóa lập tức bật mở! Bạn đã từng phải gọi thợ phá khóa lúc nửa đêm bao giờ chưa?",
                    NegativeExample = "Người thợ này có kỹ năng vô cùng đỉnh cao và đẳng cấp. Thao tác dứt khoát tính toán tỉ mỉ đến từng milimét. Không một động tác thừa khiến ai nấy đều phải thán phục.",
                    Explanation = "Bóc tách cơ chế bên trong ổ khóa và nguyên lý thanh gạt áp lực, không ca ngợi sáo rỗng vô căn cứ."
                },
                new()
                {
                    Id = "seed_doi_co_gi_hay_chopping_wood",
                    PersonaId = "doi_co_gi_hay",
                    Topic = "Các kiểu bổ củi / Dụng cụ cơ khí độc lạ / Mẹo vặt đời sống",
                    PositiveExample = "Tìm ra một cách đơn giản để bổ củi từ lâu đã là điều được nhiều người quan tâm. Nhưng ngày nay thì sao? Đây là một khung bổ củi được thiết kế để ngay cả người mới cũng có thể dễ dàng chẻ những khúc gỗ. Bạn chỉ cần đặt khúc gỗ vào bên trong khung rồi dùng búa đập xuống. Ngoài ra còn có những phương pháp sử dụng máy móc. Đầu tiên là máy chẻ gỗ. Như bạn đang thấy, nó dùng lực chậm nhưng cực kỳ mạnh để tách khúc gỗ ra. Thứ hai là loại máy chẻ gỗ dạng súng có thể cắt gỗ với tốc độ rất cao nhưng khá nguy hiểm, đòi hỏi người dùng phải thật thành thạo. Cuối cùng là một cách khá đặc biệt: sử dụng một thanh kiếm để bổ củi. Chỉ với một cú chém dứt khoát, ngay cả khúc gỗ to bằng bắp đùi cũng bị chẻ làm đôi!",
                    NegativeExample = "Chào mừng các bạn đến với video ngày hôm nay. Trong video này chúng ta sẽ cùng khám phá các kiểu bổ củi vô cùng tuyệt vời và độc đáo nhé. Hãy cùng theo dõi để xem kết quả thế nào nha!",
                    Explanation = "Kịch bản chuẩn loại bỏ hoàn toàn lời chào sáo rỗng, mở đầu bằng câu hỏi gợi mở nhu cầu thực tế, phân loại các cấp độ công cụ theo nhịp xuất hiện của video, bám sát từng động tác và chốt hạ bằng pha hành động mãn nhãn thị giác."
                },
                new()
                {
                    Id = "seed_doi_co_gi_hay_truck_driving",
                    PersonaId = "doi_co_gi_hay",
                    Topic = "Lái xe đường trường / Cabin xe tải POV / Hành trình giao thông",
                    PositiveExample = "Bạn có bao giờ tự hỏi cảm giác ngồi sau vô lăng một cỗ xe tải hạng nặng di chuyển hàng nghìn cây số ngày đêm sẽ như thế nào chưa? Như bạn đang thấy, góc nhìn từ cabin cao chót vót giúp bao quát toàn bộ con đường phía trước. Bác tài khéo léo ghìm chắc tay lái, lách qua từng khúc cua gắt và những chiếc xe đối diện. Khi màn đêm buông xuống, ánh đèn pha rọi thẳng vào mặt đường ẩm ướt trên cao tốc. Từng thao tác gạt cần số, quan sát gương chiếu hậu đều đòi hỏi sự tập trung cao độ. Một hành trình đơn độc nhưng đầy mê hoặc qua từng cung đường bất tận của những người lái xe đường trường.",
                    NegativeExample = "Chào mừng các bạn đến với video lái xe hôm nay. Xe lớn xe nhỏ vượt qua tầm mắt hiện lên đầy kịch tính. Bạn sẽ thấy rất nhiều điều thú vị và tuyệt vời trên con đường này.",
                    Explanation = "Bám sát góc nhìn cabin xe tải, quan sát vô lăng, gương chiếu hậu, thời tiết và thao tác tay của bác tài. Tuyệt đối không dùng văn mẫu PR sáo rỗng hoặc từ cấm 'hiện lên đầy kịch tính'."
                },
                new()
                {
                    Id = "seed_school_drama_pillars",
                    Topic = "Trêu đùa trong lớp học / Drama tuổi học trò",
                    PositiveExample = "Thanh niên bàn bên vừa giả vờ quay mặt đi là thò tay giật phắt chai nước của con bạn giấu biến ra sau lưng. Đến đoạn lau bảng cũng tranh nhau xóa một góc đến mức bụi phấn bay mù mịt. Vừa nghịch vừa trêu nhau thế này thì ai mà tập trung học cho nổi!",
                    NegativeExample = "Gia vị tình cảm tuổi học trò lúc nào cũng đầy drama. Đang yên đang lành thì lại lấy chai nước chọc ghẹo nhau. Đến giờ lau bảng cũng phải vô tình chung sức xóa chữ. Và màn xào nấu vũ đạo ở cửa lớp cực kỳ bất ổn.",
                    Explanation = "Mẫu đúng triệt tiêu hoàn toàn giọng marketing sáo rỗng, góc nhìn người thật tự nhiên, không rập khuôn mẫu câu 'Nhìn quả... kìa', nhịp câu sống động và bám sát hành động thực tế."
                },
                new()
                {
                    Id = "seed_oddly_satisfying",
                    Topic = "Cắt xà phòng cát động lực chuyển màu",
                    PositiveExample = "Lưỡi dao lướt qua, từng lớp cát vụn rơi xuống như thác đổ tí hon. Không một tiếng động thừa. Độ chính xác đến từng mili-mét khiến não bạn lập tức được xoa dịu.",
                    NegativeExample = "Chào mừng các bạn đến với video cắt xà phòng hôm nay. Bạn sẽ không tin vào mắt mình khi xem cảnh này đâu. Hãy cùng theo dõi nhé!",
                    Explanation = "Mẫu đúng dùng ngôn từ miêu tả cảm giác xúc giác (haptic sensory), đi thẳng vào hành động và nhịp độ thư giãn, cấm tuyệt đối lời chào hỏi sáo rỗng."
                },
                new()
                {
                    Id = "seed_curiosity_mystery",
                    Topic = "Bí mật bức tranh cổ 500 năm",
                    PositiveExample = "Bức chân dung thời Phục Hưng này nhìn qua tưởng chừng rất đỗi tĩnh lặng. Nhưng hãy để ý góc tối phía tay phải. Suốt năm thế kỷ qua, rất ít ai nhận ra người đàn ông đang bí mật nắm chặt một vật thể kỳ lạ trong tay.",
                    NegativeExample = "Trong video này chúng ta sẽ cùng khám phá bí mật bức tranh cổ 500 năm trước. Một câu chuyện rất ly kỳ và thú vị đang chờ đón bạn.",
                    Explanation = "Mẫu đúng tạo hook thị giác lập tức, chỉ thẳng vào điểm dị thường và dẫn dắt sự tò mò của người xem một cách tự nhiên."
                },
                new()
                {
                    Id = "seed_genz_troll",
                    Topic = "Pha xử lý cồng kềnh trong lớp học",
                    PositiveExample = "Pha chuyền gói bim bim dưới gầm bàn mượt mà thế mà xui gặp ngay ánh mắt thầy giám thị ngoài cửa sổ. Đang hớn hở tính ăn vụng thì cả hai đứa đứng hình mất năm giây, đúng là pha xử lý cồng kềnh khó đỡ!",
                    NegativeExample = "Hai bạn học sinh đang lén ăn quà trong giờ học và bị phát hiện. Khoảnh khắc này thực sự rất hài hước và bất ngờ đối với người xem.",
                    Explanation = "Mẫu đúng dùng giọng văn Gen Z bén ngót, bóc phốt tự nhiên như bạn bè ngồi bên cạnh, không bị lặp lại công thức văn mẫu."
                },
                new()
                {
                    Id = "seed_tech_reaction",
                    Topic = "Màn hình máy tính / Gaming / Troll công nghệ",
                    PositiveExample = "Ông nhõi ngồi xoay ghế múa phím lia lịa nhìn chuyên nghiệp phết, nhưng chưa đầy ba giây màn hình đã nhảy cảnh báo đỏ chót. Tự tin cho lắm vào rồi giờ ngồi đơ người không biết xử lý sao luôn.",
                    NegativeExample = "Trong video này cậu bé đang ngồi chơi trước máy tính. Màn hình hiện lên dòng chữ IP TRACED rất thú vị.",
                    Explanation = "Mẫu đúng quan sát chi tiết thực tế trên màn hình và thao tác tay, nói thẳng nói thật dí dỏm, không dùng câu rập khuôn."
                }
            };
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using VideoNarrator.Models;

namespace VideoNarrator.Services.PersonalityEngine
{
    public interface IPersonaManager
    {
        IReadOnlyList<PersonaProfile> GetAllPersonas();
        PersonaProfile GetActivePersona();
        PersonaProfile? GetPersonaById(string id);
        void SetActivePersona(string id);
        void SetDefaultPersona(string id);
        void CreatePersona(PersonaProfile profile);
        void UpdatePersona(PersonaProfile profile);
        void DeletePersona(string id);
        PersonaProfile DuplicatePersona(string id, string newName);
        void ResetToDefaults();
        void Save();
        void Load();
    }

    public class PersonaManager : IPersonaManager
    {
        private readonly List<PersonaProfile> _personas = new();
        private string _activePersonaId = string.Empty;
        private readonly string _storagePath;
        private readonly object _lock = new();

        public PersonaManager()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "VideoNarrator");
            if (!Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); } catch { }
            }
            _storagePath = Path.Combine(dir, "personas.json");
            Load();
        }

        public IReadOnlyList<PersonaProfile> GetAllPersonas()
        {
            lock (_lock)
            {
                return _personas.ToList();
            }
        }

        public PersonaProfile GetActivePersona()
        {
            lock (_lock)
            {
                var active = _personas.FirstOrDefault(p => p.Id == _activePersonaId);
                if (active != null) return active;

                var defaultP = _personas.FirstOrDefault(p => p.IsDefault);
                if (defaultP != null)
                {
                    _activePersonaId = defaultP.Id;
                    return defaultP;
                }

                if (_personas.Count > 0)
                {
                    _activePersonaId = _personas[0].Id;
                    return _personas[0];
                }

                var madMuse = CreateMadMusePreset();
                _personas.Add(madMuse);
                _activePersonaId = madMuse.Id;
                Save();
                return madMuse;
            }
        }

        public PersonaProfile? GetPersonaById(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return GetActivePersona();

            lock (_lock)
            {
                var direct = _personas.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
                if (direct != null) return direct;

                // Alias mapping for standard presets
                string norm = id.ToLowerInvariant().Replace("_", " ").Replace("-", " ").Trim();
                var alias = _personas.FirstOrDefault(p =>
                    p.Id.ToLowerInvariant().Replace("_", " ").Replace("-", " ").Trim() == norm ||
                    p.Name.ToLowerInvariant().Contains(norm));
                if (alias != null) return alias;

                // Built-in aliases mapping
                if (norm.Contains("curiosity") || norm.Contains("the gioi co gi hay"))
                    return _personas.FirstOrDefault(p => p.Id == "Curiosity" || p.Id == "the_gioi_co_gi_hay");
                if (norm.Contains("oddly satisfying") || norm.Contains("satisfying") || norm.Contains("hypnotic"))
                    return _personas.FirstOrDefault(p => p.Id == "Oddly Satisfying" || p.Id == "hypnotic_zen");
                if (norm.Contains("science") || norm.Contains("khoa hoc"))
                    return _personas.FirstOrDefault(p => p.Id == "Science Explained");
                if (norm.Contains("story") || norm.Contains("mad muse"))
                    return _personas.FirstOrDefault(p => p.Id == "Storytelling" || p.Id == "mad_muse");
                if (norm.Contains("fast") || norm.Contains("hype"))
                    return _personas.FirstOrDefault(p => p.Id == "Fast-Paced" || p.Id == "high_octane_hype");
                if (norm.Contains("cinematic"))
                    return _personas.FirstOrDefault(p => p.Id == "Cinematic");
                if (norm.Contains("humor") || norm.Contains("genz") || norm.Contains("troll"))
                    return _personas.FirstOrDefault(p => p.Id == "Dry Humor" || p.Id == "genz_troll");
                if (norm.Contains("custom"))
                    return _personas.FirstOrDefault(p => p.Id == "Custom Persona");

                return _personas.FirstOrDefault(p => p.IsDefault) ?? _personas.FirstOrDefault();
            }
        }

        public void SetActivePersona(string id)
        {
            lock (_lock)
            {
                var found = _personas.FirstOrDefault(p => p.Id == id);
                if (found != null)
                {
                    _activePersonaId = found.Id;
                }
            }
        }

        public void SetDefaultPersona(string id)
        {
            lock (_lock)
            {
                foreach (var p in _personas)
                {
                    p.IsDefault = (p.Id == id);
                }
                _activePersonaId = id;
                Save();
            }
        }

        public void CreatePersona(PersonaProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            lock (_lock)
            {
                profile.IsBuiltIn = false;
                _personas.Add(profile);
                _activePersonaId = profile.Id;
                Save();
            }
        }

        public void UpdatePersona(PersonaProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            lock (_lock)
            {
                int idx = _personas.FindIndex(p => p.Id == profile.Id);
                if (idx >= 0)
                {
                    _personas[idx] = profile;
                    Save();
                }
            }
        }

        public void DeletePersona(string id)
        {
            lock (_lock)
            {
                var target = _personas.FirstOrDefault(p => p.Id == id);
                if (target == null) return;

                // Built-in personas cannot be permanently deleted, but user custom ones can
                if (target.IsBuiltIn)
                {
                    throw new InvalidOperationException("Không thể xóa Persona mẫu mặc định của hệ thống.");
                }

                _personas.Remove(target);
                if (_activePersonaId == id)
                {
                    _activePersonaId = _personas.FirstOrDefault(p => p.IsDefault)?.Id ?? _personas.FirstOrDefault()?.Id ?? "";
                }
                Save();
            }
        }

        public PersonaProfile DuplicatePersona(string id, string newName)
        {
            lock (_lock)
            {
                var original = _personas.FirstOrDefault(p => p.Id == id) ?? GetActivePersona();
                var cloned = original.Clone(newName);
                _personas.Add(cloned);
                _activePersonaId = cloned.Id;
                Save();
                return cloned;
            }
        }

        public void ResetToDefaults()
        {
            lock (_lock)
            {
                _personas.Clear();
                _personas.AddRange(GetBuiltInPersonas());
                var defaultP = _personas.First(p => p.IsDefault);
                _activePersonaId = defaultP.Id;
                Save();
            }
        }

        public void Save()
        {
            try
            {
                var opts = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_personas, opts);
                File.WriteAllText(_storagePath, json, Encoding.UTF8);
            }
            catch { }
        }

        public void Load()
        {
            lock (_lock)
            {
                _personas.Clear();
                if (File.Exists(_storagePath))
                {
                    try
                    {
                        string json = File.ReadAllText(_storagePath, Encoding.UTF8);
                        var loaded = JsonSerializer.Deserialize<List<PersonaProfile>>(json);
                        if (loaded != null && loaded.Count > 0)
                        {
                            _personas.AddRange(loaded);
                        }
                    }
                    catch { }
                }

                // If empty or missing built-ins, load presets
                if (_personas.Count == 0)
                {
                    _personas.AddRange(GetBuiltInPersonas());
                    Save();
                }
                else
                {
                    // Đồng bộ và nâng cấp các Persona tích hợp sẵn với 4 trụ cột mới nhất
                    var builtIns = GetBuiltInPersonas();
                    foreach (var bi in builtIns)
                    {
                        var existing = _personas.FirstOrDefault(p => p.Id == bi.Id && p.IsBuiltIn);
                        if (existing != null)
                        {
                            existing.Name = bi.Name;
                            existing.CustomRules = bi.CustomRules;
                            existing.Description = bi.Description;
                            existing.VoiceTone = bi.VoiceTone;
                            existing.StyleConfig = bi.StyleConfig;
                            existing.IsDefault = bi.IsDefault;
                        }
                        else
                        {
                            _personas.Add(bi);
                        }
                    }
                    Save();
                }

                var def = _personas.FirstOrDefault(p => p.Id == "the_gioi_co_gi_hay") 
                       ?? _personas.FirstOrDefault(p => p.IsDefault) 
                       ?? _personas.First();
                _activePersonaId = def.Id;
            }
        }

        private static List<PersonaProfile> GetBuiltInPersonas()
        {
            return new List<PersonaProfile>
            {
                CreateCuriosityPreset(),
                CreateOddlySatisfyingPreset(),
                CreateScienceExplainedPreset(),
                CreateStorytellingPreset(),
                CreateFastPacedPreset(),
                CreateCinematicPreset(),
                CreateDryHumorPreset(),
                CreateCustomPersonaPreset(),
                CreateTheGioiCoGiHayPreset(),
                CreateMadMusePreset(),
                CreateDoiCoGiHayPreset(),
                CreateGenZTrollPreset(),
                CreateHypnoticZenPreset(),
                CreateDarkDocumentarianPreset(),
                CreateHighOctaneHypePreset()
            };
        }

        private static PersonaProfile CreateCuriosityPreset()
        {
            var p = CreateTheGioiCoGiHayPreset();
            p.Id = "Curiosity";
            p.Name = "🌍 Curiosity (Khám Phá & Sự Thật Độc Lạ)";
            p.IsDefault = true;
            return p;
        }

        private static PersonaProfile CreateOddlySatisfyingPreset()
        {
            return new PersonaProfile
            {
                Id = "Oddly Satisfying",
                Name = "🌀 Oddly Satisfying (Thỏa Mãn Thị Giác)",
                Description = "Tập trung vào quy trình mượt mà, âm thanh ma sát/chuyển động và cảm giác thỏa mãn thị giác tuyệt đối, tiết chế lời thừa.",
                VoiceTone = "Thư thái, êm dịu, nhả chữ chậm rãi, tinh tế",
                IsDefault = false,
                IsBuiltIn = true,
                StyleConfig = new StyleEngineConfig
                {
                    CuriosityLevel = 60,
                    DramaticLevel = 30,
                    WeirdnessLevel = 30,
                    SarcasmLevel = 10,
                    VisualLanguageLevel = 100,
                    HookIntensity = 80,
                    SuspenseLevel = 40,
                    SatisfactionLevel = 100,
                    SentenceRhythm = "slow_burn",
                    NarrationSpeed = "normal_1.0x",
                    EndingStyle = "loop_seamless",
                    Verbosity = "concise"
                },
                CustomRules = new List<string>
                {
                    "Tập trung miêu tả độ mịn, đường cắt, âm thanh ma sát và cảm giác thỏa mãn thị giác.",
                    "Tiết chế lời thừa, dùng câu ngắn cô đọng để hình ảnh tự lên tiếng.",
                    "Tạo cảm giác giải tỏa căng thẳng và thư giãn dopamine."
                }
            };
        }

        private static PersonaProfile CreateScienceExplainedPreset()
        {
            return new PersonaProfile
            {
                Id = "Science Explained",
                Name = "🔬 Science Explained (Giải Mã Khoa Học & Cơ Chế)",
                Description = "Giải thích nguyên lý vật lý, cơ học hoặc hiện tượng khoa học bằng ngôn ngữ đời thường dễ hình dung, bóc tách mối quan hệ nhân quả rõ ràng.",
                VoiceTone = "Sắc sảo, logic, hào hứng, tự nhiên, giải thích sáng rõ",
                IsDefault = false,
                IsBuiltIn = true,
                StyleConfig = new StyleEngineConfig
                {
                    CuriosityLevel = 95,
                    DramaticLevel = 60,
                    WeirdnessLevel = 40,
                    SarcasmLevel = 15,
                    VisualLanguageLevel = 90,
                    HookIntensity = 90,
                    SuspenseLevel = 75,
                    SatisfactionLevel = 95,
                    SentenceRhythm = "alternating",
                    NarrationSpeed = "normal_1.0x",
                    EndingStyle = "mind_bending",
                    Verbosity = "balanced"
                },
                CustomRules = new List<string>
                {
                    "Hook ngay bằng hiện tượng trái trực giác hoặc câu hỏi cơ chế cụ thể.",
                    "Giải thích logic nhân - quả từng bước bằng các động từ giàu tính cơ học thực tế.",
                    "Không dùng thuật ngữ hàn lâm khô cứng, dùng ví von trực quan dễ hiểu."
                }
            };
        }

        private static PersonaProfile CreateStorytellingPreset()
        {
            return new PersonaProfile
            {
                Id = "Storytelling",
                Name = "📖 Storytelling (Kể Chuyện Kịch Tính & Nhân Vật)",
                Description = "Kể chuyện có nhân vật/chủ thể, rào cản thử thách, hành động giải quyết và kết quả thuyết phục.",
                VoiceTone = "Truyền cảm, có chiều sâu, nhập vai cuốn hút",
                IsDefault = false,
                IsBuiltIn = true,
                StyleConfig = new StyleEngineConfig
                {
                    CuriosityLevel = 90,
                    DramaticLevel = 80,
                    WeirdnessLevel = 50,
                    SarcasmLevel = 25,
                    VisualLanguageLevel = 85,
                    HookIntensity = 90,
                    SuspenseLevel = 85,
                    SatisfactionLevel = 90,
                    SentenceRhythm = "alternating",
                    NarrationSpeed = "rapid_1.2x",
                    EndingStyle = "payoff_climax",
                    Verbosity = "balanced"
                },
                CustomRules = new List<string>
                {
                    "Nhập vai người kể chuyện chứng kiến tình huống tận mắt.",
                    "Xây dựng nút thắt - trở ngại - hành động và cú chốt hạ thỏa mãn.",
                    "Tương phản nhịp điệu giữa lúc rào cản xuất hiện và khi vấn đề được giải quyết."
                }
            };
        }

        private static PersonaProfile CreateFastPacedPreset()
        {
            return new PersonaProfile
            {
                Id = "Fast-Paced",
                Name = "⚡ Fast-Paced (Nhịp Nhanh Dồn Dập)",
                Description = "Nhịp dồn dập, câu ngắn gọn 3-6 từ, giàu động từ mạnh, loại bỏ hoàn toàn từ đệm và triết lý.",
                VoiceTone = "Nhanh, dứt khoát, dồn dập, giàu năng lượng",
                IsDefault = false,
                IsBuiltIn = true,
                StyleConfig = new StyleEngineConfig
                {
                    CuriosityLevel = 85,
                    DramaticLevel = 90,
                    WeirdnessLevel = 60,
                    SarcasmLevel = 30,
                    VisualLanguageLevel = 80,
                    HookIntensity = 95,
                    SuspenseLevel = 80,
                    SatisfactionLevel = 85,
                    SentenceRhythm = "rapid_fire",
                    NarrationSpeed = "rapid_1.2x",
                    EndingStyle = "punchline",
                    Verbosity = "concise"
                },
                CustomRules = new List<string>
                {
                    "Câu cực ngắn, dứt khoát, cắt bỏ toàn bộ từ nối và liên từ rườm rà.",
                    "Động từ hành động chiếm ưu thế, đẩy nhịp kể lên tốc độ cao.",
                    "Ngắt nhịp câu chuẩn xác bằng dấu chấm để đọc không bị vấp."
                }
            };
        }

        private static PersonaProfile CreateCinematicPreset()
        {
            return new PersonaProfile
            {
                Id = "Cinematic",
                Name = "🎬 Cinematic (Điện Ảnh Sâu Lắng & Khoảng Lặng)",
                Description = "Giàu hình ảnh thị giác, câu từ trau chuốt, có khoảng lặng pause hợp lý, tạo chiều sâu không gian.",
                VoiceTone = "Trầm ấm, sâu lắng, đậm chất điện ảnh",
                IsDefault = false,
                IsBuiltIn = true,
                StyleConfig = new StyleEngineConfig
                {
                    CuriosityLevel = 80,
                    DramaticLevel = 75,
                    WeirdnessLevel = 50,
                    SarcasmLevel = 10,
                    VisualLanguageLevel = 95,
                    HookIntensity = 85,
                    SuspenseLevel = 85,
                    SatisfactionLevel = 85,
                    SentenceRhythm = "slow_burn",
                    NarrationSpeed = "normal_1.0x",
                    EndingStyle = "mind_bending",
                    Verbosity = "balanced"
                },
                CustomRules = new List<string>
                {
                    "Ngôn từ gợi cảm xúc và hình ảnh điện ảnh sắc nét.",
                    "Chèn khoảng lặng nhịp thở để hình ảnh tỏa sáng.",
                    "Kết thúc có dư âm gợi mở."
                }
            };
        }

        private static PersonaProfile CreateDryHumorPreset()
        {
            return new PersonaProfile
            {
                Id = "Dry Humor",
                Name = "😏 Dry Humor (Hài Hước Khô & Châm Biếm Nhẹ)",
                Description = "Hài hước khô, tỉnh rụi, nhận xét sắc sảo châm biếm nhẹ nhàng về sự vô lý của tình huống, không pha trò lố.",
                VoiceTone = "Hài hước, tỉnh rụi, duyên dáng, châm biếm thông minh",
                IsDefault = false,
                IsBuiltIn = true,
                StyleConfig = new StyleEngineConfig
                {
                    CuriosityLevel = 80,
                    DramaticLevel = 50,
                    WeirdnessLevel = 70,
                    SarcasmLevel = 80,
                    VisualLanguageLevel = 80,
                    HookIntensity = 85,
                    SuspenseLevel = 60,
                    SatisfactionLevel = 80,
                    SentenceRhythm = "alternating",
                    NarrationSpeed = "normal_1.0x",
                    EndingStyle = "punchline",
                    Verbosity = "balanced"
                },
                CustomRules = new List<string>
                {
                    "Giọng điệu tỉnh rụi như không có gì xảy ra trước những pha xử lý cồng kềnh.",
                    "Nhận xét hóm hỉnh tinh tế, tuyệt đối không dùng tiếng cười giả tạo.",
                    "Chốt hạ bất ngờ bằng cú twist châm biếm nhẹ."
                }
            };
        }

        private static PersonaProfile CreateCustomPersonaPreset()
        {
            return new PersonaProfile
            {
                Id = "Custom Persona",
                Name = "⚙️ Custom Persona (Tùy Chỉnh Cá Nhân Hóa)",
                Description = "Nhận toàn bộ giá trị tùy chỉnh từ người dùng trên giao diện để tạo phong cách độc nhất.",
                VoiceTone = "Linh hoạt theo cấu hình tùy chọn của người dùng",
                IsDefault = false,
                IsBuiltIn = true,
                StyleConfig = new StyleEngineConfig
                {
                    CuriosityLevel = 85,
                    DramaticLevel = 70,
                    WeirdnessLevel = 50,
                    SarcasmLevel = 30,
                    VisualLanguageLevel = 85,
                    HookIntensity = 90,
                    SuspenseLevel = 75,
                    SatisfactionLevel = 85,
                    SentenceRhythm = "alternating",
                    NarrationSpeed = "normal_1.0x",
                    EndingStyle = "mind_bending",
                    Verbosity = "balanced"
                },
                CustomRules = new List<string>
                {
                    "Áp dụng chính xác chỉ đạo riêng từ người dùng.",
                    "Tuân thủ các nguyên tắc chống sáo rỗng và bám sát thực tế video."
                }
            };
        }

        private static PersonaProfile CreateTheGioiCoGiHayPreset()
        {
            return new PersonaProfile
            {
                Id = "the_gioi_co_gi_hay",
                Name = "🌍 Thế Giới Có Gì Hay? (Sự Thật & Mẹo Độc Lạ)",
                Description = "Chuẩn phong cách triệu view kênh 'Thế Giới Có Gì Hay?' và Zack D. Films. Hook trực diện mở màn cực gắt ('Đây là cách...', 'Tại sao...'), bóc tách quy luật và cơ chế hoạt động vi mô, giải mã sự thật độc lạ bám sát màn hình, kết thúc bằng CTA biến nút thích sáng tạo.",
                VoiceTone = "Đĩnh đạc, cuốn hút, kích thích tò mò cao độ, phân tích cơ chế rõ ràng, câu từ sắc sảo và gãy gọn",
                IsDefault = true,
                IsBuiltIn = true,
                StyleConfig = new StyleEngineConfig
                {
                    CuriosityLevel = 100,
                    DramaticLevel = 75,
                    WeirdnessLevel = 85,
                    SarcasmLevel = 25,
                    VisualLanguageLevel = 95,
                    HookIntensity = 100,
                    SuspenseLevel = 85,
                    SatisfactionLevel = 95,
                    SentenceRhythm = "alternating",
                    NarrationSpeed = "normal_1.0x",
                    EndingStyle = "mind_bending",
                    Verbosity = "balanced"
                },
                CustomRules = new List<string>
                {
                    "CẤU TRÚC 4 BƯỚC VÀNG CHUẨN KÊNH 'THẾ GIỚI CÓ GÌ HAY?' (TRIỆU VIEW):",
                    "- BƯỚC 1 - HOOK TRỰC DIỆN (0-2s): Mở đầu đập thẳng vào vấn đề hoặc hành vi dị thường, tạo sự tò mò tột độ. Dùng các mẫu câu mạnh mẽ: 'Đây là cách một số người gian lận trên...', 'Tại sao người ta lại đổ hàng triệu...', 'Điều gì sẽ xảy ra nếu...'. TUYỆT ĐỐI CẤM mở đầu vòng vo, cấm chào hỏi ('Chào mừng các bạn...'), cấm triết lý sáo rỗng ('Cuộc sống xung quanh ta...').",
                    "- BƯỚC 2 - LUẬT LỆ & ÁP LỰC THỰC TẾ (2-5s): Đưa ra quy tắc, luật lệ, rào cản thời gian hoặc bối cảnh dẫn đến hành động (Ví dụ: 'Sau khi đánh bóng thì bạn chỉ có khoảng 3 phút để tìm bóng. Nếu không tìm thấy, bóng sẽ chính thức được tính là bị mất'). Câu từ ngắn gọn, logic, chính xác.",
                    "- BƯỚC 3 - BÓC TÁCH CƠ CHẾ & HÀNH ĐỘNG VI MÔ (5-12s): Miêu tả chuẩn xác công cụ, mánh khóe hoặc từng cử chỉ mà người xem đang thấy trên màn hình (Ví dụ: 'Vì vậy, một số người chơi sử dụng một thiết bị nhỏ được giấu ở chân để chứa thêm một quả bóng. Chỉ với một động tác đơn giản, họ bí mật thả quả bóng ra khiến mọi người tưởng rằng họ vừa tìm thấy quả bóng ban đầu'). Từ ngữ giàu tính thị giác và cơ chế thực tế.",
                    "- BƯỚC 4 - CÚ CHỐT HẠ & CTA TƯƠNG TÁC THÔNG MINH (12-15s): Kết thúc bằng câu kêu gọi hành động sáng tạo gắn liền với đồ vật trong video ('Biến nút thích của tôi thành bóng golf', 'Biến nút thích thành thanh socola') hoặc câu hỏi thăm dò quan điểm ('Nếu là bạn, bạn có phát hiện ra trò gian lận này không?'). TUYỆT ĐỐI CẤM câu xin like ăn xin nhàm chán ('nhớ like và subscribe cho mình nhé').",
                    "MẬT ĐỘ THÔNG TIN CAO (HIGH INFORMATION DENSITY): Mỗi câu đều mang thông tin đắt giá, loại bỏ 100% từ thừa, từ phóng đại vô nghĩa ('vô cùng tuyệt vời', 'đẳng cấp chuyên nghiệp tính toán tỉ mỉ', 'pha xử lý đi vào lòng đất').",
                    "PHÁT ÂM AI CHUẨN XÁC: Viết câu hoàn chỉnh có dấu chấm phẩy rõ ràng để AI TTS ngắt nhịp thở tự nhiên, giọng đọc có ngữ điệu đĩnh đạc và cuốn hút."
                }
            };
        }

        private static PersonaProfile CreateMadMusePreset()
        {
            return new PersonaProfile
            {
                Id = "mad_muse",
                Name = "✨ Diễn Xuất Lôi Cuốn (Dynamic Cinematic Storytelling)",
                Description = "Tông giọng nhập vai giàu cảm xúc, có tính diễn truyền cảm, nhịp kể chuyện lôi cuốn cuốn hút từng giây. Hook mạnh 3s đầu, buildup kịch tính, payoff thỏa mãn và ending hoàn hảo.",
                VoiceTone = "Truyền cảm, sắc bén, nhịp kể chuyện lôi cuốn, có tính diễn nhập vai xuất thần",
                IsDefault = false,
                IsBuiltIn = true,
                StyleConfig = new StyleEngineConfig
                {
                    CuriosityLevel = 95,
                    DramaticLevel = 85,
                    WeirdnessLevel = 80,
                    SarcasmLevel = 45,
                    VisualLanguageLevel = 95,
                    HookIntensity = 95,
                    SuspenseLevel = 85,
                    SatisfactionLevel = 85,
                    SentenceRhythm = "alternating",
                    NarrationSpeed = "rapid_1.2x",
                    EndingStyle = "mind_bending",
                    Verbosity = "balanced"
                },
                CustomRules = new List<string>
                {
                    "TÍNH DIỄN VÀ NHẬP VAI TRUYỀN CẢM: Lời văn như một diễn viên kịch bản đang diễn xuất trực tiếp trước màn hình, truyền tải trọn vẹn cảm xúc và nhịp đập của video.",
                    "BỎ HOÀN TOÀN GIỌNG MARKETING: Tuyệt đối cấm các câu triết lý mở đầu sáo rỗng hoặc tính từ quảng cáo trừu tượng.",
                    "GÓC NHÌN CÁ NHÂN SẮC LẸM: Bình luận trực diện, tự nhiên như người thật đang chứng kiến tận mắt. Đa dạng hóa cách mở đầu, tuyệt đối cấm rập khuôn văn mẫu.",
                    "ĐIỀU CHỈNH NHỊP CÂU DYNAMIC: Câu ngắn mở màn giật mình xen kẽ câu dài miêu tả hành vi vi mô và cú chốt hạ khó đoán.",
                    "VIẾT CỤ THỂ 100%: Mọi liên tưởng đều phải neo vào vật thể thật, màu sắc, vị trí và cử chỉ thực tế trong khung hình.",
                    "TUYỆT ĐỐI CẤM RẬP KHUÔN: Cấm mở đầu cố định bằng 'Nhìn quả... kìa', cấm lạm dụng 'Cứ ngỡ... ai dè', cấm kết bài bằng 'cười không ngậm được mồm' hoặc 'bất lực giùm'."
                }
            };
        }

        private static PersonaProfile CreateGenZTrollPreset()
        {
            return new PersonaProfile
            {
                Id = "genz_troll",
                Name = "🔥 Review Sắc Báo & Gen Z Troll",
                Description = "Táo bạo, bóc phốt hài hước từ góc nhìn cá nhân người thật, cấm giọng văn PR công nghiệp, chêm từ Gen Z cực bén, gọi tên đồ vật và cử chỉ cụ thể.",
                VoiceTone = "Hài hước, duyên dáng, cà khịa bén ngót, bắt trend TikTok",
                IsDefault = false,
                IsBuiltIn = true,
                StyleConfig = new StyleEngineConfig
                {
                    CuriosityLevel = 80,
                    DramaticLevel = 70,
                    WeirdnessLevel = 50,
                    SarcasmLevel = 85,
                    VisualLanguageLevel = 75,
                    HookIntensity = 90,
                    SuspenseLevel = 60,
                    SatisfactionLevel = 70,
                    SentenceRhythm = "rapid_fire",
                    NarrationSpeed = "rapid_1.2x",
                    EndingStyle = "punchline",
                    Verbosity = "balanced"
                },
                CustomRules = new List<string>
                {
                    "NÓI THẲNG NÓI THẬT 100%: Trong video có chi tiết gì bóc phốt thẳng cái đó, cấm tiệt giọng văn PR giới thiệu hay triết lý sáo rỗng.",
                    "GÓC NHÌN NGƯỜI THẬT TỰ NHIÊN: Lối nói chuyện đời thường, linh hoạt, đa dạng cách mở đầu theo tình huống video. CẤM rập khuôn mọi video bằng 'Nhìn quả... kìa', CẤM lạm dụng 'Cứ ngỡ... ai dè', CẤM câu kết 'cười không ngậm được mồm'.",
                    "CHỈNH NHỊP CÂU DYNAMIC: Câu ngắn dài đan xen nhịp nhàng, có dấu chấm câu rõ ràng để ngắt nhịp thở chuẩn xác khi đọc TTS.",
                    "VIẾT CỤ THỂ 100% THEO VIDEO: Gọi tên đúng đồ vật, màu sắc, động tác và biểu cảm thực tế xuất hiện trên màn hình, không bịa đặt chi tiết từ video khác.",
                    "Chêm từ tiếng Anh Gen Z tự nhiên phù hợp ngữ cảnh: flex, vibe, chill, chemistry, slay, visual."
                }
            };
        }

        private static PersonaProfile CreateHypnoticZenPreset()
        {
            return new PersonaProfile
            {
                Id = "hypnotic_zen",
                Name = "🌀 Thôi Miên & Thỏa Mãn Thị Giác (Hypnotic Visuals / Oddly Satisfying)",
                Description = "Thôi miên thị giác, miêu tả cảm giác ASMR cực đã mắt, độ thỏa mãn thị giác cao nhất, nhịp điệu nhả chữ êm tai và chuẩn xác.",
                VoiceTone = "Thư thái, thôi miên, tĩnh tại, miêu tả xúc giác haptic đỉnh cao",
                IsDefault = false,
                IsBuiltIn = true,
                StyleConfig = new StyleEngineConfig
                {
                    CuriosityLevel = 75,
                    DramaticLevel = 40,
                    WeirdnessLevel = 30,
                    SarcasmLevel = 10,
                    VisualLanguageLevel = 100,
                    HookIntensity = 85,
                    SuspenseLevel = 55,
                    SatisfactionLevel = 100,
                    SentenceRhythm = "slow_burn",
                    NarrationSpeed = "normal_1.0x",
                    EndingStyle = "loop_seamless",
                    Verbosity = "concise"
                },
                CustomRules = new List<string>
                {
                    "THÔI MIÊN THỊ GIÁC: Tập trung miêu tả độ mịn, đường cắt, âm thanh ma sát và cảm giác thỏa mãn thị giác tuyệt đối.",
                    "Tạo cảm giác giải tỏa căng thẳng (stress-relief) và làm dịu dopamine trong não bộ.",
                    "Tạo điểm nối vòng lặp hoàn hảo (Perfect Loop) giữa câu kết và câu đầu."
                }
            };
        }

        private static PersonaProfile CreateDarkDocumentarianPreset()
        {
            return new PersonaProfile
            {
                Id = "dark_documentarian",
                Name = "Dark Documentarian (Điều Tra Bí Ẩn)",
                Description = "Không khí bí ẩn, điều tra, phân tích tâm lý sâu cay, tông giọng sắc lạnh, dồn ép sự căng thẳng, kết thúc bất ngờ khó đoán.",
                VoiceTone = "Trầm ổn, sắc lạnh, kịch tính, lôi cuốn tâm lý",
                IsDefault = false,
                IsBuiltIn = true,
                StyleConfig = new StyleEngineConfig
                {
                    CuriosityLevel = 95,
                    DramaticLevel = 90,
                    WeirdnessLevel = 60,
                    SarcasmLevel = 30,
                    VisualLanguageLevel = 85,
                    HookIntensity = 95,
                    SuspenseLevel = 95,
                    SatisfactionLevel = 75,
                    SentenceRhythm = "syncopated",
                    NarrationSpeed = "fast_1.1x",
                    EndingStyle = "cliffhanger",
                    Verbosity = "balanced"
                },
                CustomRules = new List<string>
                {
                    "Tạo bầu không khí hồi hộp, vén màn từng manh mối như một vụ án bí ẩn.",
                    "Không vội kết luận, để người xem tự xâu chuỗi dữ kiện trước khi tung ra cú twist."
                }
            };
        }

        private static PersonaProfile CreateHighOctaneHypePreset()
        {
            return new PersonaProfile
            {
                Id = "high_octane_hype",
                Name = "High-Octane Hype (Năng Lượng Bùng Nổ)",
                Description = "Năng lượng bùng nổ, tốc độ cao, gay cấn nghẹt thở, động từ mạnh, kích thích endorphin tối đa, không để một giây nào lắng xuống.",
                VoiceTone = "Bùng nổ, dồn dập, nhiệt huyết, thót tim",
                IsDefault = false,
                IsBuiltIn = true,
                StyleConfig = new StyleEngineConfig
                {
                    CuriosityLevel = 85,
                    DramaticLevel = 95,
                    WeirdnessLevel = 50,
                    SarcasmLevel = 25,
                    VisualLanguageLevel = 85,
                    HookIntensity = 100,
                    SuspenseLevel = 80,
                    SatisfactionLevel = 90,
                    SentenceRhythm = "rapid_fire",
                    NarrationSpeed = "rapid_1.2x",
                    EndingStyle = "call_to_action",
                    Verbosity = "balanced"
                },
                CustomRules = new List<string>
                {
                    "Dùng động từ hành động mạnh, tiết tấu dồn dập kích hoạt adrenaline.",
                    "Mỗi giây đều phải có biến cố hoặc sự thay đổi cục diện đáng kinh ngạc."
                }
            };
        }
        private static PersonaProfile CreateDoiCoGiHayPreset()
        {
            return new PersonaProfile
            {
                Id = "doi_co_gi_hay",
                Name = "🛠️ Khám Phá & Đời Sống Độc Lạ (Đời Có Gì Hay)",
                Description = "Thuyết minh khám phá công cụ, mẹo vặt, cơ khí đời sống và oddly satisfying kiểu kênh 'Đời Có Gì Hay'. Bám sát từng giây hành động thực tế, hook câu hỏi tự nhiên ('Tìm ra một cách... nhưng ngày nay thì sao?'), phân tầng tăng tiến từ thủ công đến máy móc công nghệ cao, kết thúc bằng pha hành động mãn nhãn.",
                VoiceTone = "Điềm tĩnh, trầm ấm, rõ ràng, đĩnh đạc, khách quan bám sát hình ảnh thực tế",
                IsDefault = false,
                IsBuiltIn = true,
                StyleConfig = new StyleEngineConfig
                {
                    CuriosityLevel = 90,
                    DramaticLevel = 65,
                    WeirdnessLevel = 60,
                    SarcasmLevel = 10,
                    VisualLanguageLevel = 95,
                    HookIntensity = 90,
                    SuspenseLevel = 70,
                    SatisfactionLevel = 95,
                    SentenceRhythm = "alternating",
                    NarrationSpeed = "normal_1.0x",
                    EndingStyle = "mind_bending",
                    Verbosity = "balanced"
                },
                CustomRules = new List<string>
                {
                    "CẤU TRÚC KỊCH BẢN CHUẨN ĐỜI CÓ GÌ HAY (4 BƯỚC HOÀN CHỈNH):",
                    "- Bước 1 (Hook 0-3s): Đặt vấn đề quen thuộc bằng câu hỏi hoặc trần thuật ('Tìm ra một cách đơn giản để [hành động] từ lâu đã là điều được nhiều người quan tâm. Nhưng ngày nay thì sao?').",
                    "- Bước 2 (Giải pháp cấp độ 1 - Dụng cụ sáng tạo): 'Đây là một... được thiết kế để ngay cả người mới cũng có thể dễ dàng... Bạn chỉ cần đặt... rồi dùng... đập xuống.'",
                    "- Bước 3 (Giải pháp cấp độ 2 - Tăng tiến máy móc cơ khí): 'Ngoài ra còn có những phương pháp sử dụng máy móc. Đầu tiên là máy... Như bạn đang thấy, nó dùng lực chậm nhưng cực kỳ mạnh để... Thứ hai là loại máy... với tốc độ rất cao nhưng khá nguy hiểm, đòi hỏi người dùng phải thành thạo.'",
                    "- Bước 4 (Payoff cao trào mãn nhãn): 'Cuối cùng là một cách khá đặc biệt...' kết hợp với pha hành động bất ngờ nhất (chém kiếm chẻ khúc gỗ, máy cắt siêu nhanh...).",
                    "BỎ HOÀN TOÀN GIỌNG MARKETING: Cấm tiệt mọi lời chào hỏi sáo rỗng ('Chào mừng các bạn', 'Hãy like và đăng ký', 'Sản phẩm tuyệt vời').",
                    "BÁM SÁT 100% CHI TIẾT TRÊN KHUNG HÌNH: Dùng các từ nối neo thị giác trực tiếp ('Như bạn đang thấy...', 'Bạn chỉ cần...', 'Chỉ với một cú chém...').",
                    "NHỊP ĐIỆU CÂU TỰ NHIÊN, DỄ ĐỌC TTS: Câu văn rõ ràng, có dấu ngắt nghỉ mạch lạc, từ ngữ thuần Việt mộc mạc và chuẩn xác."
                }
            };
        }
    }
}

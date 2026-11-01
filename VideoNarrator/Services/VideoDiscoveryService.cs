using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VideoNarrator.Helpers;
using VideoNarrator.Models;

namespace VideoNarrator.Services
{
    public interface IVideoDiscoveryService
    {
        IReadOnlyList<string> TrendingTopics { get; }

        Task<List<DiscoveredVideoItem>> SearchVideosAsync(
            string query,
            int maxResults = 8,
            CancellationToken cancellationToken = default);

        Task<DiscoveredVideoItem?> FindBestVideoForTopicAsync(
            string? topic = null,
            CancellationToken cancellationToken = default);
    }

    public class VideoDiscoveryService : IVideoDiscoveryService
    {
        private readonly IFileService _fileService;
        private static readonly Random _random = new();

        public IReadOnlyList<string> TrendingTopics { get; } = new List<string>
        {
            "Sự thật thú vị về thế giới",
            "Khoa học kỳ thú và thí nghiệm độc lạ",
            "Oddly satisfying soap cutting",
            "Mẹo vặt cuộc sống thông minh",
            "Động vật hoang dã tự nhiên kỳ thú",
            "Chế tác cơ khí chính xác đỉnh cao",
            "Món ăn đường phố độc lạ thế giới",
            "Hiện tượng thiên nhiên kỳ thú",
            "Kỹ năng thủ công mỹ nghệ đỉnh chóp",
            "Ảo giác thị giác 3D độc lạ"
        };

        public VideoDiscoveryService(IFileService fileService)
        {
            _fileService = fileService;
        }

        public async Task<List<DiscoveredVideoItem>> SearchVideosAsync(
            string query,
            int maxResults = 8,
            CancellationToken cancellationToken = default)
        {
            string cleanQuery = string.IsNullOrWhiteSpace(query)
                ? TrendingTopics[_random.Next(TrendingTopics.Count)]
                : query.Trim();

            _fileService.Log($"[Video Discovery] Tìm kiếm video trực tuyến với từ khóa: '{cleanQuery}' (Tối đa: {maxResults})");

            string scriptPath = ResolveScriptPath();
            string pythonPath = ResolvePythonPath();

            if (!File.Exists(scriptPath))
            {
                _fileService.Log($"[Video Discovery] Không tìm thấy worker script tại: {scriptPath}", "ERROR");
                return new List<DiscoveredVideoItem>();
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = pythonPath,
                    Arguments = $"\"{scriptPath}\" search \"{cleanQuery}\" {maxResults}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using var process = new Process { StartInfo = startInfo };
                var outputBuilder = new StringBuilder();

                process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        outputBuilder.AppendLine(e.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(30));

                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);

                string outputJson = outputBuilder.ToString().Trim();
                if (string.IsNullOrWhiteSpace(outputJson))
                {
                    _fileService.Log("[Video Discovery] Worker không trả về dữ liệu.", "WARN");
                    return new List<DiscoveredVideoItem>();
                }

                // Locate JSON in output
                int jsonStart = outputJson.IndexOf('{');
                int jsonEnd = outputJson.LastIndexOf('}');
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    outputJson = outputJson.Substring(jsonStart, jsonEnd - jsonStart + 1);
                }

                using var doc = JsonDocument.Parse(outputJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("videos", out var videosProp) && videosProp.ValueKind == JsonValueKind.Array)
                {
                    var list = new List<DiscoveredVideoItem>();
                    foreach (var elem in videosProp.EnumerateArray())
                    {
                        var item = JsonSerializer.Deserialize<DiscoveredVideoItem>(elem.GetRawText());
                        if (item != null && !string.IsNullOrWhiteSpace(item.Url))
                        {
                            list.Add(item);
                        }
                    }
                    if (list.Count > 0)
                    {
                        // Lọc kép: Chỉ chấp nhận video ngắn chuẩn Shorts (12s - 65s) và video gốc hấp dẫn
                        var filtered = list
                            .Where(v => v.Duration >= 12 && v.Duration <= 65 && !IsBlacklistedTitle(v.Title))
                            .ToList();

                        // Nếu tìm kiếm trực tuyến còn thiếu so với số lượng yêu cầu (maxResults), tự động bù đắp từ kho video triệu view chọn lọc
                        if (filtered.Count < maxResults)
                        {
                            var fallbacks = GetCuratedFallbackVideos(cleanQuery, maxResults);
                            foreach (var fb in fallbacks)
                            {
                                if (filtered.All(v => v.Id != fb.Id) && filtered.Count < maxResults)
                                {
                                    filtered.Add(fb);
                                }
                            }
                        }

                        if (filtered.Count > 0)
                        {
                            _fileService.Log($"[Video Discovery] Tìm thấy {filtered.Count} video ngắn gốc phù hợp ({filtered.Min(v => v.Duration):F0}s - {filtered.Max(v => v.Duration):F0}s).");
                            return filtered;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _fileService.Log($"[Video Discovery] Ngoại lệ khi tìm video: {ex.Message}", "WARN");
            }

            _fileService.Log($"[Video Discovery] Sử dụng video thịnh hành dự phòng cho chủ đề '{cleanQuery}'.");
            return GetCuratedFallbackVideos(cleanQuery, maxResults);
        }

        public static bool IsBlacklistedTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title)) return false;
            string lower = title.ToLowerInvariant();
            string[] bad = {
                "top 10", "top 5", "top 3", "top ", "tổng hợp", "những pha", "những khoảnh khắc",
                "những loài", "bxh", "ranking", "compilation", "best of", "reaction", "review",
                "thuyết minh", "lồng tiếng", "tóm tắt", "giải thích", "kể chuyện", "podcast",
                "audiobook", "sách nói", "part ", "tập ", "phần ", "cho bé", "dạy bé",
                "security cam", "cctv", "dashcam", "surveillance", "alien", "ufo", "ghost",
                "paranormal", "unexplained", "caught on tape", "camera footage"
            };
            return bad.Any(b => lower.Contains(b));
        }

        private static List<DiscoveredVideoItem> GetCuratedFallbackVideos(string query, int count)
        {
            var allCurated = new List<DiscoveredVideoItem>
            {
                new()
                {
                    Id = "pm2NhM30TtA",
                    Title = "Incredible Moments Caught on Camera",
                    Url = "https://www.youtube.com/shorts/pm2NhM30TtA",
                    Duration = 25,
                    Thumbnail = "https://i.ytimg.com/vi/pm2NhM30TtA/hqdefault.jpg",
                    Uploader = "Break Time",
                    ViewCount = 5400000,
                    Platform = "YouTube"
                },
                new()
                {
                    Id = "RUmWVSGkDe0",
                    Title = "Nghệ thuật cắt xà phòng Soap Cut ASMR cực kỳ thỏa mãn",
                    Url = "https://www.youtube.com/shorts/RUmWVSGkDe0",
                    Duration = 21,
                    Thumbnail = "https://i.ytimg.com/vi/RUmWVSGkDe0/hqdefault.jpg",
                    Uploader = "Satisfying ASMR",
                    ViewCount = 9200000,
                    Platform = "YouTube"
                },
                new()
                {
                    Id = "e4AZWrpJ4ww",
                    Title = "Kỹ thuật tiện cơ khí chính xác lathe machining đỉnh cao",
                    Url = "https://www.youtube.com/shorts/e4AZWrpJ4ww",
                    Duration = 30,
                    Thumbnail = "https://i.ytimg.com/vi/e4AZWrpJ4ww/hqdefault.jpg",
                    Uploader = "Machining Tech",
                    ViewCount = 6800000,
                    Platform = "YouTube"
                },
                new()
                {
                    Id = "Oqm7YrV9Qcc",
                    Title = "Fun With Physics - Khoa học kỳ thú chuyển động",
                    Url = "https://www.youtube.com/shorts/Oqm7YrV9Qcc",
                    Duration = 39,
                    Thumbnail = "https://i.ytimg.com/vi/Oqm7YrV9Qcc/hqdefault.jpg",
                    Uploader = "Physics Master",
                    ViewCount = 4500000,
                    Platform = "YouTube"
                },
                new()
                {
                    Id = "IgRtbJybJms",
                    Title = "Mẹo vặt chế tạo và công cụ thông minh cực đỉnh",
                    Url = "https://www.youtube.com/shorts/IgRtbJybJms",
                    Duration = 30,
                    Thumbnail = "https://i.ytimg.com/vi/IgRtbJybJms/hqdefault.jpg",
                    Uploader = "Genius Hacks",
                    ViewCount = 7100000,
                    Platform = "YouTube"
                },
                new()
                {
                    Id = "F4eJwJvZRYg",
                    Title = "Kỹ năng gọt hoa quả dưa hấu khổng lồ siêu mãn nhãn",
                    Url = "https://www.youtube.com/shorts/F4eJwJvZRYg",
                    Duration = 62,
                    Thumbnail = "https://i.ytimg.com/vi/F4eJwJvZRYg/hqdefault.jpg",
                    Uploader = "Street Food Art",
                    ViewCount = 8300000,
                    Platform = "YouTube"
                },
                new()
                {
                    Id = "qwc4filNNBI",
                    Title = "Cắt cát động lực học Kinetic Sand ASMR thư giãn",
                    Url = "https://www.youtube.com/shorts/qwc4filNNBI",
                    Duration = 59,
                    Thumbnail = "https://i.ytimg.com/vi/qwc4filNNBI/hqdefault.jpg",
                    Uploader = "Sand Master",
                    ViewCount = 11000000,
                    Platform = "YouTube"
                },
                new()
                {
                    Id = "4sC5FgiGvjY",
                    Title = "Spiral Illusion Hypnotic Visual - Ảo giác xoắn ốc kỳ thú",
                    Url = "https://www.youtube.com/shorts/4sC5FgiGvjY",
                    Duration = 50,
                    Thumbnail = "https://i.ytimg.com/vi/4sC5FgiGvjY/hqdefault.jpg",
                    Uploader = "Illusion World",
                    ViewCount = 6100000,
                    Platform = "YouTube"
                },
                new()
                {
                    Id = "C9XH7R0It8w",
                    Title = "Squaring the circle - Hiện tượng hình học kỳ diệu",
                    Url = "https://www.youtube.com/shorts/C9XH7R0It8w",
                    Duration = 30,
                    Thumbnail = "https://i.ytimg.com/vi/C9XH7R0It8w/hqdefault.jpg",
                    Uploader = "Math Curiosities",
                    ViewCount = 2900000,
                    Platform = "YouTube"
                },
                new()
                {
                    Id = "7x2oL3Kzoog",
                    Title = "Thí nghiệm vật lý độc lạ và cực kỳ mãn nhãn",
                    Url = "https://www.youtube.com/shorts/7x2oL3Kzoog",
                    Duration = 46,
                    Thumbnail = "https://i.ytimg.com/vi/7x2oL3Kzoog/hqdefault.jpg",
                    Uploader = "Science Lab",
                    ViewCount = 3200000,
                    Platform = "YouTube"
                },
                new()
                {
                    Id = "U3a1-omUuWY",
                    Title = "Phục chế thanh kim loại rỉ sét sáng bóng như mới",
                    Url = "https://www.youtube.com/shorts/U3a1-omUuWY",
                    Duration = 37,
                    Thumbnail = "https://i.ytimg.com/vi/U3a1-omUuWY/hqdefault.jpg",
                    Uploader = "Restoration ASMR",
                    ViewCount = 7500000,
                    Platform = "YouTube"
                }
            };

            string lower = (query ?? string.Empty).ToLowerInvariant();
            var matched = allCurated.Where(v =>
            {
                if (lower.Contains("asmr") || lower.Contains("thoả mãn") || lower.Contains("thoa man") || lower.Contains("xà phòng") || lower.Contains("cát"))
                    return v.Id is "RUmWVSGkDe0" or "qwc4filNNBI";
                if (lower.Contains("khoa học") || lower.Contains("khoa hoc") || lower.Contains("thí nghiệm") || lower.Contains("thi nghiem") || lower.Contains("vật lý"))
                    return v.Id is "7x2oL3Kzoog" or "Oqm7YrV9Qcc" or "C9XH7R0It8w";
                if (lower.Contains("cơ khí") || lower.Contains("co khi") || lower.Contains("tiện") || lower.Contains("kim loại") || lower.Contains("chế tác"))
                    return v.Id is "e4AZWrpJ4ww" or "U3a1-omUuWY";
                if (lower.Contains("ảo giác") || lower.Contains("ao giac") || lower.Contains("illusion"))
                    return v.Id is "4sC5FgiGvjY" or "C9XH7R0It8w";
                if (lower.Contains("mẹo") || lower.Contains("meo") || lower.Contains("thông minh"))
                    return v.Id is "IgRtbJybJms" or "U3a1-omUuWY";
                if (lower.Contains("món") || lower.Contains("mon") || lower.Contains("ăn") || lower.Contains("ẩm thực") || lower.Contains("hoa quả"))
                    return v.Id is "F4eJwJvZRYg";
                return true;
            }).ToList();

            var pool = matched.Count >= count ? matched : allCurated;
            return pool.Take(Math.Max(1, count)).ToList();
        }

        public async Task<DiscoveredVideoItem?> FindBestVideoForTopicAsync(
            string? topic = null,
            CancellationToken cancellationToken = default)
        {
            string targetQuery = string.IsNullOrWhiteSpace(topic)
                ? TrendingTopics[_random.Next(TrendingTopics.Count)]
                : topic.Trim();

            var candidates = await SearchVideosAsync(targetQuery, 10, cancellationToken);
            if (candidates.Count == 0)
            {
                // Fallback to random popular trend
                string fallbackQuery = TrendingTopics[_random.Next(TrendingTopics.Count)];
                candidates = await SearchVideosAsync(fallbackQuery, 8, cancellationToken);
            }

            if (candidates.Count == 0) return null;

            // Sàng lọc nghiêm ngặt: Chỉ lấy video ngắn chuẩn từ 12s đến 65s, không dính từ khóa cấm
            var validShorts = candidates
                .Where(c => c.Duration >= 12 && c.Duration <= 65 && !IsBlacklistedTitle(c.Title))
                .ToList();

            var pool = validShorts.Count > 0 ? validShorts : candidates;

            // Ưu tiên độ dài lý tưởng cho Shorts giữ chân người xem (20s - 50s) và lượt xem cao
            var best = pool
                .OrderByDescending(c => (c.Duration >= 20 && c.Duration <= 50) ? 100 : 10)
                .ThenByDescending(c => c.ViewCount)
                .FirstOrDefault();

            return best ?? pool[0];
        }

        private static string ResolveScriptPath()
        {
            string baseDir = AppContext.BaseDirectory;
            string p1 = Path.Combine(baseDir, "Assets", "scripts", "video_download_worker.py");
            if (File.Exists(p1)) return p1;

            string p2 = Path.Combine(baseDir, "..", "..", "..", "Assets", "scripts", "video_download_worker.py");
            if (File.Exists(p2)) return Path.GetFullPath(p2);

            string p3 = Path.Combine(baseDir, "..", "..", "..", "VideoNarrator", "Assets", "scripts", "video_download_worker.py");
            if (File.Exists(p3)) return Path.GetFullPath(p3);

            string p4 = @"d:\code\tao video\VideoNarrator\Assets\scripts\video_download_worker.py";
            if (File.Exists(p4)) return p4;

            return p1;
        }

        private static string ResolvePythonPath()
        {
            string defaultPython = @"C:\Users\nguye\AppData\Local\Programs\Python\Python312\python.exe";
            if (File.Exists(defaultPython)) return defaultPython;

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string py311 = Path.Combine(appData, "Programs", "Python", "Python311", "python.exe");
            if (File.Exists(py311)) return py311;

            return "python";
        }
    }
}

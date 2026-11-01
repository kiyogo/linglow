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
            "Động vật đáng yêu và thông minh",
            "Chế tác cơ khí chính xác đỉnh cao",
            "Món ăn đường phố độc lạ thế giới",
            "Bí ẩn và hiện tượng khó tin",
            "Kỹ năng thủ công mỹ nghệ đỉnh chóp"
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
                        // Lọc kép: Chỉ chấp nhận video ngắn chuẩn (22s - 62s, loại bỏ video quá ngắn) và video gốc
                        var filtered = list
                            .Where(v => v.Duration >= 22 && v.Duration <= 62 && !IsBlacklistedTitle(v.Title))
                            .ToList();

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
                "audiobook", "sách nói", "part ", "tập ", "phần ", "cho bé", "dạy bé"
            };
            return bad.Any(b => lower.Contains(b));
        }

        private static List<DiscoveredVideoItem> GetCuratedFallbackVideos(string query, int count)
        {
            var fallbackPool = new List<DiscoveredVideoItem>
            {
                new()
                {
                    Id = "__VcPjEzeOI",
                    Title = "Chú chó Husky thông minh xử lý tình huống trước camera",
                    Url = "https://www.youtube.com/shorts/__VcPjEzeOI",
                    Duration = 46,
                    Thumbnail = "https://i.ytimg.com/vi/__VcPjEzeOI/hqdefault.jpg",
                    Uploader = "Smart Pets",
                    ViewCount = 3850000,
                    Platform = "YouTube"
                },
                new()
                {
                    Id = "8ap9kR5M8Kg",
                    Title = "Nghệ thuật cắt xà phòng Soap Cutting ASMR",
                    Url = "https://www.youtube.com/shorts/8ap9kR5M8Kg",
                    Duration = 46,
                    Thumbnail = "https://i.ytimg.com/vi/8ap9kR5M8Kg/hqdefault.jpg",
                    Uploader = "Ameerah ASMR",
                    ViewCount = 6790000,
                    Platform = "YouTube"
                },
                new()
                {
                    Id = "woze7VTcawI",
                    Title = "Cún cưng phản ứng đáng yêu khi nghe gọi qua chuông cửa",
                    Url = "https://www.youtube.com/shorts/woze7VTcawI",
                    Duration = 35,
                    Thumbnail = "https://i.ytimg.com/vi/woze7VTcawI/hqdefault.jpg",
                    Uploader = "RingTV",
                    ViewCount = 4200000,
                    Platform = "YouTube"
                },
                new()
                {
                    Id = "SLw74rF7uVU",
                    Title = "Kỹ thuật pha màu Slime độc lạ cực kỳ thỏa mãn",
                    Url = "https://www.youtube.com/shorts/SLw74rF7uVU",
                    Duration = 29,
                    Thumbnail = "https://i.ytimg.com/vi/SLw74rF7uVU/hqdefault.jpg",
                    Uploader = "Satisfying Lab",
                    ViewCount = 37800000,
                    Platform = "YouTube"
                }
            };

            return fallbackPool.Take(Math.Max(1, count)).ToList();
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

            // Sàng lọc nghiêm ngặt: Chỉ lấy video ngắn chuẩn từ 22s đến 62s, không dính từ khóa cấm
            var validShorts = candidates
                .Where(c => c.Duration >= 22 && c.Duration <= 62 && !IsBlacklistedTitle(c.Title))
                .ToList();

            var pool = validShorts.Count > 0 ? validShorts : candidates;

            // Ưu tiên độ dài lý tưởng cho Shorts giữ chân người xem (28s - 58s) và lượt xem cao
            var best = pool
                .OrderByDescending(c => (c.Duration >= 28 && c.Duration <= 58) ? 100 : 10)
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

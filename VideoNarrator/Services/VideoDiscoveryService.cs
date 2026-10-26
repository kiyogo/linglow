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
                        _fileService.Log($"[Video Discovery] Tìm thấy {list.Count} video trực tuyến phù hợp.");
                        return list;
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

        private static List<DiscoveredVideoItem> GetCuratedFallbackVideos(string query, int count)
        {
            var fallbackPool = new List<DiscoveredVideoItem>
            {
                new()
                {
                    Id = "UivrsuLad_U",
                    Title = "Người đàn ông bấm vé tàu siêu tốc tại ga Ấn Độ",
                    Url = "https://www.youtube.com/shorts/UivrsuLad_U",
                    Duration = 23,
                    Thumbnail = "https://i.ytimg.com/vi/UivrsuLad_U/hqdefault.jpg",
                    Uploader = "Linglow Trending",
                    ViewCount = 1250000,
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
                    Id = "S7ENUwUmWro",
                    Title = "Thí nghiệm vật lý và hiện tượng khoa học kỳ thú",
                    Url = "https://www.youtube.com/watch?v=S7ENUwUmWro",
                    Duration = 60,
                    Thumbnail = "https://i.ytimg.com/vi/S7ENUwUmWro/hqdefault.jpg",
                    Uploader = "Science Explorer",
                    ViewCount = 850000,
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

            // Sort: Prioritize ideal short duration (20s - 65s) and higher views
            var best = candidates
                .OrderByDescending(c => (c.Duration >= 18 && c.Duration <= 65) ? 100 : 10)
                .ThenByDescending(c => c.ViewCount)
                .FirstOrDefault();

            return best ?? candidates[0];
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

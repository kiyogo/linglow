using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace VideoNarrator.Services
{
    public class VideoDownloadResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("filePath")]
        public string FilePath { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("duration")]
        public double Duration { get; set; }

        [JsonPropertyName("error")]
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public interface IVideoDownloadService
    {
        bool IsSupportedUrl(string url);

        Task<VideoDownloadResult> DownloadVideoAsync(
            string url,
            string? targetDirectory = null,
            Action<string, double>? progressCallback = null,
            CancellationToken cancellationToken = default);
    }

    public class VideoDownloadService : IVideoDownloadService
    {
        private readonly IFileService _fileService;
        private readonly HttpClient _httpClient;

        public VideoDownloadService(IFileService fileService, HttpClient? httpClient = null)
        {
            _fileService = fileService;
            _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        }

        public static bool IsSupportedUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;

            string host = uri.Host.ToLowerInvariant();
            string path = uri.AbsolutePath.ToLowerInvariant();

            // Direct video extensions
            if (path.EndsWith(".mp4") || path.EndsWith(".mov") || path.EndsWith(".webm") || path.EndsWith(".mkv"))
                return true;

            // Social platforms supported by yt-dlp
            if (host.Contains("tiktok.com") ||
                host.Contains("douyin.com") ||
                host.Contains("youtube.com") || host.Contains("youtu.be") ||
                host.Contains("facebook.com") || host.Contains("fb.watch") || host.Contains("fb.com") ||
                host.Contains("instagram.com") ||
                host.Contains("twitter.com") || host.Contains("x.com") ||
                host.Contains("reddit.com") ||
                host.Contains("pinterest.com") || host.Contains("pin.it") ||
                host.Contains("threads.net") ||
                host.Contains("vimeo.com") ||
                host.Contains("bilibili.com") ||
                host.Contains("kuaishou.com"))
            {
                return true;
            }

            return false;
        }

        bool IVideoDownloadService.IsSupportedUrl(string url) => IsSupportedUrl(url);

        public async Task<VideoDownloadResult> DownloadVideoAsync(
            string url,
            string? targetDirectory = null,
            Action<string, double>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return new VideoDownloadResult { Success = false, ErrorMessage = "URL video không được để trống." };
            }

            url = url.Trim();
            string outputDir = string.IsNullOrWhiteSpace(targetDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Linglow_Downloads")
                : targetDirectory;

            if (!Directory.Exists(outputDir))
            {
                try { Directory.CreateDirectory(outputDir); } catch { }
            }

            progressCallback?.Invoke("Đang kết nối và tải video từ mạng...", 10);
            _fileService.Log($"Bắt đầu tải video từ URL: {url} vào {outputDir}");

            // 1. Kiểm tra nếu là link tải trực tiếp MP4
            if (url.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                url.EndsWith(".mov", StringComparison.OrdinalIgnoreCase) ||
                url.EndsWith(".webm", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    progressCallback?.Invoke("Đang tải file video trực tiếp...", 30);
                    string fileName = Path.GetFileName(new Uri(url).LocalPath);
                    if (string.IsNullOrWhiteSpace(fileName)) fileName = $"download_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";
                    string destPath = Path.Combine(outputDir, fileName);

                    using var resp = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    resp.EnsureSuccessStatusCode();

                    await using var contentStream = await resp.Content.ReadAsStreamAsync(cancellationToken);
                    await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await contentStream.CopyToAsync(fileStream, cancellationToken);

                    progressCallback?.Invoke("Tải video hoàn tất!", 100);
                    _fileService.Log($"Tải trực tiếp thành công: {destPath}");
                    return new VideoDownloadResult
                    {
                        Success = true,
                        FilePath = destPath,
                        Title = Path.GetFileNameWithoutExtension(destPath)
                    };
                }
                catch (Exception ex)
                {
                    _fileService.Log($"Tải trực tiếp thất bại, chuyển sang yt-dlp: {ex.Message}", "WARN");
                }
            }

            // 2. Chạy worker script yt-dlp
            string scriptPath = ResolveScriptPath();
            string pythonPath = ResolvePythonPath();

            if (!File.Exists(scriptPath))
            {
                return new VideoDownloadResult
                {
                    Success = false,
                    ErrorMessage = $"Không tìm thấy kịch bản tải video tại {scriptPath}"
                };
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = pythonPath,
                    Arguments = $"\"{scriptPath}\" \"{url}\" \"{outputDir}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using var process = new Process { StartInfo = psi };
                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        outputBuilder.AppendLine(e.Data);
                        progressCallback?.Invoke("Đang tải các luồng video và âm thanh...", 60);
                    }
                };

                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        errorBuilder.AppendLine(e.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync(cancellationToken);

                string outputJson = outputBuilder.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(outputJson))
                {
                    // Lấy dòng json cuối cùng
                    string[] lines = outputJson.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        if (line.TrimStart().StartsWith("{") && line.TrimEnd().EndsWith("}"))
                        {
                            try
                            {
                                var res = JsonSerializer.Deserialize<VideoDownloadResult>(line);
                                if (res != null && res.Success && File.Exists(res.FilePath))
                                {
                                    progressCallback?.Invoke("Tải video thành công!", 100);
                                    _fileService.Log($"Tải video thành công: {res.FilePath}");
                                    return res;
                                }
                                else if (res != null && !res.Success)
                                {
                                    return res;
                                }
                            }
                            catch { }
                        }
                    }
                }

                string errText = errorBuilder.ToString();
                _fileService.Log($"Lỗi tải video từ yt-dlp: {errText}", "ERROR");
                return new VideoDownloadResult
                {
                    Success = false,
                    ErrorMessage = !string.IsNullOrWhiteSpace(errText) ? errText : "Không thể tải video từ URL này. Vui lòng kiểm tra lại đường link."
                };
            }
            catch (Exception ex)
            {
                _fileService.Log($"Ngoại lệ khi tải video: {ex.Message}", "ERROR", ex);
                return new VideoDownloadResult
                {
                    Success = false,
                    ErrorMessage = $"Lỗi xử lý tải video: {ex.Message}"
                };
            }
        }

        private static string ResolveScriptPath()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Assets", "scripts", "video_download_worker.py"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "scripts", "video_download_worker.py"),
                Path.Combine(Directory.GetCurrentDirectory(), "VideoNarrator", "Assets", "scripts", "video_download_worker.py"),
                Path.Combine(Directory.GetCurrentDirectory(), "Assets", "scripts", "video_download_worker.py"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Assets", "scripts", "video_download_worker.py"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "VideoNarrator", "Assets", "scripts", "video_download_worker.py")
            };

            foreach (var c in candidates)
            {
                try
                {
                    string full = Path.GetFullPath(c);
                    if (File.Exists(full)) return full;
                }
                catch { }
            }

            return Path.Combine(AppContext.BaseDirectory, "Assets", "scripts", "video_download_worker.py");
        }

        private static string ResolvePythonPath()
        {
            var candidates = new[]
            {
                @"C:\Users\nguye\AppData\Local\Programs\Python\Python312\python.exe",
                "python.exe",
                "python"
            };

            foreach (var c in candidates)
            {
                if (c.Contains('\\') && File.Exists(c)) return c;
            }

            return "python";
        }
    }
}

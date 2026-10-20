using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VideoNarrator.Services
{
    public class VideoProbeResult
    {
        public bool Success { get; set; }
        public string AudioText { get; set; } = string.Empty;
        public string OcrText { get; set; } = string.Empty;
        public string SuggestedTopic { get; set; } = string.Empty;
        public string? Error { get; set; }
    }

    public interface IVideoProbeService
    {
        Task<VideoProbeResult> ProbeVideoAsync(string videoPath, CancellationToken cancellationToken = default);
    }

    public class VideoProbeService : IVideoProbeService
    {
        private readonly IFileService _fileService;
        private readonly IFfmpegService _ffmpegService;

        public VideoProbeService(IFileService fileService, IFfmpegService ffmpegService)
        {
            _fileService = fileService;
            _ffmpegService = ffmpegService;
        }

        public async Task<VideoProbeResult> ProbeVideoAsync(string videoPath, CancellationToken cancellationToken = default)
        {
            var fallbackResult = new VideoProbeResult { Success = false };
            if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
            {
                return fallbackResult;
            }

            string pythonExe = FindPythonExecutable();
            string scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "scripts", "video_probe_worker.py");
            if (!File.Exists(scriptPath))
            {
                // Try relative to workspace
                scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "scripts", "video_probe_worker.py");
            }

            if (!File.Exists(scriptPath))
            {
                _fileService.Log($"Không tìm thấy script video_probe_worker.py tại: {scriptPath}", "WARN");
                return fallbackResult;
            }

            string tempJson = Path.Combine(Path.GetTempPath(), $"vn_probe_{Guid.NewGuid():N}.json");
            string ffmpegPath = _ffmpegService.FfmpegPath;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"\"{scriptPath}\" --video \"{videoPath}\" --output-json \"{tempJson}\" --ffmpeg \"{ffmpegPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using var proc = new Process { StartInfo = psi };
                proc.Start();

                // Wait up to 12 seconds
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(12));

                await proc.WaitForExitAsync(cts.Token);

                if (File.Exists(tempJson) && new FileInfo(tempJson).Length > 0)
                {
                    string json = await File.ReadAllTextAsync(tempJson, Encoding.UTF8, cancellationToken);
                    var parsed = JsonSerializer.Deserialize<JsonElement>(json);

                    var res = new VideoProbeResult
                    {
                        Success = parsed.TryGetProperty("success", out var s) && s.GetBoolean(),
                        AudioText = parsed.TryGetProperty("audio_text", out var at) ? at.GetString() ?? "" : "",
                        OcrText = parsed.TryGetProperty("ocr_text", out var ot) ? ot.GetString() ?? "" : "",
                        SuggestedTopic = parsed.TryGetProperty("suggested_topic", out var st) ? st.GetString() ?? "" : "",
                        Error = parsed.TryGetProperty("error", out var err) ? err.GetString() : null
                    };

                    _fileService.Log($"Nhận diện bối cảnh video: Audio='{res.AudioText}', OCR='{res.OcrText}', Gợi ý='{res.SuggestedTopic}'");
                    return res;
                }
            }
            catch (Exception ex)
            {
                _fileService.Log($"Lỗi thăm dò bối cảnh video: {ex.Message}", "WARN");
            }
            finally
            {
                if (File.Exists(tempJson))
                {
                    try { File.Delete(tempJson); } catch { }
                }
            }

            return fallbackResult;
        }

        private static string FindPythonExecutable()
        {
            string[] candidatePaths =
            {
                @"C:\Users\nguye\AppData\Local\Programs\Python\Python312\python.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python\Python312\python.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python\Python313\python.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python\Python314\python.exe"),
                @"C:\Python312\python.exe",
                @"C:\Python311\python.exe",
                @"C:\Program Files\Python312\python.exe"
            };

            foreach (var path in candidatePaths)
            {
                if (File.Exists(path)) return path;
            }

            string? envPath = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(envPath))
            {
                foreach (var dir in envPath.Split(Path.PathSeparator))
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(dir)) continue;
                        string full = Path.Combine(dir.Trim(), "python.exe");
                        if (File.Exists(full) && !full.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase))
                        {
                            return full;
                        }
                    }
                    catch { }
                }
            }

            return "python";
        }
    }
}

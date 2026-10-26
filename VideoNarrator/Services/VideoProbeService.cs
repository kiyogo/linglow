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
        public List<string> KeyframeBase64List { get; set; } = new();
        public string? Error { get; set; }
    }

    public interface IVideoProbeService
    {
        Task<VideoProbeResult> ProbeVideoAsync(string videoPath, CancellationToken cancellationToken = default);
        Task<List<string>> ExtractKeyframesBase64Async(string videoPath, double durationSeconds, int count = 3, CancellationToken cancellationToken = default);
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

                    res.KeyframeBase64List = await ExtractKeyframesBase64Async(videoPath, 0, 3, cancellationToken);
                    _fileService.Log($"Nhận diện bối cảnh video: Audio='{res.AudioText}', OCR='{res.OcrText}', Gợi ý='{res.SuggestedTopic}', Keyframes={res.KeyframeBase64List.Count}");
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

            // Trích xuất khung hình ảnh trực tiếp qua FFmpeg ngay cả khi python probe vắng mặt
            var keyframes = await ExtractKeyframesBase64Async(videoPath, 0, 3, cancellationToken);
            return new VideoProbeResult
            {
                Success = keyframes.Count > 0,
                SuggestedTopic = Path.GetFileNameWithoutExtension(videoPath),
                KeyframeBase64List = keyframes
            };
        }

        public async Task<List<string>> ExtractKeyframesBase64Async(string videoPath, double durationSeconds, int count = 3, CancellationToken cancellationToken = default)
        {
            var list = new List<string>();
            if (!File.Exists(videoPath) || !_ffmpegService.IsFfmpegAvailable) return list;

            try
            {
                double safeDur = durationSeconds > 1 ? durationSeconds : await _ffmpegService.GetMediaDurationAsync(videoPath, cancellationToken);
                if (safeDur <= 0.5) safeDur = 10;

                double[] ratios = count switch
                {
                    1 => new[] { 0.35 },
                    2 => new[] { 0.25, 0.70 },
                    3 => new[] { 0.15, 0.50, 0.85 },
                    _ => new[] { 0.10, 0.35, 0.65, 0.90 }
                };

                string tempDir = _fileService.CreateTempDirectory();
                try
                {
                    for (int i = 0; i < ratios.Length; i++)
                    {
                        double sec = safeDur * ratios[i];
                        string outJpg = Path.Combine(tempDir, $"kf_{i}.jpg");
                        var psi = new ProcessStartInfo
                        {
                            FileName = _ffmpegService.FfmpegPath,
                            Arguments = $"-y -ss {sec.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} -i \"{videoPath}\" -vframes 1 -vf \"scale='min(640,iw)':-2\" -q:v 3 \"{outJpg}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var proc = Process.Start(psi);
                        if (proc != null)
                        {
                            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                            cts.CancelAfter(TimeSpan.FromSeconds(5));
                            await proc.WaitForExitAsync(cts.Token);

                            if (File.Exists(outJpg) && new FileInfo(outJpg).Length > 0)
                            {
                                byte[] bytes = await File.ReadAllBytesAsync(outJpg, cancellationToken);
                                list.Add(Convert.ToBase64String(bytes));
                            }
                        }
                    }
                }
                finally
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
            catch (Exception ex)
            {
                _fileService.Log($"ExtractKeyframesBase64Async gặp lỗi: {ex.Message}", "WARN");
            }

            return list;
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

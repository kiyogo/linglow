using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VideoNarrator.Helpers;
using VideoNarrator.Models;

namespace VideoNarrator.Services
{
    public interface IVideoService
    {
        Task<VideoInfo> GetVideoInfoAsync(string filePath, CancellationToken cancellationToken = default);
        bool IsSupportedVideoExtension(string filePath);
    }

    public class VideoService : IVideoService
    {
        private readonly IFfmpegService _ffmpegService;
        private readonly IFileService _fileService;

        public VideoService(IFfmpegService ffmpegService, IFileService fileService)
        {
            _ffmpegService = ffmpegService;
            _fileService = fileService;
        }

        public bool IsSupportedVideoExtension(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext is ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm";
        }

        public async Task<VideoInfo> GetVideoInfoAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Không tìm thấy tệp video: {filePath}", filePath);
            }

            if (!_ffmpegService.IsFfprobeAvailable)
            {
                throw new FileNotFoundException("Không tìm thấy ffprobe.exe để kiểm tra video.");
            }

            string quotedFile = ProcessHelper.QuoteArgument(filePath);
            string args = $"-v quiet -print_format json -show_format -show_streams {quotedFile}";

            var res = await ProcessHelper.RunProcessAsync(_ffmpegService.FfprobePath, args, cancellationToken);
            if (!res.IsSuccess)
            {
                _fileService.Log($"ffprobe đọc metadata thất bại: {res.StandardError}", "ERROR");
                throw new InvalidOperationException($"Không thể đọc thông tin video: {res.StandardError}");
            }

            var videoInfo = new VideoInfo
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                FileSize = new FileInfo(filePath).Length
            };

            try
            {
                using var doc = JsonDocument.Parse(res.StandardOutput);
                var root = doc.RootElement;

                // 1. Parse format info
                if (root.TryGetProperty("format", out var formatElem))
                {
                    if (formatElem.TryGetProperty("duration", out var durElem) &&
                        double.TryParse(durElem.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double dur))
                    {
                        videoInfo.DurationSeconds = dur;
                    }

                    if (formatElem.TryGetProperty("size", out var sizeElem) &&
                        long.TryParse(sizeElem.GetString(), out long sizeBytes))
                    {
                        videoInfo.FileSize = sizeBytes;
                    }
                }

                // 2. Parse streams info
                if (root.TryGetProperty("streams", out var streamsElem) && streamsElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var stream in streamsElem.EnumerateArray())
                    {
                        string codecType = stream.TryGetProperty("codec_type", out var typeElem) 
                            ? typeElem.GetString() ?? "" 
                            : "";

                        if (codecType == "video" && videoInfo.Width == 0)
                        {
                            if (stream.TryGetProperty("width", out var wElem)) videoInfo.Width = wElem.GetInt32();
                            if (stream.TryGetProperty("height", out var hElem)) videoInfo.Height = hElem.GetInt32();
                            if (stream.TryGetProperty("codec_name", out var cElem)) videoInfo.VideoCodec = cElem.GetString() ?? "";

                            // Calculate FPS
                            string rFrameRate = stream.TryGetProperty("r_frame_rate", out var rElem) ? rElem.GetString() ?? "" : "";
                            if (!string.IsNullOrEmpty(rFrameRate) && rFrameRate.Contains('/'))
                            {
                                var parts = rFrameRate.Split('/');
                                if (parts.Length == 2 &&
                                    double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double num) &&
                                    double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double den) &&
                                    den > 0)
                                {
                                    videoInfo.Fps = num / den;
                                }
                            }
                        }
                        else if (codecType == "audio" && !videoInfo.HasAudio)
                        {
                            videoInfo.HasAudio = true;
                            if (stream.TryGetProperty("codec_name", out var acElem))
                            {
                                videoInfo.AudioCodec = acElem.GetString() ?? "";
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _fileService.Log($"Lỗi phân tích JSON ffprobe: {ex.Message}", "WARN", ex);
            }

            _fileService.Log($"Video Info: {videoInfo.FileName}, {videoInfo.ResolutionFormatted}, {videoInfo.DurationFormatted}, {videoInfo.FpsFormatted}, Audio: {videoInfo.HasAudio}");
            return videoInfo;
        }
    }
}

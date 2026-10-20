using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VideoNarrator.Helpers;
using VideoNarrator.Models;

namespace VideoNarrator.Services
{
    public interface ISceneDetectionService
    {
        Task<List<VideoScene>> DetectScenesAsync(
            string videoFilePath,
            double durationSeconds,
            CancellationToken cancellationToken = default);
    }

    public class SceneDetectionService : ISceneDetectionService
    {
        private readonly IFfmpegService _ffmpegService;
        private readonly IFileService _fileService;

        public SceneDetectionService(IFfmpegService ffmpegService, IFileService fileService)
        {
            _ffmpegService = ffmpegService;
            _fileService = fileService;
        }

        public async Task<List<VideoScene>> DetectScenesAsync(
            string videoFilePath,
            double durationSeconds,
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(videoFilePath))
            {
                throw new FileNotFoundException("Tệp video không tồn tại", videoFilePath);
            }

            var scenes = new List<VideoScene>();

            try
            {
                // Detect scene change timestamps using FFmpeg scene filter
                string qFile = ProcessHelper.QuoteArgument(videoFilePath);
                string args = $"-i {qFile} -filter:v \"select='gt(scene,0.32)',metadata=print:file=-\" -f null -";

                var cutTimestamps = new List<double> { 0.0 };

                var res = await ProcessHelper.RunProcessAsync(
                    _ffmpegService.FfmpegPath,
                    args,
                    cancellationToken,
                    onOutputLine: line =>
                    {
                        var match = Regex.Match(line, @"pts_time:([0-9.]+)");
                        if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double t))
                        {
                            if (t > 0.5 && t < durationSeconds - 0.5 && (cutTimestamps.Count == 0 || t - cutTimestamps[^1] > 1.2))
                            {
                                cutTimestamps.Add(t);
                            }
                        }
                    });

                if (cutTimestamps.Count == 1 && durationSeconds > 10)
                {
                    // Fallback to intelligent pacing cuts if no sharp lighting changes detected
                    double segmentLen = Math.Clamp(durationSeconds / 4.0, 3.5, 8.0);
                    double cur = segmentLen;
                    while (cur < durationSeconds - 1.5)
                    {
                        cutTimestamps.Add(cur);
                        cur += segmentLen;
                    }
                }

                if (!cutTimestamps.Contains(durationSeconds))
                {
                    cutTimestamps.Add(durationSeconds);
                }

                cutTimestamps.Sort();

                for (int i = 0; i < cutTimestamps.Count - 1; i++)
                {
                    double start = cutTimestamps[i];
                    double end = cutTimestamps[i + 1];
                    if (end - start < 0.5) continue;

                    scenes.Add(new VideoScene
                    {
                        Id = $"scene_{i + 1:00}",
                        Start = Math.Round(start, 2),
                        End = Math.Round(end, 2),
                        Description = $"Phân đoạn {i + 1}: Diễn biến từ {start:0.#}s đến {end:0.#}s",
                        Importance = i == 0 ? "High" : "Medium",
                        ImportanceScore = i == 0 ? 0.95 : 0.70,
                        Subject = "Chủ thể chính",
                        Action = "Hành động chính trong cảnh",
                        CameraType = "Dynamic",
                        FocusX = 0.5,
                        FocusY = 0.5
                    });
                }
            }
            catch (Exception ex)
            {
                _fileService.Log($"Lỗi nhận diện scene tự động bằng FFmpeg: {ex.Message}. Sử dụng phân đoạn dự phòng.", "WARN", ex);
            }

            if (scenes.Count == 0)
            {
                scenes.Add(new VideoScene
                {
                    Id = "scene_01",
                    Start = 0.0,
                    End = Math.Max(1.0, durationSeconds),
                    Description = "Toàn bộ video",
                    Importance = "High",
                    ImportanceScore = 0.9,
                    Subject = "Chủ thể chính",
                    Action = "Diễn biến chính",
                    CameraType = "Static",
                    FocusX = 0.5,
                    FocusY = 0.5
                });
            }

            _fileService.Log($"Nhận diện được {scenes.Count} scenes trong video.");
            return scenes;
        }
    }
}

using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VideoNarrator.Helpers;
using VideoNarrator.Models;

namespace VideoNarrator.Services
{
    public interface IVideoComposerService
    {
        Task<string> ComposeVerticalShortAsync(
            string sourceVideoPath,
            string mixedAudioPath,
            string? subtitleAssPath,
            EditingPlan plan,
            string outputPath,
            int crf = 18,
            string preset = "medium",
            Action<string, double>? progressCallback = null,
            CancellationToken cancellationToken = default);
    }

    public class VideoComposerService : IVideoComposerService
    {
        private readonly IFfmpegService _ffmpegService;
        private readonly IFileService _fileService;
        private readonly FfmpegFilterBuilder _filterBuilder = new();

        public VideoComposerService(IFfmpegService ffmpegService, IFileService fileService)
        {
            _ffmpegService = ffmpegService;
            _fileService = fileService;
        }

        public async Task<string> ComposeVerticalShortAsync(
            string sourceVideoPath,
            string mixedAudioPath,
            string? subtitleAssPath,
            EditingPlan plan,
            string outputPath,
            int crf = 18,
            string preset = "medium",
            Action<string, double>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(sourceVideoPath))
            {
                throw new FileNotFoundException("Tệp video gốc không tồn tại.", sourceVideoPath);
            }

            if (!File.Exists(mixedAudioPath))
            {
                throw new FileNotFoundException("Tệp âm thanh tổng hợp không tồn tại.", mixedAudioPath);
            }

            int targetW = plan.OutputWidth > 0 ? plan.OutputWidth : 1080;
            int targetH = plan.OutputHeight > 0 ? plan.OutputHeight : 1920;
            int targetFps = plan.TargetFps > 0 ? plan.TargetFps : 30;

            progressCallback?.Invoke("Đang khởi tạo bộ lọc FFmpeg cho video 9:16...", 10);

            string qVideo = ProcessHelper.QuoteArgument(sourceVideoPath);
            string qAudio = ProcessHelper.QuoteArgument(mixedAudioPath);
            string qOut = ProcessHelper.QuoteArgument(outputPath);

            // 1. Build Scene Cut & Concat + Crop to 9:16
            double sourceDuration = await _ffmpegService.GetMediaDurationAsync(sourceVideoPath, cancellationToken);
            double mixedAudioDuration = await _ffmpegService.GetMediaDurationAsync(mixedAudioPath, cancellationToken);

            bool isUncut = plan.Scenes == null || plan.Scenes.Count == 0 ||
                           (plan.Scenes.Count == 1 && plan.Scenes[0].Start <= 0.1 && (sourceDuration <= 0 || plan.Scenes[0].End >= sourceDuration - 1.0));

            var scenesToRender = new List<VideoScene>();
            if (plan.Scenes != null)
            {
                foreach (var s in plan.Scenes)
                {
                    scenesToRender.Add(new VideoScene
                    {
                        Id = s.Id,
                        Start = s.Start,
                        End = s.End,
                        Description = s.Description,
                        Importance = s.Importance,
                        ImportanceScore = s.ImportanceScore,
                        FocusX = s.FocusX,
                        FocusY = s.FocusY
                    });
                }
            }

            // Chỉ bù/lặp cảnh trong chế độ CẮT GHÉP HIGHLIGHT nếu tổng thời lượng cảnh bị thiếu so với giọng đọc
            if (!isUncut && scenesToRender.Count > 0 && mixedAudioDuration > 0)
            {
                double scenesDur = scenesToRender.Sum(s => Math.Max(0, s.End - s.Start));
                if (scenesDur < mixedAudioDuration)
                {
                    double targetSceneDur = mixedAudioDuration;

                    for (int k = scenesToRender.Count - 1; k >= 0 && scenesDur < targetSceneDur; k--)
                    {
                        var sc = scenesToRender[k];
                        double maxEnd = (k == scenesToRender.Count - 1) ? sourceDuration : scenesToRender[k + 1].Start;
                        if (sc.End < maxEnd - 0.1)
                        {
                            double add = Math.Min(maxEnd - sc.End, targetSceneDur - scenesDur);
                            sc.End = Math.Round(sc.End + add, 2);
                            scenesDur = scenesToRender.Sum(s => Math.Max(0, s.End - s.Start));
                        }
                    }

                    for (int k = 0; k < scenesToRender.Count && scenesDur < targetSceneDur; k++)
                    {
                        var sc = scenesToRender[k];
                        double minStart = (k == 0) ? 0.0 : scenesToRender[k - 1].End;
                        if (sc.Start > minStart + 0.1)
                        {
                            double sub = Math.Min(sc.Start - minStart, targetSceneDur - scenesDur);
                            sc.Start = Math.Round(sc.Start - sub, 2);
                            scenesDur = scenesToRender.Sum(s => Math.Max(0, s.End - s.Start));
                        }
                    }

                    if (scenesDur < targetSceneDur && targetSceneDur - scenesDur <= 0.8 && scenesToRender.Count > 0)
                    {
                        scenesToRender[^1].End = Math.Min(sourceDuration, scenesToRender[^1].End + (targetSceneDur - scenesDur));
                        scenesDur = scenesToRender.Sum(s => Math.Max(0, s.End - s.Start));
                    }

                    int loopIdx = 1;
                    while (scenesDur < targetSceneDur)
                    {
                        double needed = targetSceneDur - scenesDur;
                        double segLen = Math.Min(needed, Math.Min(sourceDuration, 5.0));
                        if (segLen <= 0.2) break;

                        scenesToRender.Add(new VideoScene
                        {
                            Id = $"scene_comp_{loopIdx:D2}",
                            Start = 0.0,
                            End = Math.Round(Math.Min(sourceDuration, segLen), 2),
                            Description = "Khung cảnh tiếp nối khớp âm thanh",
                            Importance = "Medium",
                            ImportanceScore = 0.8,
                            FocusX = plan.FocusX,
                            FocusY = plan.FocusY
                        });
                        loopIdx++;
                        scenesDur = scenesToRender.Sum(s => Math.Max(0, s.End - s.Start));
                        if (loopIdx > 10) break;
                    }
                }
            }

            var (cutFilter, cutLabel) = _filterBuilder.BuildSceneCutAndConcatFilter(
                scenesToRender, sourceDuration, targetW, targetH, plan.FocusX, plan.FocusY);

            string currentLabel = cutLabel;
            string filterComplex = cutFilter;

            // 2. Build subtle zoom if enabled
            if (plan.ZoomEffects != null && plan.ZoomEffects.Count > 0)
            {
                string zoom = _filterBuilder.BuildZoomFilter(plan.ZoomEffects, targetW, targetH, targetFps);
                filterComplex += $";{currentLabel}{zoom}[v_zoomed]";
                currentLabel = "[v_zoomed]";
            }

            // 3. Burn Subtitles
            if (!string.IsNullOrWhiteSpace(subtitleAssPath) && File.Exists(subtitleAssPath))
            {
                string subFilter = _filterBuilder.BuildSubtitleFilter(subtitleAssPath);
                if (!string.IsNullOrEmpty(subFilter))
                {
                    filterComplex += $";{currentLabel}{subFilter}[v_subtitled]";
                    currentLabel = "[v_subtitled]";
                }
            }

            double totalScenesDur = scenesToRender.Count > 0 ? scenesToRender.Sum(s => Math.Max(0, s.End - s.Start)) : sourceDuration;
            double effectiveDur = isUncut
                ? (sourceDuration > 0 ? sourceDuration : (mixedAudioDuration > 0 ? mixedAudioDuration : totalScenesDur))
                : ((totalScenesDur > 0 && mixedAudioDuration > 0) ? Math.Max(totalScenesDur, mixedAudioDuration) : Math.Max(totalScenesDur, mixedAudioDuration));

            string durLimit = (effectiveDur > 0)
                ? $"-t {effectiveDur.ToString("0.000", CultureInfo.InvariantCulture)} "
                : "";

            // Enforce constant frame rate and pixel format for wide compatibility (TikTok, YouTube Shorts, Reels)
            string args = $"-y -i {qVideo} -i {qAudio} -filter_complex \"{filterComplex}\" -map \"{currentLabel}\" -map 1:a:0 " +
                          $"-r {targetFps} -c:v libx264 -pix_fmt yuv420p -preset {preset} -crf {crf} " +
                          $"-c:a aac -ar 48000 -ac 2 -b:a 192k {durLimit}{qOut}";

            _fileService.Log($"Bắt đầu render video hoàn thiện: ffmpeg {args}");
            progressCallback?.Invoke("Đang render video dọc 9:16 bằng FFmpeg...", 30);

            var res = await ProcessHelper.RunProcessAsync(
                _ffmpegService.FfmpegPath,
                args,
                cancellationToken,
                onOutputLine: line =>
                {
                    // Update progress periodically from output if available
                });

            if (!res.IsSuccess || !File.Exists(outputPath))
            {
                _fileService.Log($"Lỗi render video hoàn thiện: {res.StandardError}", "ERROR");
                throw new InvalidOperationException($"Lỗi xuất video 9:16: {res.StandardError}");
            }

            progressCallback?.Invoke("Render video hoàn tất thành công!", 100);
            _fileService.Log($"Xuất video hoàn chỉnh thành công: {outputPath} ({new FileInfo(outputPath).Length} bytes)");

            return outputPath;
        }
    }
}

using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VideoNarrator.Helpers;
using VideoNarrator.Models;

namespace VideoNarrator.Services
{
    public interface IProxyVideoService
    {
        Task<string> GenerateProxyPreviewAsync(
            string sourceVideoPath,
            string mixedAudioPath,
            string? subtitleAssPath,
            EditingPlan plan,
            string outputPath,
            CancellationToken cancellationToken = default);
    }

    public class ProxyVideoService : IProxyVideoService
    {
        private readonly IFfmpegService _ffmpegService;
        private readonly IFileService _fileService;
        private readonly FfmpegFilterBuilder _filterBuilder = new();

        public ProxyVideoService(IFfmpegService ffmpegService, IFileService fileService)
        {
            _ffmpegService = ffmpegService;
            _fileService = fileService;
        }

        public async Task<string> GenerateProxyPreviewAsync(
            string sourceVideoPath,
            string mixedAudioPath,
            string? subtitleAssPath,
            EditingPlan plan,
            string outputPath,
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(sourceVideoPath))
            {
                throw new FileNotFoundException("Tệp video gốc không tồn tại.", sourceVideoPath);
            }

            // Proxy resolution: calculated proportionally (max dimension <= 960, ultrafast for snappy preview)
            int targetW = plan.OutputWidth > 0 ? plan.OutputWidth : 1080;
            int targetH = plan.OutputHeight > 0 ? plan.OutputHeight : 1920;
            int proxyW = targetW / 2;
            int proxyH = targetH / 2;
            while (proxyW > 960 || proxyH > 960)
            {
                proxyW /= 2;
                proxyH /= 2;
            }
            proxyW = Math.Max(2, (proxyW / 2) * 2);
            proxyH = Math.Max(2, (proxyH / 2) * 2);

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
                            Id = $"scene_prev_{loopIdx:D2}",
                            Start = 0.0,
                            End = Math.Round(Math.Min(sourceDuration, segLen), 2),
                            Description = "Khung cảnh tiếp nối xem trước",
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
                scenesToRender, sourceDuration, proxyW, proxyH, plan.FocusX, plan.FocusY);

            string currentLabel = cutLabel;
            string filterComplex = cutFilter;

            // 2. Burn Subtitles
            if (!string.IsNullOrWhiteSpace(subtitleAssPath) && File.Exists(subtitleAssPath))
            {
                string subFilter = _filterBuilder.BuildSubtitleFilter(subtitleAssPath);
                if (!string.IsNullOrEmpty(subFilter))
                {
                    filterComplex += $";{currentLabel}{subFilter}[v_preview_sub]";
                    currentLabel = "[v_preview_sub]";
                }
            }

            double totalScenesDur = scenesToRender.Count > 0 ? scenesToRender.Sum(s => Math.Max(0, s.End - s.Start)) : sourceDuration;
            double effectiveDur = isUncut
                ? (sourceDuration > 0 ? sourceDuration : (mixedAudioDuration > 0 ? mixedAudioDuration : totalScenesDur))
                : ((totalScenesDur > 0 && mixedAudioDuration > 0) ? Math.Max(totalScenesDur, mixedAudioDuration) : Math.Max(totalScenesDur, mixedAudioDuration));

            string durLimit = (effectiveDur > 0)
                ? $"-t {effectiveDur.ToString("0.000", CultureInfo.InvariantCulture)} "
                : "";

            string args = $"-y -i {qVideo} -i {qAudio} -filter_complex \"{filterComplex}\" -map \"{currentLabel}\" -map 1:a:0 " +
                          $"-c:v libx264 -preset ultrafast -pix_fmt yuv420p -crf 28 -c:a aac -b:a 128k {durLimit}{qOut}";

            _fileService.Log($"Tạo proxy preview: ffmpeg {args}");
            var res = await ProcessHelper.RunProcessAsync(_ffmpegService.FfmpegPath, args, cancellationToken);

            if (!res.IsSuccess || !File.Exists(outputPath))
            {
                _fileService.Log($"Lỗi render proxy preview: {res.StandardError}", "WARN");
                throw new InvalidOperationException($"Không thể tạo video xem trước (proxy): {res.StandardError}");
            }

            _fileService.Log($"Tạo video xem trước thành công: {outputPath}");
            return outputPath;
        }
    }
}

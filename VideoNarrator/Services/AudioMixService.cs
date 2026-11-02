using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VideoNarrator.Helpers;
using VideoNarrator.Models;

namespace VideoNarrator.Services
{
    public interface IAudioMixService
    {
        Task<string> MixAudioTracksAsync(
            string voiceAudioPath,
            string? musicPath,
            List<SoundEffectEvent>? soundEffects,
            double musicVolume,
            bool enableDucking,
            double duckingVolume,
            double targetDuration,
            string outputPath,
            CancellationToken cancellationToken = default);
    }

    public class AudioMixService : IAudioMixService
    {
        private readonly IFfmpegService _ffmpegService;
        private readonly IFileService _fileService;

        public AudioMixService(IFfmpegService ffmpegService, IFileService fileService)
        {
            _ffmpegService = ffmpegService;
            _fileService = fileService;
        }

        public async Task<string> MixAudioTracksAsync(
            string voiceAudioPath,
            string? musicPath,
            List<SoundEffectEvent>? soundEffects,
            double musicVolume,
            bool enableDucking,
            double duckingVolume,
            double targetDuration,
            string outputPath,
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(voiceAudioPath))
            {
                throw new FileNotFoundException("Tệp âm thanh thuyết minh không tồn tại.", voiceAudioPath);
            }

            bool hasMusic = !string.IsNullOrWhiteSpace(musicPath) && File.Exists(musicPath);

            // Filter and resolve valid sound effects
            var validSfx = new List<SoundEffectEvent>();
            if (soundEffects != null)
            {
                foreach (var sfx in soundEffects)
                {
                    if (string.IsNullOrWhiteSpace(sfx.FilePath) || !File.Exists(sfx.FilePath))
                    {
                        string? resolved = ResolveSfxFilePath(sfx.SfxType);
                        if (!string.IsNullOrEmpty(resolved)) sfx.FilePath = resolved;
                    }

                    if (!string.IsNullOrWhiteSpace(sfx.FilePath) && File.Exists(sfx.FilePath))
                    {
                        validSfx.Add(sfx);
                    }
                }
            }

            if (!hasMusic && validSfx.Count == 0)
            {
                // No music or sfx to mix, just ensure audio is stereo 48000Hz AAC
                string qIn = ProcessHelper.QuoteArgument(voiceAudioPath);
                string qOut = ProcessHelper.QuoteArgument(outputPath);
                string durParam = (targetDuration > 0)
                    ? $"-af \"apad=whole_dur={targetDuration.ToString("0.000", CultureInfo.InvariantCulture)}\" -t {targetDuration.ToString("0.000", CultureInfo.InvariantCulture)} "
                    : "";
                string args = $"-y -i {qIn} {durParam}-c:a aac -ar 48000 -ac 2 -b:a 192k {qOut}";
                var simpleRes = await ProcessHelper.RunProcessAsync(_ffmpegService.FfmpegPath, args, cancellationToken);
                if (simpleRes.IsSuccess && File.Exists(outputPath))
                {
                    return outputPath;
                }
            }

            // Build multi-input FFmpeg command with sidechain ducking and mixing
            var inputArgs = new StringBuilder();
            inputArgs.Append($"-y -i {ProcessHelper.QuoteArgument(voiceAudioPath)} ");

            int inputIndex = 1;
            int musicIndex = -1;

            if (hasMusic)
            {
                musicIndex = inputIndex++;
                inputArgs.Append($"-stream_loop -1 -i {ProcessHelper.QuoteArgument(musicPath!)} ");
            }

            var sfxIndices = new List<int>();
            foreach (var sfx in validSfx)
            {
                sfxIndices.Add(inputIndex++);
                inputArgs.Append($"-i {ProcessHelper.QuoteArgument(sfx.FilePath)} ");
            }

            // Filter Complex
            var fc = new StringBuilder();
            fc.Append("\"");

            string voiceOutput = "[0:a]";
            if (targetDuration > 0)
            {
                string targetDurStr = targetDuration.ToString("0.000", CultureInfo.InvariantCulture);
                fc.Append($"[0:a]apad=whole_dur={targetDurStr}[v_pad];");
                voiceOutput = "[v_pad]";
            }

            var mixInputs = new List<string> { voiceOutput };

            if (hasMusic)
            {
                string normVol = musicVolume.ToString("0.00", CultureInfo.InvariantCulture);

                if (enableDucking)
                {
                    // Sidechain compression ducks music whenever voice speaks
                    fc.Append($"[{musicIndex}:a]volume={normVol}[bgm_norm];");
                    fc.Append($"[bgm_norm][0:a]sidechaincompress=threshold=0.08:ratio=3:attack=20:release=150[ducked_bgm];");
                    mixInputs.Add("[ducked_bgm]");
                }
                else
                {
                    fc.Append($"[{musicIndex}:a]volume={normVol}[bgm_norm];");
                    mixInputs.Add("[bgm_norm]");
                }
            }

            for (int i = 0; i < validSfx.Count; i++)
            {
                var sfx = validSfx[i];
                int sfxIdx = sfxIndices[i];
                int delayMs = (int)Math.Max(1, Math.Round(sfx.Start * 1000));
                string vol = sfx.Volume.ToString("0.00", CultureInfo.InvariantCulture);

                fc.Append($"[{sfxIdx}:a]adelay={delayMs}|{delayMs},volume={vol}[sfx_{i}];");
                mixInputs.Add($"[sfx_{i}]");
            }

            if (mixInputs.Count > 1)
            {
                // KHẮC PHỤC TRIỆT ĐỂ: normalize=0 và dropout_transition=0 đảm bảo giọng đọc đạt 100% âm lượng chuẩn
                // ngay từ mili-giây đầu tiên, xóa bỏ hoàn toàn hiện tượng 'lúc đầu âm thanh nhỏ rồi to dần lên'.
                string allInputs = string.Join("", mixInputs);
                fc.Append($"{allInputs}amix=inputs={mixInputs.Count}:duration=first:dropout_transition=0:normalize=0[aout]");
            }
            else
            {
                fc.Append($"{voiceOutput}anull[aout]");
            }

            fc.Append("\"");

            string qFinalOut = ProcessHelper.QuoteArgument(outputPath);
            string finalDurParam = (targetDuration > 0)
                ? $"-t {targetDuration.ToString("0.000", CultureInfo.InvariantCulture)} "
                : "";
            string fullArgs = $"{inputArgs} -filter_complex {fc} -map \"[aout]\" {finalDurParam}-c:a aac -ar 48000 -ac 2 -b:a 192k {qFinalOut}";

            _fileService.Log($"Mix âm thanh với FFmpeg: {fullArgs}");
            var res = await ProcessHelper.RunProcessAsync(_ffmpegService.FfmpegPath, fullArgs, cancellationToken);

            if (!res.IsSuccess || !File.Exists(outputPath))
            {
                _fileService.Log($"Mix âm thanh thất bại: {res.StandardError}. Sử dụng tệp voice đơn thuần.", "WARN");
                File.Copy(voiceAudioPath, outputPath, overwrite: true);
            }

            return outputPath;
        }

        public static string? ResolveSfxFilePath(string? sfxType)
        {
            if (string.IsNullOrWhiteSpace(sfxType)) sfxType = "whoosh";
            string clean = sfxType.Trim().ToLowerInvariant();

            string primaryName = "whoosh";
            if (clean.Contains("whoosh") || clean.Contains("transition") || clean.Contains("swipe") || clean.Contains("chuyen"))
                primaryName = "whoosh";
            else if (clean.Contains("pop") || clean.Contains("bubble") || clean.Contains("click") || clean.Contains("badge") || clean.Contains("nhan"))
                primaryName = "pop";
            else if (clean.Contains("ding") || clean.Contains("bell") || clean.Contains("fact") || clean.Contains("chuong") || clean.Contains("thongtin"))
                primaryName = "ding";
            else if (clean.Contains("impact") || clean.Contains("thud") || clean.Contains("boom") || clean.Contains("hook") || clean.Contains("no"))
                primaryName = "impact";
            else if (clean.Contains("sparkle") || clean.Contains("chime") || clean.Contains("magic") || clean.Contains("glitter") || clean.Contains("laplanh"))
                primaryName = "sparkle";
            else if (clean.Contains("camera") || clean.Contains("snap") || clean.Contains("shutter"))
                primaryName = "camera_click";

            var folders = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Assets", "audio", "sfx"),
                Path.Combine(Directory.GetCurrentDirectory(), "Assets", "audio", "sfx"),
                Path.Combine(Directory.GetCurrentDirectory(), "VideoNarrator", "Assets", "audio", "sfx"),
                Path.Combine(AppContext.BaseDirectory, "sfx")
            };

            var exts = new[] { ".mp3", ".wav" };
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var ext in exts)
                {
                    string candidate = Path.Combine(folder, primaryName + ext);
                    if (File.Exists(candidate)) return candidate;
                }
            }

            // Fallback: any sfx in folder
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder)) continue;
                var any = Directory.GetFiles(folder, "*.mp3").Concat(Directory.GetFiles(folder, "*.wav")).FirstOrDefault();
                if (any != null) return any;
            }

            return null;
        }
    }
}

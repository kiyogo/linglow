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

            // Filter valid sound effects
            var validSfx = new List<SoundEffectEvent>();
            if (soundEffects != null)
            {
                foreach (var sfx in soundEffects)
                {
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

            string currentVoice = "[0:a]";
            if (targetDuration > 0)
            {
                string targetDurStr = targetDuration.ToString("0.000", CultureInfo.InvariantCulture);
                fc.Append($"[0:a]apad=whole_dur={targetDurStr}[voice_padded];");
                currentVoice = "[voice_padded]";
            }

            if (hasMusic)
            {
                string normVol = musicVolume.ToString("0.00", CultureInfo.InvariantCulture);

                if (enableDucking)
                {
                    // Sidechain compression ducks music whenever voice speaks (uses [0:a] original speech as sidechain key)
                    fc.Append($"[{musicIndex}:a]volume={normVol}[bgm_norm];");
                    fc.Append($"[bgm_norm][0:a]sidechaincompress=threshold=0.06:ratio=4:attack=50:release=300[ducked_bgm];");
                    fc.Append($"{currentVoice}[ducked_bgm]amix=inputs=2:duration=first:dropout_transition=0.5[voice_music];");
                    currentVoice = "[voice_music]";
                }
                else
                {
                    fc.Append($"[{musicIndex}:a]volume={normVol}[bgm_norm];");
                    fc.Append($"{currentVoice}[bgm_norm]amix=inputs=2:duration=first:dropout_transition=0.5[voice_music];");
                    currentVoice = "[voice_music]";
                }
            }

            if (validSfx.Count > 0)
            {
                // Mix in SFX with adelay
                var sfxMixInputs = new StringBuilder(currentVoice);
                for (int i = 0; i < validSfx.Count; i++)
                {
                    var sfx = validSfx[i];
                    int sfxIdx = sfxIndices[i];
                    int delayMs = (int)Math.Max(0, sfx.Start * 1000);
                    string vol = sfx.Volume.ToString("0.00", CultureInfo.InvariantCulture);

                    fc.Append($"[{sfxIdx}:a]adelay={delayMs}|{delayMs},volume={vol}[sfx_{i}];");
                    sfxMixInputs.Append($"[sfx_{i}]");
                }

                fc.Append($"{sfxMixInputs}amix=inputs={1 + validSfx.Count}:duration=first:dropout_transition=0.5[aout]");
            }
            else
            {
                fc.Append($"{currentVoice}anull[aout]");
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
    }
}

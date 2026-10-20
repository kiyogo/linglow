using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VideoNarrator.Helpers;

namespace VideoNarrator.Services
{
    public interface IFfmpegService
    {
        string FfmpegPath { get; }
        string FfprobePath { get; }
        bool IsFfmpegAvailable { get; }
        bool IsFfprobeAvailable { get; }

        Task<double> GetMediaDurationAsync(string filePath, CancellationToken cancellationToken = default);
        Task AdjustAudioDurationAsync(string inputAudioPath, string outputAudioPath, double targetDurationSeconds, CancellationToken cancellationToken = default);
        Task MergeVideoAndAudioAsync(
            string videoPath,
            string audioPath,
            string? musicPath,
            double musicVolume,
            string audioHandlingMode,
            string outputPath,
            CancellationToken cancellationToken = default);
    }

    public class FfmpegService : IFfmpegService
    {
        private readonly IFileService _fileService;
        private string _ffmpegPath = string.Empty;
        private string _ffprobePath = string.Empty;

        public string FfmpegPath => _ffmpegPath;
        public string FfprobePath => _ffprobePath;
        public bool IsFfmpegAvailable => !string.IsNullOrEmpty(_ffmpegPath) && File.Exists(_ffmpegPath);
        public bool IsFfprobeAvailable => !string.IsNullOrEmpty(_ffprobePath) && File.Exists(_ffprobePath);

        public FfmpegService(IFileService fileService)
        {
            _fileService = fileService;
            LocateBinaries();
        }

        private void LocateBinaries()
        {
            string baseDir = AppContext.BaseDirectory;

            // 1. Check in Assets/ffmpeg
            string candidateFfmpeg = Path.Combine(baseDir, "Assets", "ffmpeg", "ffmpeg.exe");
            string candidateFfprobe = Path.Combine(baseDir, "Assets", "ffmpeg", "ffprobe.exe");

            if (File.Exists(candidateFfmpeg) && File.Exists(candidateFfprobe))
            {
                _ffmpegPath = candidateFfmpeg;
                _ffprobePath = candidateFfprobe;
                return;
            }

            // 2. Check next to executable
            candidateFfmpeg = Path.Combine(baseDir, "ffmpeg.exe");
            candidateFfprobe = Path.Combine(baseDir, "ffprobe.exe");

            if (File.Exists(candidateFfmpeg) && File.Exists(candidateFfprobe))
            {
                _ffmpegPath = candidateFfmpeg;
                _ffprobePath = candidateFfprobe;
                return;
            }

            // 3. Search in system PATH
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (string dir in pathEnv.Split(Path.PathSeparator))
                {
                    try
                    {
                        string pFfmpeg = Path.Combine(dir.Trim(), "ffmpeg.exe");
                        string pFfprobe = Path.Combine(dir.Trim(), "ffprobe.exe");
                        if (File.Exists(pFfmpeg) && string.IsNullOrEmpty(_ffmpegPath))
                        {
                            _ffmpegPath = pFfmpeg;
                        }
                        if (File.Exists(pFfprobe) && string.IsNullOrEmpty(_ffprobePath))
                        {
                            _ffprobePath = pFfprobe;
                        }
                    }
                    catch
                    {
                        // Ignore invalid directories in PATH
                    }
                }
            }

            // 4. Check in %LOCALAPPDATA%\VideoNarrator\ffmpeg\
            string appDataFfmpegDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoNarrator", "ffmpeg");
            string appDataFfmpeg = Path.Combine(appDataFfmpegDir, "ffmpeg.exe");
            string appDataFfprobe = Path.Combine(appDataFfmpegDir, "ffprobe.exe");

            if (File.Exists(appDataFfmpeg) && string.IsNullOrEmpty(_ffmpegPath))
            {
                _ffmpegPath = appDataFfmpeg;
            }
            if (File.Exists(appDataFfprobe) && string.IsNullOrEmpty(_ffprobePath))
            {
                _ffprobePath = appDataFfprobe;
            }

            // 5. If still missing, extract from embedded assembly resources
            if (string.IsNullOrEmpty(_ffmpegPath) || string.IsNullOrEmpty(_ffprobePath))
            {
                TryExtractEmbeddedBinaries(appDataFfmpegDir);
            }

            _fileService.Log($"FFmpeg path: {_ffmpegPath}, FFprobe path: {_ffprobePath}");
        }

        private void TryExtractEmbeddedBinaries(string targetDir)
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                Directory.CreateDirectory(targetDir);

                if (string.IsNullOrEmpty(_ffmpegPath))
                {
                    string destFfmpeg = Path.Combine(targetDir, "ffmpeg.exe");
                    if (!File.Exists(destFfmpeg))
                    {
                        using var stream = assembly.GetManifestResourceStream("VideoNarrator.Assets.ffmpeg.ffmpeg.exe");
                        if (stream != null)
                        {
                            using var fs = new FileStream(destFfmpeg, FileMode.Create, FileAccess.Write, FileShare.None);
                            stream.CopyTo(fs);
                        }
                    }
                    if (File.Exists(destFfmpeg))
                    {
                        _ffmpegPath = destFfmpeg;
                    }
                }

                if (string.IsNullOrEmpty(_ffprobePath))
                {
                    string destFfprobe = Path.Combine(targetDir, "ffprobe.exe");
                    if (!File.Exists(destFfprobe))
                    {
                        using var stream = assembly.GetManifestResourceStream("VideoNarrator.Assets.ffmpeg.ffprobe.exe");
                        if (stream != null)
                        {
                            using var fs = new FileStream(destFfprobe, FileMode.Create, FileAccess.Write, FileShare.None);
                            stream.CopyTo(fs);
                        }
                    }
                    if (File.Exists(destFfprobe))
                    {
                        _ffprobePath = destFfprobe;
                    }
                }
            }
            catch (Exception ex)
            {
                _fileService.Log($"Lỗi trích xuất FFmpeg nhúng: {ex.Message}");
            }
        }

        public async Task<double> GetMediaDurationAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (!IsFfprobeAvailable)
            {
                throw new FileNotFoundException("Không tìm thấy tệp ffprobe.exe. Vui lòng đặt ffprobe.exe trong Assets\\ffmpeg.");
            }

            string quotedFile = ProcessHelper.QuoteArgument(filePath);
            string args = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 {quotedFile}";

            var result = await ProcessHelper.RunProcessAsync(_ffprobePath, args, cancellationToken);
            if (!result.IsSuccess)
            {
                _fileService.Log($"ffprobe duration error: {result.StandardError}", "ERROR");
                throw new InvalidOperationException($"Lỗi khi lấy thời lượng tệp qua ffprobe: {result.StandardError}");
            }

            string output = result.StandardOutput.Trim();
            if (double.TryParse(output, NumberStyles.Any, CultureInfo.InvariantCulture, out double duration))
            {
                return duration;
            }

            return 0.0;
        }

        public string BuildAtempoFilter(double speed)
        {
            // Atempo only supports speeds between 0.5 and 2.0 per filter
            var sb = new StringBuilder();
            double remaining = speed;

            while (remaining > 2.0)
            {
                sb.Append("atempo=2.0,");
                remaining /= 2.0;
            }

            while (remaining < 0.5)
            {
                sb.Append("atempo=0.5,");
                remaining /= 0.5;
            }

            sb.Append($"atempo={remaining.ToString("0.0000", CultureInfo.InvariantCulture)}");
            return sb.ToString();
        }

        public async Task AdjustAudioDurationAsync(
            string inputAudioPath,
            string outputAudioPath,
            double targetDurationSeconds,
            CancellationToken cancellationToken = default)
        {
            if (!IsFfmpegAvailable)
            {
                throw new FileNotFoundException("Không tìm thấy tệp ffmpeg.exe.");
            }

            double originalAudioDuration = await GetMediaDurationAsync(inputAudioPath, cancellationToken);
            _fileService.Log($"Điều chỉnh audio: Hiện tại = {originalAudioDuration:F2}s, Video mục tiêu = {targetDurationSeconds:F2}s");

            double diff = Math.Abs(originalAudioDuration - targetDurationSeconds);
            if (diff <= 0.25 || originalAudioDuration >= targetDurationSeconds)
            {
                // BẢO TỒN NGUYÊN VẸN 100% GIỌNG NÓI THUYẾT MINH:
                // Tuyệt đối không dùng atempo để nén/tăng tốc audio giọng nói!
                // Giữ nguyên file gốc để đảm bảo chất giọng tự nhiên, rõ từng chữ và phụ đề khớp chuẩn xác 1:1.
                File.Copy(inputAudioPath, outputAudioPath, overwrite: true);
                return;
            }
            else
            {
                // Audio is shorter than targetDuration: đệm khoảng lặng bằng apad
                string qIn = ProcessHelper.QuoteArgument(inputAudioPath);
                string qOut = ProcessHelper.QuoteArgument(outputAudioPath);

                string filterStr = $"apad=whole_dur={targetDurationSeconds.ToString("0.000", CultureInfo.InvariantCulture)}";
                string args = $"-y -i {qIn} -af \"{filterStr}\" -c:a libmp3lame -q:a 2 {qOut}";
                _fileService.Log($"Đệm khoảng lặng cho audio khớp thời lượng đích ({targetDurationSeconds:F1}s): ffmpeg {args}");

                var res = await ProcessHelper.RunProcessAsync(_ffmpegPath, args, cancellationToken);
                if (!res.IsSuccess)
                {
                    _fileService.Log($"Lỗi thêm khoảng lặng audio: {res.StandardError}", "WARN");
                    File.Copy(inputAudioPath, outputAudioPath, true);
                }
            }
        }

        public async Task MergeVideoAndAudioAsync(
            string videoPath,
            string audioPath,
            string? musicPath,
            double musicVolume,
            string audioHandlingMode,
            string outputPath,
            CancellationToken cancellationToken = default)
        {
            if (!IsFfmpegAvailable)
            {
                throw new FileNotFoundException("Không tìm thấy tệp ffmpeg.exe.");
            }

            string qVideo = ProcessHelper.QuoteArgument(videoPath);
            string qAudio = ProcessHelper.QuoteArgument(audioPath);
            string qOut = ProcessHelper.QuoteArgument(outputPath);

            bool hasMusic = !string.IsNullOrWhiteSpace(musicPath) && File.Exists(musicPath);
            string musicVolStr = Math.Clamp(musicVolume, 0.01, 1.0).ToString("0.00", CultureInfo.InvariantCulture);

            double vidDur = await GetMediaDurationAsync(videoPath, cancellationToken);
            double audDur = await GetMediaDurationAsync(audioPath, cancellationToken);
            double maxDur = Math.Max(vidDur, audDur);
            string loopVid = (vidDur > 0 && audDur > vidDur + 0.3) ? "-stream_loop -1 " : "";
            string audLimit = (maxDur > 0) ? $"-t {maxDur.ToString("0.000", CultureInfo.InvariantCulture)} " : "";

            string args;

            if (audioHandlingMode == "KeepOriginalAudio")
            {
                if (hasMusic)
                {
                    string qMusic = ProcessHelper.QuoteArgument(musicPath!);
                    args = $"-y {loopVid}-i {qVideo} -i {qAudio} -stream_loop -1 -i {qMusic} " +
                           $"-filter_complex \"[0:a]volume=0.25[orig];[1:a]volume=1.0[narr];[2:a]volume={musicVolStr}[bgm];[orig][narr][bgm]amix=inputs=3:duration=first:dropout_transition=0.5[aout]\" " +
                           $"-map 0:v:0 -map \"[aout]\" -c:v copy -c:a aac -b:a 192k {audLimit}{qOut}";
                }
                else
                {
                    args = $"-y {loopVid}-i {qVideo} -i {qAudio} " +
                           $"-filter_complex \"[0:a]volume=0.25[orig];[1:a]volume=1.0[narr];[orig][narr]amix=inputs=2:duration=first:dropout_transition=0.5[aout]\" " +
                           $"-map 0:v:0 -map \"[aout]\" -c:v copy -c:a aac -b:a 192k {audLimit}{qOut}";
                }
            }
            else
            {
                // ReplaceOriginalAudio or MuteOriginalAudio
                if (hasMusic)
                {
                    string qMusic = ProcessHelper.QuoteArgument(musicPath!);
                    args = $"-y {loopVid}-i {qVideo} -i {qAudio} -stream_loop -1 -i {qMusic} " +
                           $"-filter_complex \"[1:a]volume=1.0[narr];[2:a]volume={musicVolStr}[bgm];[narr][bgm]amix=inputs=2:duration=first:dropout_transition=0.5[aout]\" " +
                           $"-map 0:v:0 -map \"[aout]\" -c:v copy -c:a aac -b:a 192k {audLimit}{qOut}";
                }
                else
                {
                    args = $"-y {loopVid}-i {qVideo} -i {qAudio} -map 0:v:0 -map 1:a:0 -c:v copy -c:a aac -b:a 192k {audLimit}{qOut}";
                }
            }

            _fileService.Log($"Ghép video và audio: ffmpeg {args}");
            var res = await ProcessHelper.RunProcessAsync(_ffmpegPath, args, cancellationToken);

            if (!res.IsSuccess || !File.Exists(outputPath))
            {
                _fileService.Log($"Ghép video bằng copy stream thất bại. Đang thử lại với libx264 re-encode. Lỗi: {res.StandardError}", "WARN");

                // Fallback with re-encoding in case video stream copy has issues
                string fallbackArgs;
                if (hasMusic)
                {
                    string qMusic = ProcessHelper.QuoteArgument(musicPath!);
                    fallbackArgs = $"-y -i {qVideo} -i {qAudio} -stream_loop -1 -i {qMusic} " +
                                   $"-filter_complex \"[1:a]volume=1.0[narr];[2:a]volume={musicVolStr}[bgm];[narr][bgm]amix=inputs=2:duration=first:dropout_transition=0.5[aout]\" " +
                                   $"-map 0:v:0 -map \"[aout]\" -c:v libx264 -preset fast -crf 20 -c:a aac -b:a 192k {audLimit}{qOut}";
                }
                else
                {
                    fallbackArgs = $"-y -i {qVideo} -i {qAudio} -map 0:v:0 -map 1:a:0 -c:v libx264 -preset fast -crf 20 -c:a aac -b:a 192k {audLimit}{qOut}";
                }

                var fallbackRes = await ProcessHelper.RunProcessAsync(_ffmpegPath, fallbackArgs, cancellationToken);
                if (!fallbackRes.IsSuccess || !File.Exists(outputPath))
                {
                    _fileService.Log($"Lỗi hoàn toàn khi ghép video: {fallbackRes.StandardError}", "ERROR");
                    throw new InvalidOperationException($"Lỗi render video: {fallbackRes.StandardError}");
                }
            }

            _fileService.Log($"Đã ghép video thành công: {outputPath}");
        }
    }
}

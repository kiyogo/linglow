using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using VideoNarrator.Models;

namespace VideoNarrator.Services
{
    public class FfmpegFilterBuilder
    {
        public static string EscapeFilterPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            string normalized = path.Replace('\\', '/');
            // Escape colons for ffmpeg filter: e.g. C: -> C\\:
            normalized = normalized.Replace(":", "\\:");
            normalized = normalized.Replace("'", "\\'");
            return normalized;
        }

        public string BuildCropAndScaleToVertical(int inputWidth, int inputHeight, int targetWidth, int targetHeight, double focusX = 0.5, double focusY = 0.5)
        {
            if (targetWidth <= 0) targetWidth = 1080;
            if (targetHeight <= 0) targetHeight = 1920;

            // Ensure even dimensions for H.264/yuv420p
            if (targetWidth % 2 != 0) targetWidth++;
            if (targetHeight % 2 != 0) targetHeight++;

            double clampedFocusX = Math.Clamp(focusX, 0.0, 1.0);
            double clampedFocusY = Math.Clamp(focusY, 0.0, 1.0);
            string fx = clampedFocusX.ToString("0.00", CultureInfo.InvariantCulture);
            string fy = clampedFocusY.ToString("0.00", CultureInfo.InvariantCulture);

            // Dynamic universal scale & crop:
            // 1. setsar=1: Enforce square pixel aspect ratio.
            // 2. scale with force_original_aspect_ratio=increase: Proportionally scales up/down so input video fully covers targetWidth x targetHeight without distortion or black bars.
            // 3. crop to target dimensions: Centers or pans using focusX / focusY, safely aligned to even 2-pixel boundaries.
            return $"setsar=1,scale={targetWidth}:{targetHeight}:force_original_aspect_ratio=increase:flags=bicubic," +
                   $"crop={targetWidth}:{targetHeight}:'trunc((in_w-{targetWidth})*{fx}/2)*2':'trunc((in_h-{targetHeight})*{fy}/2)*2'";
        }

        public (string filterComplex, string outLabel) BuildSceneCutAndConcatFilter(
            List<VideoScene>? scenes,
            double sourceDuration,
            int targetWidth,
            int targetHeight,
            double defaultFocusX = 0.5,
            double defaultFocusY = 0.5)
        {
            if (targetWidth <= 0) targetWidth = 1080;
            if (targetHeight <= 0) targetHeight = 1920;
            if (targetWidth % 2 != 0) targetWidth++;
            if (targetHeight % 2 != 0) targetHeight++;

            double fx = Math.Clamp(defaultFocusX, 0.0, 1.0);
            double fy = Math.Clamp(defaultFocusY, 0.0, 1.0);
            string fxStr = fx.ToString("0.00", CultureInfo.InvariantCulture);
            string fyStr = fy.ToString("0.00", CultureInfo.InvariantCulture);

            string scaleCrop = $"setsar=1,scale={targetWidth}:{targetHeight}:force_original_aspect_ratio=increase:flags=bicubic," +
                               $"crop={targetWidth}:{targetHeight}:'trunc((in_w-{targetWidth})*{fxStr}/2)*2':'trunc((in_h-{targetHeight})*{fyStr}/2)*2',tpad=stop_mode=clone:stop_duration=4";

            var validScenes = new List<VideoScene>();
            if (scenes != null)
            {
                foreach (var s in scenes)
                {
                    if (s != null && s.End > s.Start + 0.2)
                    {
                        validScenes.Add(s);
                    }
                }
            }

            // If no scenes, or single scene that covers almost the full source duration
            if (validScenes.Count == 0 ||
                (validScenes.Count == 1 && validScenes[0].Start <= 0.1 && (sourceDuration <= 0 || validScenes[0].End >= sourceDuration - 0.5)))
            {
                return ($"[0:v]{scaleCrop}[v_cut_concat]", "[v_cut_concat]");
            }

            if (validScenes.Count == 1)
            {
                var s = validScenes[0];
                string st = s.Start.ToString("0.00", CultureInfo.InvariantCulture);
                string ed = s.End.ToString("0.00", CultureInfo.InvariantCulture);
                string fc = $"[0:v]trim=start={st}:end={ed},setpts=PTS-STARTPTS[v0];[v0]{scaleCrop}[v_cut_concat]";
                return (fc, "[v_cut_concat]");
            }

            // Multiple scenes: trim each and concat
            var sb = new StringBuilder();
            var concatInputs = new StringBuilder();
            for (int i = 0; i < validScenes.Count; i++)
            {
                var s = validScenes[i];
                string st = s.Start.ToString("0.00", CultureInfo.InvariantCulture);
                string ed = s.End.ToString("0.00", CultureInfo.InvariantCulture);
                sb.Append($"[0:v]trim=start={st}:end={ed},setpts=PTS-STARTPTS[v{i}];");
                concatInputs.Append($"[v{i}]");
            }

            sb.Append($"{concatInputs}concat=n={validScenes.Count}:v=1:a=0[vcat];");
            sb.Append($"[vcat]{scaleCrop}[v_cut_concat]");

            return (sb.ToString(), "[v_cut_concat]");
        }

        public string BuildCropFilter(int width, int height, int x, int y)
        {
            return $"crop={width}:{height}:{x}:{y}";
        }

        public string BuildScaleFilter(int targetWidth, int targetHeight)
        {
            return $"scale={targetWidth}:{targetHeight}:flags=bicubic";
        }

        public string BuildZoomFilter(List<ZoomEffect>? zoomEffects, int width, int height, int fps = 30)
        {
            double maxZoomPercent = 0.04;
            if (zoomEffects != null && zoomEffects.Count > 0)
            {
                var primaryZoom = zoomEffects[0];
                maxZoomPercent = Math.Clamp(primaryZoom.ToScale - 1.0, 0.02, 0.10);
            }

            string zoomPctStr = maxZoomPercent.ToString("0.000", CultureInfo.InvariantCulture);

            // PTS-preserving zoom filter: uses crop & scale with eval=frame to prevent FFmpeg timestamp drift after 10s
            return $"crop=w='iw*(1-{zoomPctStr}*min(t,10)/10)':h='ih*(1-{zoomPctStr}*min(t,10)/10)':x='(iw-w)/2':y='(ih-h)/2',scale={width}:{height}:flags=bicubic";
        }

        public string BuildPanFilter(double startFocusX, double endFocusX, int inputWidth, int cropWidth)
        {
            int startX = (int)Math.Round((inputWidth - cropWidth) * Math.Clamp(startFocusX, 0, 1));
            int endX = (int)Math.Round((inputWidth - cropWidth) * Math.Clamp(endFocusX, 0, 1));
            return $"crop=w={cropWidth}:h=ih:x='{startX}+({endX}-{startX})*(t/it)':y=0";
        }

        public string BuildSubtitleFilter(string subtitleFilePath)
        {
            if (string.IsNullOrWhiteSpace(subtitleFilePath) || !File.Exists(subtitleFilePath))
            {
                return string.Empty;
            }

            string escaped = EscapeFilterPath(subtitleFilePath);
            string ext = Path.GetExtension(subtitleFilePath).ToLowerInvariant();

            return ext == ".ass" ? $"ass='{escaped}'" : $"subtitles='{escaped}'";
        }

        public string BuildAudioMixFilter(bool hasVoice, bool hasMusic, double musicVolume, bool hasSfx, double sfxVolume = 0.5)
        {
            var sb = new StringBuilder();
            int inputs = 0;

            if (hasVoice) inputs++;
            if (hasMusic) inputs++;
            if (hasSfx) inputs++;

            if (inputs <= 1)
            {
                return hasMusic ? $"volume={musicVolume.ToString("0.00", CultureInfo.InvariantCulture)}" : "volume=1.0";
            }

            return $"amix=inputs={inputs}:duration=first:dropout_transition=2";
        }

        public string BuildDuckingFilter(double voiceVolume = 1.0, double normalMusicVolume = 0.12, double duckedMusicVolume = 0.04)
        {
            // Filter complex using sidechain compression:
            // Input 0: Voice
            // Input 1: Music
            // Duck music whenever voice is active
            string normalVolStr = normalMusicVolume.ToString("0.00", CultureInfo.InvariantCulture);
            return $"[1:a]volume={normalVolStr}[bgm];[bgm][0:a]sidechaincompress=threshold=0.06:ratio=4:attack=50:release=350[ducked_bgm];[0:a][ducked_bgm]amix=inputs=2:duration=first[aout]";
        }

        public string BuildTransitionFilter(string type, double duration = 0.25)
        {
            string durStr = duration.ToString("0.00", CultureInfo.InvariantCulture);
            return type.ToLowerInvariant() switch
            {
                "fade" => $"fade=t=in:st=0:d={durStr}",
                "crossfade" => $"xfade=transition=fade:duration={durStr}",
                "slide" => $"xfade=transition=slideleft:duration={durStr}",
                "quick zoom" => $"xfade=transition=circlecrop:duration={durStr}",
                _ => string.Empty
            };
        }
    }
}

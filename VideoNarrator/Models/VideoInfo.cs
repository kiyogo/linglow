using System;

namespace VideoNarrator.Models
{
    public class VideoInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public double DurationSeconds { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public double Fps { get; set; }
        public string VideoCodec { get; set; } = string.Empty;
        public bool HasAudio { get; set; }
        public string AudioCodec { get; set; } = string.Empty;
        public long FileSize { get; set; }

        public string ResolutionFormatted => Width > 0 && Height > 0 ? $"{Width} x {Height}" : "N/A";
        public string DurationFormatted => DurationSeconds > 0 
            ? TimeSpan.FromSeconds(DurationSeconds).ToString(@"hh\:mm\:ss\.ff") 
            : "00:00:00.00";
        public string FpsFormatted => Fps > 0 ? $"{Fps:0.##} FPS" : "N/A";
        public string FileSizeFormatted
        {
            get
            {
                if (FileSize <= 0) return "0 MB";
                double mb = FileSize / (1024.0 * 1024.0);
                if (mb >= 1024.0)
                {
                    return $"{mb / 1024.0:0.##} GB";
                }
                return $"{mb:0.##} MB";
            }
        }
    }
}

using System;
using System.IO;

namespace VideoNarrator.Models
{
    public class AppSettings
    {
        // Core / Classic Settings
        public string GeminiApiUrl { get; set; } = "https://generativelanguage.googleapis.com";
        public string GeminiApiKey { get; set; } = string.Empty;
        public string GeminiModel { get; set; } = "gemini-3-flash-preview";
        public string TtsProvider { get; set; } = "Edge-TTS"; // "Edge-TTS", "OpenAI", "ElevenLabs"
        public string TtsApiKey { get; set; } = string.Empty;
        public string TtsVoice { get; set; } = "vi-VN-NamMinhNeural (Nam - Tiếng Việt)";
        public double SpeechRate { get; set; } = 1.20;
        public double SpeechPitch { get; set; } = 2.0; // Cao độ / Ngữ điệu (-20% đến +20%), mặc định +2%
        public string TtsEmotionStyle { get; set; } = "🤖 Nhấn nhá cảm xúc tự nhiên (Đề xuất)";
        public string OutputDirectory { get; set; } = string.Empty;
        public bool EnableBackgroundMusic { get; set; } = false;
        public string BackgroundMusicPath { get; set; } = string.Empty;
        public double BackgroundMusicVolume { get; set; } = 0.10;
        public string AudioHandlingMode { get; set; } = "ReplaceOriginalAudio"; // "ReplaceOriginalAudio", "KeepOriginalAudio", "MuteOriginalAudio"

        // Auto Editing Engine Settings
        public bool IsAutoEditEnabled { get; set; } = true;
        public string AutoEditStylePreset { get; set; } = "TikTok Shorts"; // "Documentary", "News", "Educational", "Military/Technology", "TikTok Shorts"
        public string OutputResolution { get; set; } = "1080x1920 (9:16 Dọc TikTok/Shorts)";
        public string SubtitleStylePreset { get; set; } = "TikTok"; // "Clean", "Documentary", "News", "Educational", "Military", "TikTok"
        public string DurationMode { get; set; } = "🤖 Tự Động Thông Minh (Thuật toán tự quyết định)";
        public string NarrationTone { get; set; } = "🤖 Tự Động Phân Tích (Theo nội dung video)";
        public string AiTrainingInstruction { get; set; } = "🔥 Phong cách Video Nhét Chữ: Lối kể chuyện cực kỳ cuốn hút, giữ chân người xem từ giây đầu đến cuối, dùng từ đắt giá tự nhiên không giới hạn, không dùng từ xàm ít người dùng, nội dung kịch tính lôi cuốn.";
        
        public bool EnableSceneDetection { get; set; } = true;
        public bool EnableAutoCut { get; set; } = true;
        public bool EnableAutoZoom { get; set; } = true;
        public bool EnableAutoPan { get; set; } = true;
        public bool EnableAutoSubtitle { get; set; } = true;
        public bool EnableKeywordHighlight { get; set; } = true;
        public bool EnableAutoTransition { get; set; } = true;
        public bool EnableAutoBRoll { get; set; } = false;
        public string BRollFolderPath { get; set; } = string.Empty;
        public bool EnableMusicDucking { get; set; } = true;
        public string MusicFolderPath { get; set; } = string.Empty;
        public bool EnableSoundEffects { get; set; } = false;
        public string SfxFolderPath { get; set; } = string.Empty;

        public static string GetDefaultOutputDirectory()
        {
            string videosPath = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            if (string.IsNullOrWhiteSpace(videosPath))
            {
                videosPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos");
            }
            string defaultDir = Path.Combine(videosPath, "VideoNarrator");
            return defaultDir;
        }
    }
}

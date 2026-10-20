namespace VideoNarrator.Models
{
    public class TtsSettings
    {
        public string Provider { get; set; } = "Edge-TTS"; // "Edge-TTS", "OpenAI", "ElevenLabs"
        public string ApiKey { get; set; } = string.Empty;
        public string Voice { get; set; } = "vi-VN-NamMinhNeural";
        public double Speed { get; set; } = 1.05;
        public double Pitch { get; set; } = 2.0; // Cao độ / Ngữ điệu (-20% đến +20%), mặc định +2%
        public string EmotionStyle { get; set; } = "Dynamic"; // "Dynamic", "Cheerful", "Calm", "FastPaced", "Standard"
        public string ElevenLabsModelId { get; set; } = "eleven_multilingual_v2";
        public string OpenAIModel { get; set; } = "tts-1";
    }
}

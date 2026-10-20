using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using VideoNarrator.Models;

namespace VideoNarrator.Services
{
    public interface ISubtitleService
    {
        string GenerateAssSubtitleFile(
            List<SubtitleSegment> segments,
            string presetName,
            string outputPath,
            int videoWidth = 1080,
            int videoHeight = 1920,
            bool enableKeywordHighlight = true);

        List<SubtitleSegment> BuildSubtitlesFromWordTimestamps(
            List<WordTimestamp> words,
            double totalDuration);

        List<SubtitleSegment> SplitNarrationToSubtitles(
            string narrationText,
            double totalAudioDuration);
    }

    public class SubtitleService : ISubtitleService
    {
        private readonly IFileService _fileService;

        public SubtitleService(IFileService fileService)
        {
            _fileService = fileService;
        }

        public List<SubtitleSegment> BuildSubtitlesFromWordTimestamps(List<WordTimestamp> words, double totalDuration)
        {
            var result = new List<SubtitleSegment>();
            if (words == null || words.Count == 0) return result;

            int i = 0;
            while (i < words.Count)
            {
                int chunkCount = 0;
                int maxChunk = 4; // 3-4 words is ideal for vertical TikTok without wrapping

                // Look ahead to check punctuation
                for (int j = 0; j < maxChunk && (i + j) < words.Count; j++)
                {
                    chunkCount++;
                    string w = words[i + j].Word.Trim();
                    if (w.EndsWith(".") || w.EndsWith("!") || w.EndsWith("?") || w.EndsWith(";") || w.EndsWith(":") || w.EndsWith(","))
                    {
                        break;
                    }
                }

                var chunkWords = words.GetRange(i, chunkCount);
                double segStart = Math.Round(chunkWords[0].Start, 2);
                double lastWordEnd = chunkWords[^1].End;

                double nextStart = (i + chunkCount < words.Count)
                    ? words[i + chunkCount].Start
                    : (lastWordEnd + 0.35);

                // Giới hạn thời gian hiển thị: kết thúc gọn gàng sau từ cuối (+0.35s), không kéo dài vô tận khi khoảng nghỉ giữa các câu
                double targetEnd = lastWordEnd + 0.35;
                if (targetEnd > nextStart) targetEnd = nextStart;
                if (totalDuration > 0 && targetEnd > totalDuration) targetEnd = totalDuration;
                double segEnd = Math.Round(Math.Max(targetEnd, lastWordEnd), 2);
                if (segEnd <= segStart) segEnd = Math.Round(segStart + 0.35, 2);

                string phraseText = string.Join(" ", chunkWords.Select(w => w.Word.Trim()));

                // Highlight longest word
                var highlights = new List<string>();
                string longest = "";
                foreach (var cw in chunkWords)
                {
                    string cleanW = Regex.Replace(cw.Word, @"[^\w]", "");
                    if (cleanW.Length > longest.Length && cleanW.Length >= 4)
                    {
                        longest = cleanW;
                    }
                }
                if (!string.IsNullOrEmpty(longest)) highlights.Add(longest);

                result.Add(new SubtitleSegment
                {
                    Start = segStart,
                    End = segEnd,
                    Text = phraseText,
                    HighlightWords = highlights,
                    WordTimestamps = chunkWords
                });

                i += chunkCount;
            }

            return result;
        }

        public List<SubtitleSegment> SplitNarrationToSubtitles(string narrationText, double totalAudioDuration)
        {
            var result = new List<SubtitleSegment>();
            if (string.IsNullOrWhiteSpace(narrationText)) return result;

            string clean = TtsService.StripTimestampsAndFormatText(narrationText);
            var sentences = Regex.Split(clean, @"(?<=[.!?\n])\s+");

            var rawPhrases = new List<string>();
            foreach (var s in sentences)
            {
                string st = s.Trim();
                if (string.IsNullOrEmpty(st)) continue;

                // Split into punchy small chunks (3-4 words) for vertical TikTok / Shorts
                var words = st.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length <= 4)
                {
                    rawPhrases.Add(st);
                }
                else
                {
                    int i = 0;
                    while (i < words.Length)
                    {
                        int count = Math.Min(4, words.Length - i);
                        rawPhrases.Add(string.Join(" ", words, i, count));
                        i += count;
                    }
                }
            }

            if (rawPhrases.Count == 0) return result;

            // Calculate duration proportional to word count
            int totalWords = 0;
            foreach (var p in rawPhrases)
            {
                totalWords += p.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            }
            if (totalWords == 0) totalWords = 1;

            double timePerWord = totalAudioDuration / totalWords;
            double currentStart = 0.0;

            for (int i = 0; i < rawPhrases.Count; i++)
            {
                string phrase = rawPhrases[i];
                var wordsInPhrase = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                double segDur = Math.Max(0.8, wordsInPhrase.Length * timePerWord);
                double currentEnd = Math.Min(totalAudioDuration, currentStart + segDur);

                // Populate per-word timestamps
                var segWordTimings = new List<WordTimestamp>();
                double perWordDur = segDur / Math.Max(1, wordsInPhrase.Length);
                for (int wIdx = 0; wIdx < wordsInPhrase.Length; wIdx++)
                {
                    double wS = currentStart + (wIdx * perWordDur);
                    double wE = (wIdx == wordsInPhrase.Length - 1) ? currentEnd : (wS + perWordDur);
                    segWordTimings.Add(new WordTimestamp
                    {
                        Word = wordsInPhrase[wIdx],
                        Start = Math.Round(wS, 2),
                        End = Math.Round(wE, 2)
                    });
                }

                // Auto extract highlight candidate (longest word)
                var highlights = new List<string>();
                string longestWord = "";
                foreach (var w in wordsInPhrase)
                {
                    string cleanWord = Regex.Replace(w, @"[^\w]", "");
                    if (cleanWord.Length > longestWord.Length && cleanWord.Length >= 4)
                    {
                        longestWord = cleanWord;
                    }
                }
                if (!string.IsNullOrEmpty(longestWord))
                {
                    highlights.Add(longestWord);
                }

                result.Add(new SubtitleSegment
                {
                    Start = Math.Round(currentStart, 2),
                    End = Math.Round(currentEnd, 2),
                    Text = phrase,
                    HighlightWords = highlights,
                    WordTimestamps = segWordTimings
                });

                currentStart = currentEnd;
            }

            return result;
        }

        public string GenerateAssSubtitleFile(
            List<SubtitleSegment> segments,
            string presetName,
            string outputPath,
            int videoWidth = 1080,
            int videoHeight = 1920,
            bool enableKeywordHighlight = true)
        {
            var sb = new StringBuilder();

            string presetLower = (presetName ?? "tiktok").ToLowerInvariant();
            bool isProgressiveReveal = presetLower.Contains("từng từ");
            bool isHormozi = presetLower.Contains("hormozi") || presetLower.Contains("1-2 từ");
            bool isKaraoke = !isProgressiveReveal && !isHormozi &&
                (presetLower.Contains("tiktok") || presetLower.Contains("karaoke") || presetLower.Contains("sáng chữ"));

            // Scale font size proportionally if resolution is different
            double scaleFactor = videoHeight > 0 ? (double)videoHeight / 1920.0 : 1.0;
            bool isLandscape = videoWidth > videoHeight;

            // Default: Arial Bold (Universal native Windows font with 100% flawless Vietnamese diacritics)
            string fontName = "Arial";
            int baseFontSize = isLandscape ? 48 : 76;
            string primaryColor = "&H00FFFFFF"; // Crisp White
            string outlineColor = "&H00000000"; // Pitch Black
            string backColor = "&H80000000";    // Translucent black shadow
            int outline = isLandscape ? 5 : 7;
            int shadow = 3;
            int marginV = isLandscape
                ? (int)Math.Round(90 * (videoHeight / 1080.0))
                : (int)Math.Round(450 * scaleFactor);
            string highlightColor = "&H0000E6FF"; // Vibrant TikTok Gold-Yellow in ASS BGR (&H00BBGGRR)

            if (isHormozi)
            {
                fontName = "Arial";
                baseFontSize = isLandscape ? 62 : 88;
                outline = isLandscape ? 6 : 8;
                shadow = 4;
                marginV = isLandscape ? (int)Math.Round(120 * (videoHeight / 1080.0)) : (int)Math.Round(500 * scaleFactor);
                highlightColor = "&H0000E6FF";
            }
            else if (presetLower.Contains("documentary"))
            {
                fontName = "Segoe UI Semibold";
                baseFontSize = 52;
                primaryColor = "&H00F0F0F0";
                outlineColor = "&H00101010";
                outline = 3;
                shadow = 2;
                marginV = (int)Math.Round(300 * scaleFactor);
                highlightColor = "&H0050D0FF";
                isKaraoke = false;
                isProgressiveReveal = false;
            }
            else if (presetLower.Contains("news"))
            {
                fontName = "Arial";
                baseFontSize = 54;
                primaryColor = "&H00FFFFFF";
                outlineColor = "&H00000000";
                outline = 4;
                shadow = 2;
                marginV = (int)Math.Round(320 * scaleFactor);
                highlightColor = "&H0010E0FF";
                isKaraoke = false;
                isProgressiveReveal = false;
            }
            else if (presetLower.Contains("clean"))
            {
                fontName = "Segoe UI Semibold";
                baseFontSize = 50;
                primaryColor = "&H00FFFFFF";
                outlineColor = "&H00000000";
                outline = 3;
                shadow = 1;
                marginV = (int)Math.Round(300 * scaleFactor);
                highlightColor = "&H0000E0FF";
                isKaraoke = false;
                isProgressiveReveal = false;
            }

            int finalFontSize = Math.Max(24, (int)Math.Round(baseFontSize * scaleFactor));
            outline = Math.Max(2, (int)Math.Round(outline * scaleFactor));
            shadow = Math.Max(1, (int)Math.Round(shadow * scaleFactor));

            // ASS Header
            sb.AppendLine("[Script Info]");
            sb.AppendLine("ScriptType: v4.00+");
            sb.AppendLine($"PlayResX: {videoWidth}");
            sb.AppendLine($"PlayResY: {videoHeight}");
            sb.AppendLine("ScaledBorderAndShadow: yes");
            sb.AppendLine();

            // V4+ Styles - Bold = -1 enforces bold weight across all GDI/DirectWrite font families
            sb.AppendLine("[V4+ Styles]");
            sb.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
            sb.AppendLine($"Style: Default,{fontName},{finalFontSize},{primaryColor},&H000000FF,{outlineColor},{backColor},-1,0,0,0,100,100,0,0,1,{outline},{shadow},2,40,40,{marginV},1");
            sb.AppendLine();

            // Events
            sb.AppendLine("[Events]");
            sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

            var viCulture = new CultureInfo("vi-VN");

            foreach (var seg in segments)
            {
                if (string.IsNullOrWhiteSpace(seg.Text)) continue;

                // Extract word list with individual timestamps
                var wordList = new List<(string word, double start, double end)>();
                if (seg.WordTimestamps != null && seg.WordTimestamps.Count > 0)
                {
                    foreach (var wt in seg.WordTimestamps)
                    {
                        string w = wt.Word.Trim();
                        if (!string.IsNullOrEmpty(w))
                        {
                            wordList.Add((w, wt.Start, wt.End));
                        }
                    }
                }

                if (wordList.Count == 0)
                {
                    var rawWords = seg.Text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    double sDur = Math.Max(0.5, seg.End - seg.Start);
                    double perWord = sDur / Math.Max(1, rawWords.Length);
                    for (int k = 0; k < rawWords.Length; k++)
                    {
                        double ws = seg.Start + (k * perWord);
                        double we = (k == rawWords.Length - 1) ? seg.End : (ws + perWord);
                        wordList.Add((rawWords[k], ws, we));
                    }
                }

                if (wordList.Count == 0) continue;

                int count = wordList.Count;

                if (isKaraoke && enableKeywordHighlight)
                {
                    // STYLE 1: TIKTOK KARAOKE (CHUẨN CAPCUT - KHÔNG GIẬT CHỮ)
                    // Dòng chữ giữ nguyên vị trí, từ đang nói sáng đèn vàng neon kèm nảy pop nhẹ
                    for (int k = 0; k < count; k++)
                    {
                        double wStart = wordList[k].start;
                        double wEnd = (k == count - 1)
                            ? Math.Min(seg.End, wordList[k].end + 0.30)
                            : Math.Min(seg.End, Math.Max(wordList[k].end, wordList[k + 1].start));

                        if (wEnd <= wStart) wEnd = wStart + 0.20;

                        string startStr = FormatAssTime(wStart);
                        string endStr = FormatAssTime(wEnd);

                        var lineSb = new StringBuilder();
                        for (int j = 0; j < count; j++)
                        {
                            string wordFormatted = wordList[j].word.ToUpper(viCulture);
                            if (j == k)
                            {
                                lineSb.Append($"{{\\c{highlightColor}\\fscx116\\fscy116\\t(0,75,\\fscx100\\fscy100)}}{wordFormatted}{{\\r}} ");
                            }
                            else
                            {
                                lineSb.Append($"{wordFormatted} ");
                            }
                        }

                        sb.AppendLine($"Dialogue: 0,{startStr},{endStr},Default,,0,0,0,,{lineSb.ToString().Trim()}");
                    }
                }
                else if (isProgressiveReveal && enableKeywordHighlight)
                {
                    // STYLE 2: TIKTOK HIỆN TỪNG TỪ (KHÔNG GIẬT CHỮ)
                    // Các từ sắp tới được ẩn bằng alpha 100% (\alpha&HFF&) để giữ đúng độ rộng dòng, tránh giật màn hình
                    for (int k = 0; k < count; k++)
                    {
                        double wStart = wordList[k].start;
                        double wEnd = (k == count - 1)
                            ? Math.Min(seg.End, wordList[k].end + 0.30)
                            : Math.Min(seg.End, Math.Max(wordList[k].end, wordList[k + 1].start));

                        if (wEnd <= wStart) wEnd = wStart + 0.20;

                        string startStr = FormatAssTime(wStart);
                        string endStr = FormatAssTime(wEnd);

                        var lineSb = new StringBuilder();
                        for (int j = 0; j < count; j++)
                        {
                            string wordFormatted = wordList[j].word.ToUpper(viCulture);
                            if (j < k)
                            {
                                lineSb.Append($"{wordFormatted} ");
                            }
                            else if (j == k)
                            {
                                lineSb.Append($"{{\\c{highlightColor}\\fscx116\\fscy116\\t(0,75,\\fscx100\\fscy100)}}{wordFormatted}{{\\r}} ");
                            }
                            else
                            {
                                // Invisible alpha (\alpha&HFF&) holds layout position seamlessly without jitter
                                lineSb.Append($"{{\\alpha&HFF&}}{wordFormatted}{{\\r}} ");
                            }
                        }

                        sb.AppendLine($"Dialogue: 0,{startStr},{endStr},Default,,0,0,0,,{lineSb.ToString().Trim()}");
                    }
                }
                else if (isHormozi)
                {
                    // STYLE 3: ALEX HORMOZI (1-2 TỪ TO NỔI BẬT GIỮA MÀN HÌNH)
                    for (int k = 0; k < count; k++)
                    {
                        double wStart = wordList[k].start;
                        double wEnd = (k == count - 1)
                            ? Math.Min(seg.End, wordList[k].end + 0.30)
                            : Math.Min(seg.End, Math.Max(wordList[k].end, wordList[k + 1].start));

                        if (wEnd <= wStart) wEnd = wStart + 0.20;

                        string startStr = FormatAssTime(wStart);
                        string endStr = FormatAssTime(wEnd);
                        string wordFormatted = wordList[k].word.ToUpper(viCulture);

                        sb.AppendLine($"Dialogue: 0,{startStr},{endStr},Default,,0,0,0,,{{\\c{highlightColor}\\fscx110\\fscy110}}{wordFormatted}{{\\r}}");
                    }
                }
                else
                {
                    // Standard Line Mode
                    string startStr = FormatAssTime(seg.Start);
                    string endStr = FormatAssTime(seg.End);
                    string formattedText = seg.Text.Trim();

                    if (enableKeywordHighlight && seg.HighlightWords != null && seg.HighlightWords.Count > 0)
                    {
                        foreach (var hw in seg.HighlightWords)
                        {
                            if (string.IsNullOrWhiteSpace(hw)) continue;
                            string pattern = $@"\b({Regex.Escape(hw)})\b";
                            formattedText = Regex.Replace(formattedText, pattern, $"{{\\c{highlightColor}\\b1}}$1{{\\c{primaryColor}\\b0}}", RegexOptions.IgnoreCase);
                        }
                    }

                    sb.AppendLine($"Dialogue: 0,{startStr},{endStr},Default,,0,0,0,,{formattedText}");
                }
            }

            File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
            _fileService.Log($"Tạo phụ đề ASS thành công: {outputPath} ({segments.Count} phân đoạn, preset '{presetName}')");
            return outputPath;
        }

        private static string FormatAssTime(double seconds)
        {
            TimeSpan ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
            int hours = (int)ts.TotalHours;
            int minutes = ts.Minutes;
            int secs = ts.Seconds;
            int centisecs = ts.Milliseconds / 10;
            return $"{hours}:{minutes:D2}:{secs:D2}.{centisecs:D2}";
        }
    }
}

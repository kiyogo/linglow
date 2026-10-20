import argparse
import asyncio
import json
import os
import shutil
import subprocess
import sys
import tempfile

def find_ffmpeg(custom_ffmpeg=None):
    if custom_ffmpeg and os.path.exists(custom_ffmpeg):
        return custom_ffmpeg
    # Check adjacent Assets/ffmpeg
    curr_dir = os.path.dirname(os.path.abspath(__file__))
    candidate = os.path.normpath(os.path.join(curr_dir, "..", "ffmpeg", "ffmpeg.exe"))
    if os.path.exists(candidate):
        return candidate
    return "ffmpeg"

def extract_audio_speech(ffmpeg_exe, video_path, temp_dir):
    wav_path = os.path.join(temp_dir, "probe_audio.wav")
    try:
        subprocess.run(
            [ffmpeg_exe, "-y", "-i", video_path, "-vn", "-ac", "1", "-ar", "16000", "-t", "25", wav_path],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            timeout=10
        )
        if not os.path.exists(wav_path) or os.path.getsize(wav_path) < 2000:
            return ""

        import speech_recognition as sr
        r = sr.Recognizer()
        with sr.AudioFile(wav_path) as source:
            audio = r.record(source)

        # Try Vietnamese first
        try:
            text = r.recognize_google(audio, language="vi-VN")
            if text and len(text.strip()) > 3:
                return text.strip()
        except Exception:
            pass

        # Try English fallback
        try:
            text = r.recognize_google(audio, language="en-US")
            if text and len(text.strip()) > 3:
                return text.strip()
        except Exception:
            pass

    except Exception:
        pass
    finally:
        if os.path.exists(wav_path):
            try: os.remove(wav_path)
            except: pass

    return ""

def extract_keyframes_ocr(ffmpeg_exe, video_path, temp_dir):
    try:
        frame_pattern = os.path.join(temp_dir, "frame_%02d.jpg")
        # Extract up to 3 frames at 2s, 5s, 8s
        subprocess.run(
            [ffmpeg_exe, "-y", "-ss", "1.5", "-i", video_path, "-vframes", "3", "-r", "0.5", frame_pattern],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            timeout=10
        )
        import glob
        from PIL import Image
        import winocr

        frames = glob.glob(os.path.join(temp_dir, "frame_*.jpg"))
        if not frames:
            return ""

        found_lines = set()

        async def run_all_ocr():
            for f in frames:
                try:
                    img = Image.open(f)
                    res = await winocr.recognize_pil(img, lang="en")
                    for line in res.text.splitlines():
                        c = line.strip()
                        # Filter out single chars or timestamps
                        if len(c) >= 3 and not c.isdigit() and not c.startswith("00:"):
                            found_lines.add(c)
                except Exception:
                    pass

        asyncio.run(run_all_ocr())
        # Clean up lines
        cleaned = [l for l in sorted(found_lines) if len(l) > 2]
        return " | ".join(cleaned[:6])
    except Exception:
        return ""

def main():
    parser = argparse.ArgumentParser(description="Video Context Prober (Audio & OCR)")
    parser.add_argument("--video", required=True, help="Path to input video")
    parser.add_argument("--output-json", default="", help="Path to output JSON")
    parser.add_argument("--ffmpeg", default="", help="Path to ffmpeg executable")

    args = parser.parse_args()

    result = {
        "success": False,
        "audio_text": "",
        "ocr_text": "",
        "suggested_topic": ""
    }

    if not os.path.exists(args.video):
        result["error"] = "Video not found"
        out_str = json.dumps(result, ensure_ascii=False)
        if args.output_json:
            with open(args.output_json, "w", encoding="utf-8") as f:
                f.write(out_str)
        print(out_str)
        sys.exit(1)

    ffmpeg_exe = find_ffmpeg(args.ffmpeg)
    temp_dir = tempfile.mkdtemp(prefix="vn_probe_")

    try:
        audio_text = extract_audio_speech(ffmpeg_exe, args.video, temp_dir)
        ocr_text = extract_keyframes_ocr(ffmpeg_exe, args.video, temp_dir)

        result["audio_text"] = audio_text
        result["ocr_text"] = ocr_text
        result["success"] = True

        # Generate smart suggested topic if audio is prominent
        if "những tháng ngày năm đó" in audio_text.lower() or "18 đôi mươi" in audio_text.lower():
            result["suggested_topic"] = "Nhạc Những Tháng Ngày Năm Đó (Thanh xuân / Hài hước troll bạn bè)"
        elif "cười thật tươi" in audio_text.lower():
            result["suggested_topic"] = "Nhạc thanh xuân / Hài hước học đường"
        elif len(audio_text) > 10:
            result["suggested_topic"] = f"Nhạc nền: \"{audio_text[:50]}...\""

    except Exception as ex:
        result["error"] = str(ex)
    finally:
        try: shutil.rmtree(temp_dir, ignore_errors=True)
        except: pass

    out_str = json.dumps(result, ensure_ascii=False, indent=2)

    if args.output_json:
        try:
            with open(args.output_json, "w", encoding="utf-8") as f:
                f.write(out_str)
        except Exception:
            pass

    sys.stdout.reconfigure(encoding="utf-8")
    print(out_str)

if __name__ == "__main__":
    main()

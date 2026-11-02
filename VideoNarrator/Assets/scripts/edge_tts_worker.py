import argparse
import asyncio
import json
import os
import sys
import re

try:
    import edge_tts
except ImportError:
    sys.stderr.write("ERROR: edge_tts package not installed. Run 'pip install edge-tts'.\n")
    sys.exit(1)

def clean_text(text: str) -> str:
    if not text:
        return ""
    # Strip BOM, zero-width spaces, and control characters
    text = text.replace('\ufeff', '').replace('\u200b', '').replace('\u200c', '').replace('\u200d', '')
    # Strip lone surrogate characters if any
    text = re.sub(r'[\ud800-\udfff]', '', text)
    return text.strip()

async def run_tts(text: str, voice: str, rate: str, pitch: str, media_path: str, words_path: str):
    text = clean_text(text)
    if not text:
        sys.stderr.write("ERROR: Input text is empty after cleaning.\n")
        sys.exit(3)

    os.makedirs(os.path.dirname(os.path.abspath(media_path)), exist_ok=True)
    if words_path:
        os.makedirs(os.path.dirname(os.path.abspath(words_path)), exist_ok=True)

    max_retries = 3
    last_err = None

    for attempt in range(1, max_retries + 1):
        # On attempt 1: use provided pitch & rate
        # On attempt 2+: relax pitch to +0Hz (most common cause of Microsoft edge-tts rejection)
        current_pitch = pitch if attempt == 1 else "+0Hz"
        current_rate = rate if attempt < 3 else "+0%"

        # Strategy A: Try with WordBoundary
        try:
            comm = edge_tts.Communicate(text=text, voice=voice, rate=current_rate, pitch=current_pitch, boundary="WordBoundary")
            word_timestamps = []
            audio_data = bytearray()

            async for chunk in comm.stream():
                if chunk["type"] == "audio":
                    audio_data.extend(chunk["data"])
                elif chunk["type"] == "WordBoundary":
                    w = chunk.get("text", "").strip()
                    if w:
                        offset = chunk.get("offset", 0)
                        duration = chunk.get("duration", 0)
                        word_timestamps.append({
                            "word": w,
                            "start": round(offset / 10000000.0, 3),
                            "end": round((offset + duration) / 10000000.0, 3)
                        })

            if len(audio_data) > 0:
                with open(media_path, "wb") as f_audio:
                    f_audio.write(audio_data)
                if words_path:
                    with open(words_path, "w", encoding="utf-8") as f_words:
                        json.dump(word_timestamps, f_words, ensure_ascii=False, indent=2)
                return  # Succeeded!
        except Exception as e_stream:
            last_err = e_stream

        # Strategy B: Fallback without WordBoundary (standard synthesis)
        try:
            comm_plain = edge_tts.Communicate(text=text, voice=voice, rate=current_rate, pitch=current_pitch)
            audio_data = bytearray()

            async for chunk in comm_plain.stream():
                if chunk["type"] == "audio":
                    audio_data.extend(chunk["data"])

            if len(audio_data) > 0:
                with open(media_path, "wb") as f_audio:
                    f_audio.write(audio_data)
                if words_path:
                    with open(words_path, "w", encoding="utf-8") as f_words:
                        json.dump([], f_words, ensure_ascii=False, indent=2)
                return  # Succeeded!
        except Exception as e_plain:
            last_err = e_plain

        if attempt < max_retries:
            await asyncio.sleep(0.8 * attempt)

    sys.stderr.write(f"ERROR: Synthesis failed after {max_retries} attempts: {last_err}\n")
    sys.exit(4)

def main():
    parser = argparse.ArgumentParser(description="Edge-TTS Audio & Word-Boundary Generator")
    parser.add_argument("--text-file", required=True, help="Path to input text file (UTF-8)")
    parser.add_argument("--voice", default="vi-VN-NamMinhNeural", help="TTS voice name")
    parser.add_argument("--rate", default="+20%", help="Speech rate (e.g., +20%%)")
    parser.add_argument("--pitch", default="+0Hz", help="Speech pitch (e.g., +0Hz)")
    parser.add_argument("--write-media", required=True, help="Path to output audio mp3")
    parser.add_argument("--write-words", default="", help="Path to output word-boundary json")
    
    args = parser.parse_args()
    
    if not os.path.exists(args.text_file):
        sys.stderr.write(f"ERROR: Text file not found: {args.text_file}\n")
        sys.exit(2)
        
    with open(args.text_file, "r", encoding="utf-8-sig") as f:
        text = f.read().strip()
        
    if not text:
        sys.stderr.write("ERROR: Input text is empty.\n")
        sys.exit(3)
        
    voice_clean = args.voice.split(" ")[0].strip() if " " in args.voice else args.voice.strip()
    asyncio.run(run_tts(text, voice_clean, args.rate, args.pitch, args.write_media, args.write_words))

if __name__ == "__main__":
    main()

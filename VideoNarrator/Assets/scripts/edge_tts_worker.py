import argparse
import asyncio
import json
import os
import sys

try:
    import edge_tts
except ImportError:
    sys.stderr.write("ERROR: edge_tts package not installed. Run 'pip install edge-tts'.\n")
    sys.exit(1)

async def run_tts(text: str, voice: str, rate: str, pitch: str, media_path: str, words_path: str):
    comm = edge_tts.Communicate(text=text, voice=voice, rate=rate, pitch=pitch, boundary="WordBoundary")
    word_timestamps = []
    
    os.makedirs(os.path.dirname(os.path.abspath(media_path)), exist_ok=True)
    if words_path:
        os.makedirs(os.path.dirname(os.path.abspath(words_path)), exist_ok=True)
        
    with open(media_path, "wb") as f_audio:
        async for chunk in comm.stream():
            if chunk["type"] == "audio":
                f_audio.write(chunk["data"])
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
                    
    if words_path:
        with open(words_path, "w", encoding="utf-8") as f_words:
            json.dump(word_timestamps, f_words, ensure_ascii=False, indent=2)

def main():
    parser = argparse.ArgumentParser(description="Edge-TTS Audio & Word-Boundary Generator")
    parser.add_argument("--text-file", required=True, help="Path to input text file (UTF-8)")
    parser.add_argument("--voice", default="vi-VN-NamMinhNeural", help="TTS voice name")
    parser.add_argument("--rate", default="+20%", help="Speech rate (e.g., +20%%)")
    parser.add_argument("--pitch", default="+2Hz", help="Speech pitch (e.g., +2Hz)")
    parser.add_argument("--write-media", required=True, help="Path to output audio mp3")
    parser.add_argument("--write-words", default="", help="Path to output word-boundary json")
    
    args = parser.parse_args()
    
    if not os.path.exists(args.text_file):
        sys.stderr.write(f"ERROR: Text file not found: {args.text_file}\n")
        sys.exit(2)
        
    with open(args.text_file, "r", encoding="utf-8") as f:
        text = f.read().strip()
        
    if not text:
        sys.stderr.write("ERROR: Input text is empty.\n")
        sys.exit(3)
        
    voice_clean = args.voice.split(" ")[0].strip() if " " in args.voice else args.voice.strip()
    try:
        asyncio.run(run_tts(text, voice_clean, args.rate, args.pitch, args.write_media, args.write_words))
    except Exception as e:
        sys.stderr.write(f"ERROR: Synthesis failed: {e}\n")
        sys.exit(4)

if __name__ == "__main__":
    main()

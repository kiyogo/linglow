import sys
import json
import os
import yt_dlp

def download_video(url, output_dir):
    try:
        os.makedirs(output_dir, exist_ok=True)
        out_tmpl = os.path.join(output_dir, "%(title).50B_%(id)s.%(ext)s")
        
        ydl_opts = {
            'outtmpl': out_tmpl,
            'format': 'bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best',
            'merge_output_format': 'mp4',
            'quiet': True,
            'no_warnings': True,
            'noplaylist': True,
            'overwrites': True,
            'postprocessors': [{
                'key': 'FFmpegVideoConvertor',
                'preferedformat': 'mp4',
            }]
        }
        
        with yt_dlp.YoutubeDL(ydl_opts) as ydl:
            info = ydl.extract_info(url, download=True)
            filename = ydl.prepare_filename(info)
            base, _ = os.path.splitext(filename)
            mp4_file = base + '.mp4'
            final_path = mp4_file if os.path.exists(mp4_file) else filename

            result = {
                "success": True,
                "filePath": os.path.abspath(final_path),
                "title": info.get("title", ""),
                "duration": info.get("duration", 0),
                "uploader": info.get("uploader", "")
            }
            print(json.dumps(result, ensure_ascii=False))
    except Exception as e:
        print(json.dumps({"success": False, "error": str(e)}, ensure_ascii=False))

if __name__ == "__main__":
    if len(sys.argv) > 2:
        download_video(sys.argv[1], sys.argv[2])
    else:
        print(json.dumps({"success": False, "error": "Missing URL or output dir"}))

import sys
import json
import os
import urllib.parse
import yt_dlp

def search_videos(query, max_results=8):
    try:
        if hasattr(sys.stdout, 'reconfigure'):
            sys.stdout.reconfigure(encoding='utf-8')
        clean_query = (query or "").strip()
        if not clean_query:
            clean_query = "oddly satisfying"

        ydl_opts = {
            'extract_flat': True,
            'quiet': True,
            'no_warnings': True,
            'noplaylist': True,
        }

        entries = []
        with yt_dlp.YoutubeDL(ydl_opts) as ydl:
            # 1. First priority: Use YouTube duration filter under 4 minutes (sp=EgIYAQ%253D%253D)
            try:
                encoded = urllib.parse.quote_plus(clean_query)
                filter_url = f"https://www.youtube.com/results?search_query={encoded}&sp=EgIYAQ%253D%253D"
                info = ydl.extract_info(filter_url, download=False)
                if info and info.get('entries'):
                    entries = [e for e in info.get('entries', []) if e]
            except Exception:
                entries = []

            # 2. Secondary fallback: ytsearch with short keyword
            if len(entries) < max_results:
                try:
                    search_count = max(max_results * 3, 20)
                    info2 = ydl.extract_info(f"ytsearch{search_count}:{clean_query} shorts", download=False)
                    if info2 and info2.get('entries'):
                        entries.extend([e for e in info2.get('entries', []) if e])
                except Exception:
                    pass

        # Parse & deduplicate candidates
        seen_ids = set()
        short_candidates = []
        other_candidates = []

        for e in entries:
            if not e:
                continue

            vid_id = e.get('id')
            if not vid_id or vid_id in seen_ids:
                continue

            # Skip channel / playlist entries
            if vid_id.startswith('UC') or e.get('ie_key') == 'YoutubeTab' or 'channel/' in e.get('url', ''):
                continue

            dur = e.get('duration')
            # Skip videos longer than 4 minutes or under 5s
            if dur and (dur < 5 or dur > 240):
                continue

            seen_ids.add(vid_id)

            url = e.get('url')
            if not url or not url.startswith('http'):
                url = f"https://www.youtube.com/watch?v={vid_id}"

            thumb = e.get('thumbnail')
            if not thumb and e.get('thumbnails'):
                thumb = e['thumbnails'][-1].get('url')

            item = {
                "id": vid_id,
                "title": (e.get("title") or "Video").strip(),
                "url": url,
                "duration": dur or 0,
                "thumbnail": thumb or f"https://i.ytimg.com/vi/{vid_id}/hqdefault.jpg",
                "uploader": (e.get("uploader") or e.get("channel") or "").strip(),
                "viewCount": e.get("view_count") or 0,
                "platform": "YouTube"
            }

            if dur and 10 <= dur <= 90:
                short_candidates.append(item)
            else:
                other_candidates.append(item)

        results = (short_candidates + other_candidates)[:max_results]
        print(json.dumps({"success": True, "videos": results}, ensure_ascii=False))
    except Exception as e:
        print(json.dumps({"success": False, "error": str(e), "videos": []}, ensure_ascii=False))

def download_video(url, output_dir):
    try:
        if hasattr(sys.stdout, 'reconfigure'):
            sys.stdout.reconfigure(encoding='utf-8')
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
    if len(sys.argv) > 1 and sys.argv[1].lower() == "search":
        q = sys.argv[2] if len(sys.argv) > 2 else "oddly satisfying shorts"
        limit = int(sys.argv[3]) if len(sys.argv) > 3 and sys.argv[3].isdigit() else 8
        search_videos(q, limit)
    elif len(sys.argv) > 2:
        download_video(sys.argv[1], sys.argv[2])
    else:
        print(json.dumps({"success": False, "error": "Missing parameters"}))

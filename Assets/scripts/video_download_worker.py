import sys
import json
import os
import urllib.parse
import re
import yt_dlp

# Danh sách từ khóa bị cấm: Tuyệt đối loại bỏ video tổng hợp, top 10, review, thuyết minh sẵn
BLACKLIST_KEYWORDS = [
    # Compilations & Rankings
    'compilation', 'tổng hợp', 'top 10', 'top 5', 'top 3', 'top ', 'bxh', 'ranking',
    'những pha', 'những khoảnh khắc', 'những loài', 'danh sách', 'best of', 'funniest moments',
    # Commentary & Reviews & Voiceover
    'reaction', 'review', 'thuyết minh', 'lồng tiếng', 'tóm tắt', 'giải thích',
    'kể chuyện', 'phân tích', 'bình luận', 'podcast', 'audiobook', 'sách nói',
    'talk show', 'vlog', 'tin tức', 'bản tin',
    # Series, Movies, Music, Gaming
    'tập ', 'phần ', 'part ', 'episode', 'ep ', 'mv', 'official video', 'karaoke', 'remix',
    'full hd', 'trailer', 'teaser', 'gameplay', 'walkthrough',
    # Educational / Kids / Cartoons
    'cho bé', 'dạy bé', 'học tiếng', 'hoạt hình'
]

# Ánh xạ chủ đề tiếng Việt sang từ khóa tìm kiếm video gốc (raw footage) triệu view
TOPIC_ENHANCEMENT_MAP = {
    'động vật': 'smart cute animal caught on camera #shorts',
    'động vật đáng yêu': 'cute smart animal real moment #shorts',
    'động vật đáng yêu và thông minh': 'smart cute animal caught on camera #shorts',
    'thú cưng': 'funny pet smart moment caught on camera #shorts',
    'chó': 'smart dog clever trick caught on camera #shorts',
    'mèo': 'funny cat smart moment #shorts',
    'sự thật': 'incredible real life moment caught on tape #shorts',
    'sự thật thú vị': 'incredible real phenomenon caught on camera #shorts',
    'sự thật thú vị về thế giới': 'amazing real world phenomenon caught on camera #shorts',
    'sự thật lạ': 'unbelievable real life moment caught on camera #shorts',
    'khoa học': 'cool science experiment demonstration satisfying #shorts',
    'khoa học kỳ thú': 'satisfying science experiment real demonstration #shorts',
    'thí nghiệm': 'physics chemistry experiment satisfying #shorts',
    'asmr': 'oddly satisfying soap cutting kinetic sand asmr #shorts',
    'asmr thoải mái': 'oddly satisfying soap cutting kinetic sand #shorts',
    'món ngon': 'street food cooking preparation skills #shorts',
    'ẩm thực': 'delicious street food making skills #shorts',
    'mẹo hay': 'clever genius life hacks satisfying fix #shorts',
    'mẹo vặt': 'smart handy life hack clever idea #shorts',
    'chế tác': 'precision metalworking restoration craft woodworking #shorts',
    'cơ khí': 'precision machining restoration metal lathe #shorts',
    'thủ công': 'satisfying handmade craft restoration #shorts',
    'bí ẩn': 'unexplained real phenomenon caught on camera #shorts',
    'kỳ thú': 'fascinating real world phenomenon #shorts',
    'thể thao': 'incredible athletic skill caught on camera #shorts',
    'ảo thuật': 'magic trick illusion close up #shorts'
}

def enhance_search_query(raw_query):
    clean = (raw_query or "").strip()
    if not clean:
        return "oddly satisfying raw clip #shorts -compilation -top"

    clean_lower = clean.lower()
    for key, enhanced in TOPIC_ENHANCEMENT_MAP.items():
        if key in clean_lower:
            return enhanced

    # Nếu người dùng nhập từ khóa riêng, thêm tiền tố #shorts và loại trừ compilation/review
    return f"{clean} #shorts -compilation -reaction -review -top"

def is_compilation_or_commentary(title, uploader):
    text = f"{title or ''} {uploader or ''}".lower()
    for bad in BLACKLIST_KEYWORDS:
        if bad in text:
            return True
    return False

def clean_display_title(raw_title):
    if not raw_title:
        return "Video gốc"
    # Loại bỏ các hashtag rác như #shorts, #viral, #fyp khỏi tiêu đề hiển thị
    cleaned = re.sub(r'#\w+', '', raw_title).strip()
    cleaned = re.sub(r'\s+', ' ', cleaned)
    return cleaned if len(cleaned) > 3 else raw_title.strip()

def search_videos(query, max_results=8):
    try:
        if hasattr(sys.stdout, 'reconfigure'):
            sys.stdout.reconfigure(encoding='utf-8')

        enhanced_query = enhance_search_query(query)

        ydl_opts = {
            'extract_flat': True,
            'quiet': True,
            'no_warnings': True,
            'noplaylist': True,
        }

        # Tìm kiếm 30 kết quả để sàng lọc kỹ lưỡng chỉ lấy video ngắn gốc
        search_count = max(max_results * 4, 30)
        entries = []
        with yt_dlp.YoutubeDL(ydl_opts) as ydl:
            try:
                info = ydl.extract_info(f"ytsearch{search_count}:{enhanced_query}", download=False)
                if info and info.get('entries'):
                    entries = [e for e in info.get('entries', []) if e]
            except Exception:
                entries = []

        seen_ids = set()
        candidates = []

        for e in entries:
            if not e:
                continue

            vid_id = e.get('id')
            if not vid_id or vid_id in seen_ids:
                continue

            # Bỏ qua channel / playlist
            if vid_id.startswith('UC') or e.get('ie_key') == 'YoutubeTab' or 'channel/' in e.get('url', ''):
                continue

            dur = e.get('duration')
            # TIÊU CHUẨN 1: CHỈ TÌM VIDEO NGẮN CHUẨN (Từ 22s đến tối đa 62s, loại bỏ video quá ngắn dưới 22s)
            if not dur or dur < 22 or dur > 62:
                continue

            raw_title = (e.get("title") or "").strip()
            uploader = (e.get("uploader") or e.get("channel") or "").strip()

            # TIÊU CHUẨN 2: CHỈ LẤY VIDEO GỐC (Không lấy top 10, tổng hợp, review, thuyết minh)
            if is_compilation_or_commentary(raw_title, uploader):
                continue

            seen_ids.add(vid_id)

            url = f"https://www.youtube.com/shorts/{vid_id}"

            thumb = e.get('thumbnail')
            if not thumb and e.get('thumbnails'):
                thumb = e['thumbnails'][-1].get('url')

            candidates.append({
                "id": vid_id,
                "title": clean_display_title(raw_title),
                "url": url,
                "duration": dur,
                "thumbnail": thumb or f"https://i.ytimg.com/vi/{vid_id}/hqdefault.jpg",
                "uploader": uploader or "Viral Shorts",
                "viewCount": e.get("view_count") or 0,
                "platform": "YouTube"
            })

            if len(candidates) >= max_results:
                break

        print(json.dumps({"success": True, "videos": candidates}, ensure_ascii=False))
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

            dur = info.get("duration", 0)
            raw_title = info.get("title", "")

            result = {
                "success": True,
                "filePath": os.path.abspath(final_path),
                "title": clean_display_title(raw_title),
                "duration": dur,
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

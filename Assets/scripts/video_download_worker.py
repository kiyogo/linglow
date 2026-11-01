import sys
import json
import os
import urllib.parse
import re
import yt_dlp

# Danh sách từ khóa bị cấm: Tuyệt đối loại bỏ video tổng hợp, top 10, review, thuyết minh sẵn, và camera an ninh mờ nhòe
BLACKLIST_KEYWORDS = [
    # Compilations & Rankings
    'compilation', 'tổng hợp', 'top 10', 'top 5', 'top 3', 'top ', 'bxh', 'ranking',
    'những pha', 'những khoảnh khắc', 'những loài', 'danh sách', 'best of', 'funniest moments',
    # Commentary & Reviews & Voiceover
    'reaction', 'review', 'thuyết minh', 'lồng tiếng', 'tóm tắt', 'giải thích',
    'kể chuyện', 'phân tích', 'bình luận', 'podcast', 'audiobook', 'sách nói',
    'talk show', 'vlog', 'tin tức', 'bản tin', 'news',
    # Grainy CCTV / Security Cameras / Dashcams / Paranormal (Loại bỏ triệt để video đen trắng, camera giám sát mờ)
    'security cam', 'cctv', 'dashcam', 'surveillance', 'alien', 'ufo', 'ghost', 'paranormal',
    'unexplained', 'caught on tape', 'camera footage',
    # Series, Movies, Music, Gaming
    'tập ', 'phần ', 'part ', 'episode', 'ep ', 'mv', 'official video', 'karaoke', 'remix',
    'full hd', 'trailer', 'teaser', 'gameplay', 'walkthrough',
    # Educational / Kids / Cartoons
    'cho bé', 'dạy bé', 'học tiếng', 'hoạt hình'
]

# Ánh xạ chủ đề tiếng Việt sang từ khóa tìm kiếm video gốc chất lượng cao (HD, màu sắc rực rỡ, lôi cuốn triệu view)
TOPIC_ENHANCEMENT_MAP = {
    'động vật': '#shorts funny cute puppy dog golden retriever clever',
    'động vật đáng yêu': '#shorts cute puppy golden retriever adorable playful',
    'động vật thông minh': '#shorts clever dog smart animal trick',
    'thú cưng': '#shorts funny dog cat cute playful tricks',
    'chó': '#shorts smart clever dog golden retriever funny',
    'mèo': '#shorts funny cute kitten cat playful',
    'sự thật': '#shorts mind blowing nature moments optical illusion satisfying',
    'sự thật thú vị': '#shorts mind blowing nature moments optical illusion satisfying',
    'sự thật thú vị về thế giới': '#shorts mind blowing nature moments optical illusion satisfying',
    'sự thật lạ': '#shorts mind blowing nature moments optical illusion satisfying',
    'khoa học': '#shorts satisfying science experiments physics demonstration',
    'khoa học kỳ thú': '#shorts satisfying science experiments physics demonstration',
    'thí nghiệm': '#shorts satisfying science experiments physics demonstration',
    'asmr': '#shorts oddly satisfying soap cutting kinetic sand slime',
    'asmr thoả mãn': '#shorts oddly satisfying soap cutting kinetic sand slime',
    'asmr thoải mái': '#shorts oddly satisfying soap cutting kinetic sand slime',
    'món ngon': '#shorts street food cooking master skills satisfying',
    'ẩm thực': '#shorts delicious street food cooking master skills',
    'mẹo hay': '#shorts genius woodworking tricks clever handy tools diy',
    'mẹo vặt': '#shorts clever handy life hacks woodworking tools diy',
    'chế tác': '#shorts satisfying rusty tool restoration craft lathe',
    'cơ khí': '#shorts precision metal lathe machining restoration',
    'thủ công': '#shorts satisfying handmade craft woodworking restoration',
    'bí ẩn': '#shorts mind blowing natural wonders incredible phenomena',
    'kỳ thú': '#shorts fascinating real world phenomena satisfying',
    'thể thao': '#shorts incredible human athletic skill parkour',
    'ảo thuật': '#shorts magic trick illusion close up street'
}

# Kho video triệu view chất lượng cao dự phòng (đảm bảo luôn đủ 6-8 video đẹp)
CURATED_VIRAL_POOL = [
    {
        "id": "pm2NhM30TtA",
        "title": "Incredible Moments Caught on Camera",
        "url": "https://www.youtube.com/shorts/pm2NhM30TtA",
        "duration": 25,
        "thumbnail": "https://i.ytimg.com/vi/pm2NhM30TtA/hqdefault.jpg",
        "uploader": "Break Time",
        "viewCount": 5400000,
        "platform": "YouTube"
    },
    {
        "id": "iuIT5wc-7AQ",
        "title": "A 3D rug that fools the dogs - Amazing Optical Illusion",
        "url": "https://www.youtube.com/shorts/iuIT5wc-7AQ",
        "duration": 38,
        "thumbnail": "https://i.ytimg.com/vi/iuIT5wc-7AQ/hqdefault.jpg",
        "uploader": "Visual Wonders",
        "viewCount": 8200000,
        "platform": "YouTube"
    },
    {
        "id": "woze7VTcawI",
        "title": "Cún cưng phản ứng đáng yêu khi nghe gọi qua chuông cửa",
        "url": "https://www.youtube.com/shorts/woze7VTcawI",
        "duration": 35,
        "thumbnail": "https://i.ytimg.com/vi/woze7VTcawI/hqdefault.jpg",
        "uploader": "RingTV",
        "viewCount": 4200000,
        "platform": "YouTube"
    },
    {
        "id": "__VcPjEzeOI",
        "title": "Chú chó Husky thông minh xử lý tình huống cực đỉnh",
        "url": "https://www.youtube.com/shorts/__VcPjEzeOI",
        "duration": 46,
        "thumbnail": "https://i.ytimg.com/vi/__VcPjEzeOI/hqdefault.jpg",
        "uploader": "Smart Pets",
        "viewCount": 3850000,
        "platform": "YouTube"
    },
    {
        "id": "4sC5FgiGvjY",
        "title": "Spiral Illusion Hypnotic Visual - Ảo giác thị giác kỳ thú",
        "url": "https://www.youtube.com/shorts/4sC5FgiGvjY",
        "duration": 50,
        "thumbnail": "https://i.ytimg.com/vi/4sC5FgiGvjY/hqdefault.jpg",
        "uploader": "Illusion World",
        "viewCount": 6100000,
        "platform": "YouTube"
    },
    {
        "id": "C9XH7R0It8w",
        "title": "Squaring the circle - Hiện tượng hình học kỳ diệu",
        "url": "https://www.youtube.com/shorts/C9XH7R0It8w",
        "duration": 30,
        "thumbnail": "https://i.ytimg.com/vi/C9XH7R0It8w/hqdefault.jpg",
        "uploader": "Math Curiosities",
        "viewCount": 2900000,
        "platform": "YouTube"
    },
    {
        "id": "7x2oL3Kzoog",
        "title": "Thí nghiệm vật lý độc lạ và mãn nhãn",
        "url": "https://www.youtube.com/shorts/7x2oL3Kzoog",
        "duration": 46,
        "thumbnail": "https://i.ytimg.com/vi/7x2oL3Kzoog/hqdefault.jpg",
        "uploader": "Science Lab",
        "viewCount": 3200000,
        "platform": "YouTube"
    },
    {
        "id": "U3a1-omUuWY",
        "title": "Phục chế thanh kim loại rỉ sét sáng loáng như mới",
        "url": "https://www.youtube.com/shorts/U3a1-omUuWY",
        "duration": 37,
        "thumbnail": "https://i.ytimg.com/vi/U3a1-omUuWY/hqdefault.jpg",
        "uploader": "Restoration ASMR",
        "viewCount": 7500000,
        "platform": "YouTube"
    }
]

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

        # Tìm kiếm 60 kết quả để sàng lọc kỹ lưỡng chỉ lấy video ngắn gốc chất lượng cao
        search_count = max(max_results * 8, 60)
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
            # TIÊU CHUẨN 1: CHỈ TÌM VIDEO NGẮN CHUẨN SHORTS (Từ 12s đến 65s)
            if not dur or dur < 12 or dur > 65:
                continue

            raw_title = (e.get("title") or "").strip()
            uploader = (e.get("uploader") or e.get("channel") or "").strip()

            # TIÊU CHUẨN 2: CHỈ LẤY VIDEO GỐC (Không lấy top 10, tổng hợp, review, thuyết minh, CCTV mờ)
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

        # Nếu tìm kiếm trực tuyến còn thiếu so với yêu cầu, bổ sung từ kho video triệu view chọn lọc
        if len(candidates) < max_results:
            for item in CURATED_VIRAL_POOL:
                if item["id"] not in seen_ids:
                    candidates.append(item)
                    seen_ids.add(item["id"])
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
            'format': 'bestvideo[height<=2160]+bestaudio/bestvideo+bestaudio/best',
            'merge_output_format': 'mp4',
            'quiet': True,
            'no_warnings': True,
            'noplaylist': True,
            'overwrites': True,
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

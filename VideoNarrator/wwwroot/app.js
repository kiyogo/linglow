// ===================================================================
// VIDEO NARRATOR AI - MODERN MATERIAL DESIGN 3 / GOOGLE WORKSPACE
// ===================================================================

// Exact Persona Profiles enum as required
const PERSONAS = {
    the_gioi_co_gi_hay: {
        id: "the_gioi_co_gi_hay",
        name: "🌍 Thế Giới Có Gì Hay? (Sự Thật & Mẹo Độc Lạ)",
        desc: "Chuẩn Zack D. Films & Thế Giới Có Gì Hay: Hook trực diện, bóc tách cơ chế và mánh khóe cực cuốn hút."
    },
    mad_muse: {
        id: "mad_muse",
        name: "Nàng Thơ Điên Rồ (Mad Muse)",
        desc: "Điện ảnh, cuốn hút, từng câu từng chữ đều thôi miên thị giác người xem."
    },
    genz_troll: {
        id: "genz_troll",
        name: "Gen Z Sát Thủ Bắt Trend",
        desc: "Hài hước, dí dỏm, bẻ lái bất ngờ, đậm chất viral mạng xã hội."
    },
    hypnotic_zen: {
        id: "hypnotic_zen",
        name: "Thôi Miên Thiền Định",
        desc: "Trầm ấm, sâu lắng, chữa lành tâm hồn với nhịp thở thư thái."
    },
    dark_documentarian: {
        id: "dark_documentarian",
        name: "Hồ Sơ Đen Tối",
        desc: "Kịch tính, gay cấn, hồi hộp, giọng điệu trinh thám điều tra ly kỳ."
    },
    high_octane_hype: {
        id: "high_octane_hype",
        name: "Bùng Nổ Năng Lượng",
        desc: "Dồn dập, hào hứng, nhiệt huyết, thôi thúc hành động ngay lập tức."
    },
    doi_co_gi_hay: {
        id: "doi_co_gi_hay",
        name: "Đời Có Gì Hay",
        desc: "Chân thực, góc nhìn đời thường, mộc mạc và sâu sắc."
    }
};

// ==================== BESPOKE SVG VECTOR ICON HELPER ====================
function setSvgUse(elementOrId, iconId, extraClass) {
    const el = typeof elementOrId === 'string' ? document.getElementById(elementOrId) : elementOrId;
    if (!el) return;
    if (el.tagName && el.tagName.toLowerCase() === 'svg') {
        let useEl = el.querySelector('use');
        if (!useEl) {
            useEl = document.createElementNS('http://www.w3.org/2000/svg', 'use');
            el.appendChild(useEl);
        }
        useEl.setAttribute('href', `#icon-${iconId}`);
        useEl.setAttributeNS('http://www.w3.org/1999/xlink', 'xlink:href', `#icon-${iconId}`);
        if (extraClass !== undefined) {
            el.setAttribute('class', `vn-icon ${extraClass}`.trim());
        }
    } else {
        el.innerHTML = `<svg class="vn-icon ${extraClass || ''}"><use href="#icon-${iconId}"></use></svg>`;
    }
}

// ==================== STATE MANAGEMENT ====================
let currentStep = 1;
let currentVideoData = null;
let currentResultVideoUrl = null;
let isGenerating = false;
let isDarkTheme = true;
let isScriptDirtyByUser = false;

// ==================== SAFE BACKEND BRIDGE WRAPPER ====================
function getBridge() {
    return window.chrome?.webview?.hostObjects?.bridge || null;
}

const safeBridge = {
    get isAvailable() {
        return !!getBridge();
    },

    async selectVideo() {
        console.log("[safeBridge] selectVideo called");
        const b = getBridge();
        if (b) {
            // First try SelectVideoFile (original C# method present in all builds)
            try {
                await b.SelectVideoFile();
                return;
            } catch (e1) {
                console.warn("[Bridge] SelectVideoFile error, trying selectVideo:", e1);
            }

            try {
                await b.selectVideo();
                return;
            } catch (e2) {
                console.warn("[Bridge] selectVideo error:", e2);
            }
        }

        // Secondary fallback: Native HTML5 file input dialog
        const nativeInput = document.getElementById('nativeFileInput');
        if (nativeInput) {
            nativeInput.click();
            return;
        }

        showToast("Vui lòng chọn video từ máy tính của bạn.");
    },

    async loadVideoPath(filePath) {
        const b = getBridge();
        if (b) {
            try { await b.LoadVideoPath(filePath); return; } catch {}
            try { await b.loadVideo(filePath); return; } catch {}
        }
    },

    async analyzeVideo() {
        const b = getBridge();
        if (b) {
            try { await b.StartAutoEdit(); return; } catch {}
            try { await b.analyzeVideo(); return; } catch {}
        }
    },

    async generateScript(topic, personaId, stylePreset) {
        const b = getBridge();
        if (b) {
            try { await b.SetCustomTopic(topic || ""); } catch {}
            try { await b.SetPersona(personaId || "the_gioi_co_gi_hay"); } catch {}
            try { await b.SetStylePreset(stylePreset || "TikTok Shorts"); } catch {}
            try { await b.GenerateScriptOnly(); return; } catch {}
            try { await b.generateScript(); return; } catch {}
        }
        showToast("Vui lòng chọn video và kết nối ứng dụng để tạo kịch bản AI.");
    },

    async generateVoice(voice, speed, pitch) {
        const b = getBridge();
        if (b) {
            try { await b.SetTtsVoice(voice); } catch {}
            try { await b.SetSpeechRate(speed); } catch {}
            try { await b.SetSpeechPitch(pitch); } catch {}
            try { await b.generateVoice(); return; } catch {}
        }
        showToast("Vui lòng kết nối ứng dụng để nghe thử giọng đọc AI.");
    },

    async createVideo(options) {
        const b = getBridge();
        if (b) {
            try { await b.SetPersona(options.personaId); } catch {}
            try { await b.SetStylePreset(options.stylePreset); } catch {}
            try { await b.SetDurationMode(options.durationMode); } catch {}
            if (options.resolution) {
                try { await b.SetResolution(options.resolution); } catch {}
            }
            try { await b.SetCustomTopic(options.topic); } catch {}
            try { await b.UpdateHook(options.hook); } catch {}
            try { await b.UpdateScript(options.script); } catch {}
            try { await b.SetTtsVoice(options.voice); } catch {}
            try { await b.SetSpeechRate(options.speechRate); } catch {}
            try { await b.SetSpeechPitch(options.speechPitch); } catch {}
            try {
                await b.SetToggle("autocut", options.autoCut);
                await b.SetToggle("autopan", options.autoPan);
                await b.SetToggle("autozoom", options.autoZoom);
                await b.SetToggle("autosubtitle", options.autoSubtitle);
                await b.SetToggle("keywordhighlight", options.keywordHighlight);
                await b.SetToggle("musicducking", options.musicDucking);
                await b.SetToggle("soundeffects", options.soundEffects);
            } catch {}

            try { await b.StartAutoEdit(); return; } catch {}
            try { await b.createVideo(); return; } catch {}
        }
        showToast("Vui lòng nạp video trước khi thực hiện dựng video.");
    },

    async downloadAndAutoProduce(url) {
        const b = getBridge();
        if (b) {
            try { await b.DownloadAndAutoProduce(url); return; } catch {}
            try { await b.downloadAndAutoProduce(url); return; } catch {}
        }
        showToast("Vui lòng khởi chạy ứng dụng để tự động hóa 1-Click.");
    },

    async downloadVideoOnly(url) {
        const b = getBridge();
        if (b) {
            try { await b.DownloadVideoOnly(url); return; } catch {}
            try { await b.downloadVideoOnly(url); return; } catch {}
        }
        showToast("Vui lòng khởi chạy ứng dụng để tải video.");
    },

    async cancelProcess() {
        const b = getBridge();
        if (b) {
            try { await b.CancelProcess(); } catch {}
            try { await b.cancelProcess(); } catch {}
        }
        hideProgressOverlay();
        setProcessing(false);
        showToast("Đã gửi yêu cầu hủy tiến trình tạo video");
    },

    async openOutputFolder() {
        const b = getBridge();
        if (b) {
            try { await b.OpenOutputFolder(); return; } catch {}
            try { await b.openOutputFolder(); return; } catch {}
        }
    },

    async openFinalVideo() {
        const b = getBridge();
        if (b) {
            try { await b.OpenFinalVideo(); return; } catch {}
            try { await b.openFinalVideo(); return; } catch {}
        }
        const videoEl = document.getElementById('videoPlayer');
        if (videoEl && !videoEl.paused) {
            videoEl.pause();
        } else if (videoEl) {
            videoEl.play();
        }
        showToast("Đang phát video thành phẩm");
    },

    async clearScript() {
        const b = getBridge();
        if (b) {
            try { await b.ClearScript(); return; } catch {}
            try { await b.clearScript(); return; } catch {}
        }
    },

    async resetProjectState() {
        const b = getBridge();
        if (b) {
            try { await b.ResetProjectState(); return; } catch {}
            try { await b.resetProjectState(); return; } catch {}
        }
    },

    async saveSettings(geminiKey, ttsKey, model, voice, outDir, apiUrl) {
        const effectiveUrl = "http://192.168.1.15:6660/api";
        const effectiveKey = "";
        const b = getBridge();
        if (b) {
            try { await b.SaveFullSettings(effectiveKey, model, "Edge-TTS", ttsKey, voice, outDir, effectiveUrl); return; } catch {}
            try { await b.saveSettings(effectiveKey, ttsKey, model, voice, outDir, effectiveUrl); return; } catch {}
        }
        localStorage.removeItem('vn_gemini_key');
        localStorage.setItem('vn_gemini_api_url', effectiveUrl);
        localStorage.setItem('vn_gemini_model', model);
        localStorage.setItem('vn_output_dir', outDir);
        showToast("Đã lưu cấu hình cài đặt (Local)");
    },

    async loadSettings() {
        localStorage.removeItem('vn_gemini_key');
        const b = getBridge();
        if (b) {
            try {
                const raw = await b.GetInitialStateJson();
                return JSON.parse(raw);
            } catch {}
            try {
                const raw = await b.loadSettings();
                return JSON.parse(raw);
            } catch {}
        }
        return {
            geminiApiKey: "",
            geminiApiUrl: "http://192.168.1.15:6660/api",
            geminiModel: localStorage.getItem('vn_gemini_model') || "gemini-3.1-flash-lite",
            outputDir: localStorage.getItem('vn_output_dir') || ""
        };
    },

    async setSpeechRate(rate) {
        const b = getBridge();
        if (b) {
            try { await b.SetSpeechRate(rate); } catch {}
        }
    },

    async setSpeechPitch(pitch) {
        const b = getBridge();
        if (b) {
            try { await b.SetSpeechPitch(pitch); } catch {}
        }
    },

    async setResolution(resolution) {
        const b = getBridge();
        if (b) {
            try { await b.SetResolution(resolution); } catch {}
        }
    }
};

// ==================== DOM INITIALIZATION ====================

window.addEventListener('DOMContentLoaded', async () => {
    initTheme();
    initStepper();
    initEventListeners();
    initPersonaDropdown();
    await initBridgeConnection();
});

function initTheme() {
    const saved = localStorage.getItem('vn_theme');
    if (saved === 'light') {
        setTheme(false);
    } else {
        setTheme(true);
    }
}

function setTheme(dark) {
    isDarkTheme = dark;
    const themeIcon = document.getElementById('themeIcon');
    const btnToggle = document.getElementById('btnToggleTheme');
    if (dark) {
        document.documentElement.setAttribute('data-theme', 'dark');
        setSvgUse(themeIcon, 'sun');
        if (btnToggle) btnToggle.title = 'Chuyển sang chế độ Sáng';
        localStorage.setItem('vn_theme', 'dark');
    } else {
        document.documentElement.setAttribute('data-theme', 'light');
        setSvgUse(themeIcon, 'moon');
        if (btnToggle) btnToggle.title = 'Chuyển sang chế độ Tối';
        localStorage.setItem('vn_theme', 'light');
    }
}

function initPersonaDropdown() {
    const personaSelect = document.getElementById('personaSelect');
    personaSelect.innerHTML = '';
    for (const key in PERSONAS) {
        const p = PERSONAS[key];
        const opt = document.createElement('option');
        opt.value = p.id;
        opt.textContent = p.name;
        if (p.id === 'the_gioi_co_gi_hay') opt.selected = true;
        personaSelect.appendChild(opt);
    }
    updatePersonaHighlight('the_gioi_co_gi_hay');
}

function updatePersonaHighlight(personaId) {
    const p = PERSONAS[personaId] || PERSONAS.the_gioi_co_gi_hay || PERSONAS.mad_muse;
    document.getElementById('personaNameTag').textContent = p.name;
    document.getElementById('personaDescText').textContent = p.desc;
}

// ==================== STEPPER NAVIGATION ====================

function initStepper() {
    const stepBtns = document.querySelectorAll('.step-btn');
    stepBtns.forEach(btn => {
        btn.addEventListener('click', () => {
            const targetStep = parseInt(btn.dataset.step, 10);
            goToStep(targetStep);
        });
    });
}

function goToStep(step) {
    if (step < 1 || step > 5) return;
    currentStep = step;

    // Update sidebar buttons if any exist
    document.querySelectorAll('.step-btn').forEach(btn => {
        const s = parseInt(btn.dataset.step, 10);
        btn.classList.toggle('active', s === step);
        if (s < step) {
            btn.classList.add('completed');
        }
    });

    // Update panels: Step 5 shows Result panel, Steps 1-4 show Studio panel
    document.querySelectorAll('.step-panel').forEach(panel => {
        panel.classList.remove('active');
    });

    if (step === 5) {
        const p5 = document.getElementById('panelStep5');
        if (p5) p5.classList.add('active');
    } else {
        const p1 = document.getElementById('panelStep1');
        if (p1) p1.classList.add('active');
    }
}

function updateAspectRatioUI(res) {
    const box = document.getElementById('previewViewportBox');
    const badge = document.getElementById('valSelectedAspect');
    const pill = document.getElementById('previewRatioPill');
    const hint = document.getElementById('resolutionHint');
    const badgeTarget = document.getElementById('badgeTargetRatio');
    if (!box) return;

    box.classList.remove('aspect-16-9', 'aspect-1-1', 'aspect-original');

    const resStr = (res || '').toLowerCase();
    if (resStr.includes('16:9') || resStr.includes('1920x1080') || resStr.includes('1280x720')) {
        box.classList.add('aspect-16-9');
        if (badge) badge.textContent = '16:9 Ngang';
        if (pill) pill.textContent = '16:9 Ngang';
        if (badgeTarget) badgeTarget.textContent = 'Target: 16:9';
        if (hint) hint.textContent = 'Khung hình ngang 16:9 chuẩn đăng YouTube, Facebook, TV.';
    } else if (resStr.includes('1:1') || resStr.includes('1080x1080')) {
        box.classList.add('aspect-1-1');
        if (badge) badge.textContent = '1:1 Vuông';
        if (pill) pill.textContent = '1:1 Vuông';
        if (badgeTarget) badgeTarget.textContent = 'Target: 1:1';
        if (hint) hint.textContent = 'Khung hình vuông 1:1 chuẩn đăng Instagram Feed & bài viết mạng xã hội.';
    } else if (resStr.includes('original') || resStr.includes('gốc')) {
        box.classList.add('aspect-original');
        if (badge) badge.textContent = 'Gốc (Original)';
        if (pill) pill.textContent = 'Kích thước gốc';
        if (badgeTarget) badgeTarget.textContent = 'Target: Gốc';
        if (hint) hint.textContent = 'Giữ nguyên 100% kích thước và tỷ lệ gốc của video ban đầu (không crop viền).';
    } else {
        // Default 9:16
        if (badge) badge.textContent = '9:16 Dọc TikTok';
        if (pill) pill.textContent = '9:16 Dọc';
        if (badgeTarget) badgeTarget.textContent = 'Target: 9:16';
        if (hint) hint.textContent = 'Tự động canh chỉnh khuôn mặt / chủ thể chính vào giữa khung hình dọc chuẩn TikTok.';
    }
}

// ==================== BRIDGE REAL-TIME MESSAGING ====================

async function initBridgeConnection() {
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.addEventListener('message', (event) => {
            try {
                const data = typeof event.data === 'string' ? JSON.parse(event.data) : event.data;
                handleBridgeMessage(data);
            } catch (e) {
                console.error("Lỗi phân tích tin nhắn bridge:", e);
            }
        });

        // Load initial state from backend
        try {
            const settings = await safeBridge.loadSettings();
            if (settings.geminiApiUrl && document.getElementById('geminiApiUrlInput')) document.getElementById('geminiApiUrlInput').value = settings.geminiApiUrl;
            if (settings.geminiApiKey) document.getElementById('geminiKey').value = settings.geminiApiKey;
            if (settings.ttsApiKey) document.getElementById('ttsKey').value = settings.ttsApiKey;
            if (settings.outputDir) document.getElementById('outputDir').value = settings.outputDir;
            if (settings.geminiModel) document.getElementById('geminiModelSelect').value = settings.geminiModel;

            if (settings.ttsVoice) {
                const voiceSelect = document.getElementById('ttsVoiceSelect');
                if (voiceSelect) voiceSelect.value = settings.ttsVoice;
            }
            if (settings.speechRate) {
                const rateSlider = document.getElementById('rangeSpeechRate');
                if (rateSlider) {
                    rateSlider.value = settings.speechRate;
                    document.getElementById('valSpeechRate').textContent = `${parseFloat(settings.speechRate).toFixed(2)}x`;
                }
            }
            if (settings.speechPitch !== undefined && settings.speechPitch !== null) {
                const pitchSlider = document.getElementById('rangeSpeechPitch');
                if (pitchSlider) {
                    pitchSlider.value = settings.speechPitch;
                    const p = parseFloat(settings.speechPitch);
                    document.getElementById('valSpeechPitch').textContent = `${p >= 0 ? '+' : ''}${p.toFixed(1)}`;
                }
            }

            if (settings.selectedResolution) {
                const resSelect = document.getElementById('resolutionSelect');
                if (resSelect) {
                    for (let i = 0; i < resSelect.options.length; i++) {
                        const optVal = resSelect.options[i].value;
                        if (optVal === settings.selectedResolution || 
                            settings.selectedResolution.includes(optVal.split(' ')[0]) ||
                            (settings.selectedResolution.toLowerCase().includes('original') && optVal.toLowerCase().includes('original'))) {
                            resSelect.selectedIndex = i;
                            break;
                        }
                    }
                    updateAspectRatioUI(resSelect.value);
                }
            }

            if (settings.videoUrlInput && document.getElementById('videoUrlInput')) {
                document.getElementById('videoUrlInput').value = settings.videoUrlInput;
            }

            if (settings.hookText) document.getElementById('hookInput').value = settings.hookText;
            if (settings.scriptText) {
                document.getElementById('scriptInput').value = settings.scriptText;
                updateWordCount(settings.scriptText);
            }
            if (settings.customTopic) document.getElementById('topicInput').value = settings.customTopic;

            if (settings.hasFinalVideo && settings.finalVideoUrl) {
                handleFinalVideoReady({ mediaUrl: settings.finalVideoUrl });
            } else if (settings.hasVideo && settings.videoUrl) {
                handleVideoLoaded(settings);
            }
        } catch (e) {
            console.warn("Không thể tải trạng thái ban đầu:", e);
        }
    }
}

function handleBridgeMessage(msg) {
    if (!msg || !msg.type) return;

    switch (msg.type) {
        case 'status':
            updateStatusText(msg.message);
            break;

        case 'progress':
            updateProgressPercentage(msg.percentage);
            break;

        case 'busy':
            setProcessing(msg.isBusy, msg.canCancel);
            break;

        case 'error':
            if (msg.hasError || msg.message) {
                showError(msg.message);
            } else {
                hideError();
            }
            break;

        case 'videoLoaded':
            handleVideoLoaded(msg);
            break;

        case 'previewReady':
            handlePreviewReady(msg);
            break;

        case 'finalVideoReady':
            handleFinalVideoReady(msg);
            break;

        case 'hook':
            if (msg.hook !== undefined) {
                document.getElementById('hookInput').value = msg.hook || "";
            }
            break;

        case 'videoUrlInput':
            const urlInputBox = document.getElementById('videoUrlInput');
            if (urlInputBox && msg.url !== undefined) {
                urlInputBox.value = msg.url || "";
            }
            break;

        case 'script':
            if (msg.script !== undefined) {
                document.getElementById('scriptInput').value = msg.script || "";
                updateWordCount(msg.script || "");
                isScriptDirtyByUser = false;
            }
            break;
    }
}

// ==================== EVENT LISTENERS ====================

function initEventListeners() {
    // Theme toggle
    document.getElementById('btnToggleTheme').addEventListener('click', () => setTheme(!isDarkTheme));

    // Open Output Folder
    document.getElementById('btnOpenFolder').addEventListener('click', () => safeBridge.openOutputFolder());
    document.getElementById('btnOpenResultFolder').addEventListener('click', () => safeBridge.openOutputFolder());

    // Settings Modal (Cài Đặt Tùy Chọn Nâng Cao)
    const settingsModal = document.getElementById('settingsModal');
    document.getElementById('btnSettings')?.addEventListener('click', () => settingsModal?.classList.remove('hidden'));
    document.getElementById('btnOpenAdvancedModal')?.addEventListener('click', () => settingsModal?.classList.remove('hidden'));
    document.getElementById('btnCloseSettings')?.addEventListener('click', () => settingsModal?.classList.add('hidden'));
    document.getElementById('btnCancelSettings')?.addEventListener('click', () => settingsModal?.classList.add('hidden'));
    const btnToggleGeminiKey = document.getElementById('btnToggleGeminiKey');
    if (btnToggleGeminiKey) {
        btnToggleGeminiKey.addEventListener('click', () => {
            const inp = document.getElementById('geminiKey');
            if (inp) inp.type = inp.type === 'password' ? 'text' : 'password';
        });
    }
    document.getElementById('btnSaveSettings')?.addEventListener('click', async () => {
        const gUrl = "http://192.168.1.15:6660/api";
        const gKey = "";
        const tKey = document.getElementById('ttsKey')?.value || "";
        const gModel = document.getElementById('geminiModelSelect')?.value || "gemini-3.1-flash-lite";
        const tVoice = document.getElementById('ttsVoiceSelect')?.value || "";
        const outDir = document.getElementById('outputDir')?.value || "";
        await safeBridge.saveSettings(gKey, tKey, gModel, tVoice, outDir, gUrl);
        settingsModal?.classList.add('hidden');
        showToast("Đã lưu các tùy chọn nâng cao thành công!");
    });

    // STEP 1: 1-Click Social Video Download & Auto Produce
    const btnOneClick = document.getElementById('btnOneClickProduce');
    const btnDlOnly = document.getElementById('btnDownloadOnly');
    const inputUrl = document.getElementById('videoUrlInput');

    if (btnOneClick) {
        btnOneClick.addEventListener('click', async (e) => {
            e.preventDefault();
            const url = inputUrl?.value?.trim();
            if (!url) {
                showToast("Vui lòng dán link video (TikTok, YouTube Shorts, Reels, FB...)");
                inputUrl?.focus();
                return;
            }
            showToast("⚡ Đang bắt đầu tự động tải và dựng video 1-Click...");
            await safeBridge.downloadAndAutoProduce(url);
        });
    }

    if (btnDlOnly) {
        btnDlOnly.addEventListener('click', async (e) => {
            e.preventDefault();
            const url = inputUrl?.value?.trim();
            if (!url) {
                showToast("Vui lòng dán link video (TikTok, YouTube Shorts, Reels, FB...)");
                inputUrl?.focus();
                return;
            }
            showToast("Đang tải video về máy...");
            await safeBridge.downloadVideoOnly(url);
        });
    }

    if (inputUrl) {
        inputUrl.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') {
                e.preventDefault();
                btnOneClick?.click();
            }
        });
    }

    // STEP 1: Video Selection
    const btnSelect = document.getElementById('btnSelectVideo');
    if (btnSelect) {
        btnSelect.addEventListener('click', (e) => {
            e.preventDefault();
            e.stopPropagation();
            safeBridge.selectVideo();
        });
    }

    const dropzone = document.getElementById('dropzoneVideo');
    if (dropzone) {
        dropzone.addEventListener('click', (e) => {
            safeBridge.selectVideo();
        });
        dropzone.addEventListener('dragover', (e) => {
            e.preventDefault();
            dropzone.style.borderColor = 'var(--vn-primary)';
        });
        dropzone.addEventListener('dragleave', () => {
            dropzone.style.borderColor = '';
        });
        dropzone.addEventListener('drop', (e) => {
            e.preventDefault();
            dropzone.style.borderColor = '';
            const file = e.dataTransfer?.files?.[0];
            if (file) {
                if (file.path && safeBridge.isAvailable) {
                    safeBridge.loadVideoPath(file.path);
                    return;
                }
                const url = URL.createObjectURL(file);
                handleVideoLoaded({
                    fileName: file.name,
                    duration: 0,
                    width: 1920,
                    height: 1080,
                    mediaUrl: url
                });
                showToast(`Đã nhận: ${file.name}`);
                return;
            }
            safeBridge.selectVideo();
        });
    }

    const btnChange = document.getElementById('btnChangeVideo');
    if (btnChange) {
        btnChange.addEventListener('click', (e) => {
            e.preventDefault();
            e.stopPropagation();
            safeBridge.selectVideo();
        });
    }

    // Native file input listener fallback
    const nativeFileInput = document.getElementById('nativeFileInput');
    if (nativeFileInput) {
        nativeFileInput.addEventListener('change', (e) => {
            const file = e.target.files && e.target.files[0];
            if (file) {
                const url = URL.createObjectURL(file);
                handleVideoLoaded({
                    fileName: file.name,
                    duration: 0,
                    width: 1920,
                    height: 1080,
                    mediaUrl: url
                });
                showToast(`Đã chọn: ${file.name}`);
            }
        });
    }

    // Next to Step 2
    document.getElementById('btnNextToStep2')?.addEventListener('click', () => goToStep(2));

    // STEP 2: Scripting
    document.getElementById('personaSelect')?.addEventListener('change', (e) => {
        updatePersonaHighlight(e.target.value);
    });

    // Quick topic suggestion chips
    document.querySelectorAll('.quick-tag-chip').forEach(chip => {
        chip.addEventListener('click', () => {
            const topicInput = document.getElementById('topicInput');
            if (!topicInput) return;
            const chosenTopic = chip.getAttribute('data-topic') || chip.textContent;
            if (chip.classList.contains('active')) {
                chip.classList.remove('active');
                topicInput.value = '';
            } else {
                document.querySelectorAll('.quick-tag-chip').forEach(c => c.classList.remove('active'));
                chip.classList.add('active');
                topicInput.value = chosenTopic;
            }
        });
    });

    document.getElementById('topicInput')?.addEventListener('input', (e) => {
        const val = e.target.value.trim();
        document.querySelectorAll('.quick-tag-chip').forEach(c => {
            if (c.getAttribute('data-topic') === val) {
                c.classList.add('active');
            } else {
                c.classList.remove('active');
            }
        });
    });

    document.getElementById('btnGenerateScriptAction')?.addEventListener('click', () => {
        const topic = document.getElementById('topicInput')?.value.trim() || "";
        const persona = document.getElementById('personaSelect')?.value || "the_gioi_co_gi_hay";
        const style = document.getElementById('stylePresetSelect')?.value || "TikTok Shorts";
        safeBridge.generateScript(topic, persona, style);
    });

    document.getElementById('scriptInput')?.addEventListener('input', (e) => {
        isScriptDirtyByUser = true;
        updateWordCount(e.target.value);
    });

    document.getElementById('hookInput')?.addEventListener('input', () => {
        isScriptDirtyByUser = true;
    });

    const btnClearScript = document.getElementById('btnClearScript');
    if (btnClearScript) {
        btnClearScript.addEventListener('click', () => {
            const sEl = document.getElementById('scriptInput');
            if (sEl) sEl.value = "";
            const hEl = document.getElementById('hookInput');
            if (hEl) hEl.value = "";
            updateWordCount("");
            isScriptDirtyByUser = false;
            safeBridge.clearScript();
            showToast("Đã xóa sạch kịch bản cũ! AI sẽ viết mới hoàn toàn khi tạo video.");
        });
    }

    document.getElementById('btnBackToStep1')?.addEventListener('click', () => goToStep(1));
    document.getElementById('btnNextToStep3')?.addEventListener('click', () => goToStep(3));

    // STEP 3: Voice
    document.getElementById('rangeSpeechRate')?.addEventListener('input', (e) => {
        const val = parseFloat(e.target.value);
        const valEl = document.getElementById('valSpeechRate');
        if (valEl) valEl.textContent = `${val.toFixed(2)}x`;
        safeBridge.setSpeechRate(val);
        const scriptVal = document.getElementById('scriptInput')?.value;
        if (scriptVal) updateWordCount(scriptVal);
    });
    document.getElementById('rangeSpeechPitch')?.addEventListener('input', (e) => {
        const p = parseFloat(e.target.value);
        const valEl = document.getElementById('valSpeechPitch');
        if (valEl) valEl.textContent = `${p >= 0 ? '+' : ''}${p.toFixed(1)}`;
        safeBridge.setSpeechPitch(p);
    });

    document.getElementById('btnPreviewVoice')?.addEventListener('click', () => {
        const voice = document.getElementById('ttsVoiceSelect')?.value || "vi-VN-NamMinhNeural (Nam - Tiếng Việt)";
        const rate = parseFloat(document.getElementById('rangeSpeechRate')?.value || "1.20");
        const pitch = parseFloat(document.getElementById('rangeSpeechPitch')?.value || "2.0");
        safeBridge.generateVoice(voice, rate, pitch);
    });

    document.getElementById('btnBackToStep2')?.addEventListener('click', () => goToStep(2));
    document.getElementById('btnNextToStep4')?.addEventListener('click', () => goToStep(4));

    // STEP 4: Auto Edit & Generate
    document.getElementById('btnBackToStep3')?.addEventListener('click', () => goToStep(3));
    const resSelect = document.getElementById('resolutionSelect');
    if (resSelect) {
        resSelect.addEventListener('change', (e) => {
            updateAspectRatioUI(e.target.value);
            safeBridge.setResolution(e.target.value);
        });
    }

    // Start Generation Action Handlers
    document.getElementById('btnSidebarGenerate')?.addEventListener('click', triggerVideoCreation);
    document.getElementById('btnBigCreateVideo')?.addEventListener('click', triggerVideoCreation);
    document.getElementById('btnPreviewColGenerate')?.addEventListener('click', triggerVideoCreation);

    // Cancel Process
    document.getElementById('btnCancelPipeline').addEventListener('click', () => safeBridge.cancelProcess());

    // STEP 5: Result Actions
    document.getElementById('btnPlayResultVideo').addEventListener('click', () => safeBridge.openFinalVideo());
    document.getElementById('btnExportResultVideo').addEventListener('click', () => {
        safeBridge.openOutputFolder();
        showToast("Video đã được lưu sẵn trong thư mục xuất bản!");
    });
    document.getElementById('btnRestartWorkflow').addEventListener('click', () => {
        // Xóa sạch kịch bản cũ và reset toàn bộ dự án để làm lại từ đầu
        const scriptEl = document.getElementById('scriptInput');
        if (scriptEl) scriptEl.value = "";
        const hookEl = document.getElementById('hookInput');
        if (hookEl) hookEl.value = "";
        const topicEl = document.getElementById('topicInput');
        if (topicEl) topicEl.value = "";
        updateWordCount("");
        isScriptDirtyByUser = false;
        safeBridge.clearScript();
        safeBridge.resetProjectState();

        resetPlayerToEmptyState();
        resetChecklist();
        hideProgressOverlay();
        goToStep(1);
        showToast("Đã làm mới dự án, toàn bộ kịch bản cũ đã được xóa sạch để tạo từ đầu!");
    });

    // Player Controls
    const videoPlayer = document.getElementById('videoPlayer');
    document.getElementById('btnPlayPause').addEventListener('click', togglePlayPause);
    videoPlayer.addEventListener('timeupdate', updatePlayerTime);
    videoPlayer.addEventListener('loadedmetadata', onVideoLoadedMeta);
    videoPlayer.addEventListener('ended', onVideoEnded);
    document.getElementById('seekBar').addEventListener('input', (e) => {
        if (videoPlayer.duration) {
            videoPlayer.currentTime = (e.target.value / 100) * videoPlayer.duration;
        }
    });
    document.getElementById('btnMute').addEventListener('click', toggleMute);
    document.getElementById('btnFullscreen').addEventListener('click', toggleFullscreen);

    // Preview size expand / collapse toggle (Mặc định phóng to)
    const btnToggleZoom = document.getElementById('btnToggleZoomPreview');
    if (btnToggleZoom) {
        const card = document.getElementById('previewStageCard');
        const icon = document.getElementById('iconZoomPreview');
        const savedZoom = localStorage.getItem('vn_preview_expanded');
        // Mặc định luôn là phóng to (true) nếu chưa lưu hoặc nếu được thiết lập là '1'
        const isInitiallyExpanded = savedZoom !== null ? savedZoom === '1' : true;

        if (isInitiallyExpanded) {
            card?.classList.add('preview-expanded');
            if (icon) setSvgUse(icon, 'zoom-out');
            btnToggleZoom.title = 'Thu nhỏ khung xem trước';
        } else {
            card?.classList.remove('preview-expanded');
            if (icon) setSvgUse(icon, 'zoom-in');
            btnToggleZoom.title = 'Phóng to khung xem trước';
        }

        btnToggleZoom.addEventListener('click', () => {
            const isExpanded = card.classList.toggle('preview-expanded');
            localStorage.setItem('vn_preview_expanded', isExpanded ? '1' : '0');
            if (icon) {
                setSvgUse(icon, isExpanded ? 'zoom-out' : 'zoom-in');
            }
            btnToggleZoom.title = isExpanded ? 'Thu nhỏ khung xem trước' : 'Phóng to khung xem trước';
            showToast(isExpanded ? "Đã phóng to tối đa khung xem trước" : "Đã chuyển về kích thước chuẩn");
        });
    }

    // Error close
    document.getElementById('btnCloseError').addEventListener('click', hideError);
}

// ==================== PIPELINE EXECUTION ====================

function triggerVideoCreation() {
    if (isGenerating) return;

    // Nếu người dùng không tự tay gõ/sửa kịch bản (ví dụ kịch bản cũ còn sót lại từ lần chạy trước):
    // Xóa sạch để AI tạo mới 100% kịch bản từ đầu phù hợp chuẩn xác với video!
    let scriptToSend = isScriptDirtyByUser ? document.getElementById('scriptInput').value.trim() : "";
    let hookToSend = isScriptDirtyByUser ? document.getElementById('hookInput').value.trim() : "";

    const options = {
        topic: document.getElementById('topicInput').value.trim(),
        personaId: document.getElementById('personaSelect').value,
        stylePreset: document.getElementById('stylePresetSelect').value,
        durationMode: document.getElementById('durationModeSelect').value,
        resolution: document.getElementById('resolutionSelect') ? document.getElementById('resolutionSelect').value : "1080x1920 (9:16 Dọc TikTok/Shorts)",
        hook: hookToSend,
        script: scriptToSend,
        voice: document.getElementById('ttsVoiceSelect').value,
        speechRate: parseFloat(document.getElementById('rangeSpeechRate').value),
        speechPitch: parseFloat(document.getElementById('rangeSpeechPitch').value),
        autoCut: document.getElementById('chkAutoCut').checked,
        autoPan: document.getElementById('chkAutoPan').checked,
        autoZoom: document.getElementById('chkAutoZoom').checked,
        autoSubtitle: document.getElementById('chkAutoSubtitle').checked,
        keywordHighlight: document.getElementById('chkKeywordHighlight').checked,
        musicDucking: document.getElementById('chkMusicDucking').checked,
        soundEffects: document.getElementById('chkSoundEffects').checked
    };

    setProcessing(true, true);
    showProgressOverlay();
    safeBridge.createVideo(options);
}

// ==================== CHECKLIST & PROGRESS HELPERS ====================

function showProgressOverlay() {
    document.getElementById('pipelineProgressOverlay').classList.remove('hidden');
}

function hideProgressOverlay() {
    document.getElementById('pipelineProgressOverlay').classList.add('hidden');
}

function updateProgressPercentage(pct) {
    const val = Math.min(100, Math.max(0, Math.round(pct)));
    document.getElementById('pipelinePercentage').textContent = `${val}%`;
    document.getElementById('pipelineTrackFill').style.width = `${val}%`;

    // Real backend pipeline dynamic checklist sync
    if (val < 15) {
        activateChecklistStep(1);
    } else if (val < 35) {
        markChecklistDone(1);
        activateChecklistStep(2);
    } else if (val < 55) {
        markChecklistDone(1);
        markChecklistDone(2);
        activateChecklistStep(3);
    } else if (val < 75) {
        markChecklistDone(1);
        markChecklistDone(2);
        markChecklistDone(3);
        activateChecklistStep(4);
    } else if (val < 90) {
        markChecklistDone(1);
        markChecklistDone(2);
        markChecklistDone(3);
        markChecklistDone(4);
        activateChecklistStep(5);
    } else if (val < 100) {
        markChecklistDone(1);
        markChecklistDone(2);
        markChecklistDone(3);
        markChecklistDone(4);
        markChecklistDone(5);
        activateChecklistStep(6);
    } else {
        for (let i = 1; i <= 6; i++) markChecklistDone(i);
    }
}

function updateStatusText(text) {
    if (!text) return;
    document.getElementById('aiStatusText').textContent = text;
    document.getElementById('pipelineSubstatus').textContent = text;
}

function resetChecklist() {
    for (let i = 1; i <= 6; i++) {
        const item = document.getElementById(`pipeStep${i}`);
        if (item) {
            item.classList.remove('active', 'done');
            const icon = item.querySelector('.pipe-icon');
            if (icon) setSvgUse(icon, 'circle', 'pipe-icon');
        }
    }
}

function activateChecklistStep(idx) {
    const item = document.getElementById(`pipeStep${idx}`);
    if (item) {
        item.classList.add('active');
        const icon = item.querySelector('.pipe-icon');
        if (icon) setSvgUse(icon, 'spinner', 'spin-anim pipe-icon');
    }
}

function markChecklistDone(idx) {
    const item = document.getElementById(`pipeStep${idx}`);
    if (item) {
        item.classList.remove('active');
        item.classList.add('done');
        const icon = item.querySelector('.pipe-icon');
        if (icon) setSvgUse(icon, 'check-circle', 'pipe-icon');
    }
}

function setProcessing(isBusy, canCancel = true) {
    isGenerating = isBusy;
    if (!isBusy) {
        hideProgressOverlay();
    }
    document.getElementById('btnSidebarGenerate').disabled = isBusy;
    document.getElementById('btnBigCreateVideo').disabled = isBusy;
    document.getElementById('btnPreviewColGenerate').disabled = isBusy;

    const chip = document.getElementById('aiStatusChip');
    const dot = chip.querySelector('.status-indicator-dot');
    if (isBusy) {
        dot.style.backgroundColor = 'var(--vn-warning)';
        dot.style.boxShadow = '0 0 6px var(--vn-warning)';
        updateStatusText("Đang xử lý video AI...");
    } else {
        dot.style.backgroundColor = 'var(--vn-success)';
        dot.style.boxShadow = '0 0 6px var(--vn-success)';
        updateStatusText("Sẵn sàng tạo video");
    }
}

// ==================== MEDIA & PLAYER HANDLING ====================

function resetPlayerToEmptyState() {
    const video = document.getElementById('videoPlayer');
    const placeholder = document.getElementById('previewEmptyState');
    const badge = document.getElementById('videoBadgeOverlay');
    if (video) {
        video.pause();
        video.removeAttribute('src');
        video.load();
        video.classList.add('hidden');
    }
    if (placeholder) placeholder.classList.remove('hidden');
    if (badge) badge.classList.add('hidden');
    const timeDisplay = document.getElementById('timecodeDisplay');
    if (timeDisplay) timeDisplay.textContent = '00:00 / 00:00';
    const seekBar = document.getElementById('seekBar');
    if (seekBar) seekBar.value = 0;
    const icon = document.getElementById('iconPlayPause');
    if (icon) setSvgUse(icon, 'play');
}

function handleVideoLoaded(data) {
    currentVideoData = data;
    document.getElementById('videoInfoCard').classList.remove('hidden');
    document.getElementById('dropzoneVideo').classList.add('hidden');

    document.getElementById('videoFilename').textContent = data.fileName || "video.mp4";
    document.getElementById('metaDuration').innerHTML = `<svg class="vn-icon"><use href="#icon-clock"></use></svg> ${formatTime(data.duration || 0)}`;
    document.getElementById('metaResolution').innerHTML = `<svg class="vn-icon"><use href="#icon-monitor"></use></svg> ${data.width || 1920}x${data.height || 1080}`;
    document.getElementById('step1Desc').textContent = `${(data.duration || 0).toFixed(0)}s • Đã nạp`;

    // Cập nhật tùy chọn Giữ nguyên gốc với kích thước thực tế của video
    if (data.width && data.height) {
        const resSelect = document.getElementById('resolutionSelect');
        if (resSelect) {
            for (let i = 0; i < resSelect.options.length; i++) {
                if (resSelect.options[i].value.includes('Original') || resSelect.options[i].value.includes('gốc')) {
                    resSelect.options[i].text = `Giữ nguyên gốc - ${data.width}x${data.height} (Không cắt viền)`;
                    break;
                }
            }
        }
    }

    // Không phát video gốc khi mới tải lên theo yêu cầu người dùng
    resetPlayerToEmptyState();

    // Tự động xóa sạch kịch bản cũ khi nạp video mới để bắt đầu từ đầu
    const scriptEl = document.getElementById('scriptInput');
    if (scriptEl) {
        scriptEl.value = "";
        updateWordCount("");
    }
    const hookEl = document.getElementById('hookInput');
    if (hookEl) hookEl.value = "";
    isScriptDirtyByUser = false;

    // Cập nhật gợi ý trong khung preview rỗng
    const emptyDesc = document.querySelector('.empty-state-desc');
    if (emptyDesc) {
        emptyDesc.innerHTML = `Video gốc <strong>${data.fileName || "video.mp4"}</strong> đã sẵn sàng.<br>Nhấn <strong>"TẠO VIDEO"</strong> để xem kết quả hoàn thiện tại đây.`;
    }

    showToast("Đã tải video vào hệ thống thành công");
}

function handlePreviewReady(data) {
    if (data.mediaUrl) {
        const resSelect = document.getElementById('resolutionSelect');
        const aspectText = resSelect ? resSelect.value.split(' ')[0] : "Proxy";
        loadVideoToPlayer(data.mediaUrl, `Bản xem trước (${aspectText})`);
        showToast("Bản xem trước video đã sẵn sàng!");
    }
}

function handleFinalVideoReady(data) {
    hideProgressOverlay();
    setProcessing(false, false);
    if (data.mediaUrl) {
        currentResultVideoUrl = data.mediaUrl;
        loadVideoToPlayer(data.mediaUrl, "Video Hoàn Thiện 1080p");
        goToStep(5);
        document.getElementById('step5Desc').textContent = "Đã hoàn thành!";

        if (data.fileName || data.path) {
            const name = data.fileName || data.path.split(/[/\\]/).pop();
            const titleEl = document.getElementById('resVideoTitle');
            if (titleEl) titleEl.textContent = name;
        }
        if (data.duration && data.duration > 0) {
            const durTag = document.getElementById('resDurationTag');
            if (durTag) durTag.innerHTML = `<svg class="vn-icon"><use href="#icon-clock"></use></svg> ${formatTime(data.duration)}`;
        }
    }
}

function loadVideoToPlayer(url, badgeText) {
    const video = document.getElementById('videoPlayer');
    const placeholder = document.getElementById('previewEmptyState');
    const badge = document.getElementById('videoBadgeOverlay');
    const badgeTextEl = document.getElementById('videoBadgeText');

    video.src = url;
    video.classList.remove('hidden');
    placeholder.classList.add('hidden');
    badge.classList.remove('hidden');
    badgeTextEl.textContent = badgeText || "9:16 Preview";

    video.load();
    video.play().catch(() => {});
}

function togglePlayPause() {
    const video = document.getElementById('videoPlayer');
    const icon = document.getElementById('iconPlayPause');
    if (video.paused) {
        video.play();
        if (icon) setSvgUse(icon, 'pause');
    } else {
        video.pause();
        if (icon) setSvgUse(icon, 'play');
    }
}

function updatePlayerTime() {
    const video = document.getElementById('videoPlayer');
    const readout = document.getElementById('timecodeDisplay');
    const seek = document.getElementById('seekBar');
    if (!video.duration) return;

    readout.textContent = `${formatTime(video.currentTime)} / ${formatTime(video.duration)}`;
    seek.value = (video.currentTime / video.duration) * 100;
}

function onVideoLoadedMeta() {
    const video = document.getElementById('videoPlayer');
    document.getElementById('timecodeDisplay').textContent = `00:00 / ${formatTime(video.duration || 0)}`;
}

function onVideoEnded() {
    const icon = document.getElementById('iconPlayPause');
    if (icon) setSvgUse(icon, 'play');
}

function toggleMute() {
    const video = document.getElementById('videoPlayer');
    const icon = document.getElementById('iconVolume');
    video.muted = !video.muted;
    if (icon) setSvgUse(icon, video.muted ? 'speaker-slash' : 'speaker');
}

function toggleFullscreen() {
    const box = document.getElementById('previewViewportBox');
    if (!document.fullscreenElement) {
        box.requestFullscreen().catch(() => {});
    } else {
        document.exitFullscreen().catch(() => {});
    }
}

// ==================== UI HELPERS ====================

function updateWordCount(script) {
    const counter = document.getElementById('scriptWordCounter');
    if (!script) {
        counter.textContent = "0 từ (~0s)";
        return;
    }
    const words = script.trim().split(/\s+/).filter(w => w.length > 0).length;
    const rateEl = document.getElementById('rangeSpeechRate');
    const rate = rateEl ? parseFloat(rateEl.value) : 1.20;
    const wordsPerSec = 4.15 * (rate > 0 ? rate : 1.20);
    const estSec = Math.round(words / wordsPerSec);
    counter.textContent = `${words} từ (~${estSec}s)`;
}

function showError(msg) {
    hideProgressOverlay();
    setProcessing(false, false);
    document.getElementById('errorMessageText').textContent = msg || "Đã xảy ra lỗi!";
    document.getElementById('errorBanner').classList.remove('hidden');
}

function hideError() {
    document.getElementById('errorBanner').classList.add('hidden');
}

let toastTimer = null;
function showToast(msg) {
    const toast = document.getElementById('toastMessage');
    document.getElementById('toastText').textContent = msg;
    toast.classList.remove('hidden');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => {
        toast.classList.add('hidden');
    }, 3200);
}

function formatTime(seconds) {
    if (isNaN(seconds) || seconds < 0) return "00:00";
    const mins = Math.floor(seconds / 60);
    const secs = Math.floor(seconds % 60);
    return `${String(mins).padStart(2, '0')}:${String(secs).padStart(2, '0')}`;
}

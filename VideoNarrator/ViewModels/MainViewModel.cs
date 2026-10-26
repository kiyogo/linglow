using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VideoNarrator.Helpers;
using VideoNarrator.Models;
using VideoNarrator.Services;
using VideoNarrator.Services.PersonalityEngine;

namespace VideoNarrator.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly IGeminiService _geminiService;
        private readonly ITtsService _ttsService;
        private readonly IFfmpegService _ffmpegService;
        private readonly IVideoService _videoService;
        private readonly ISettingsService _settingsService;
        private readonly IFileService _fileService;

        // Auto Editing Engine Services
        private readonly ISceneDetectionService _sceneDetectionService;
        private readonly IEditingPlanService _editingPlanService;
        private readonly ISubtitleService _subtitleService;
        private readonly IAudioMixService _audioMixService;
        private readonly IBRollService _bRollService;
        private readonly IProxyVideoService _proxyVideoService;
        private readonly IVideoComposerService _videoComposerService;
        private readonly ITopicToVideoService? _topicToVideoService;
        private readonly IProjectService? _projectService;
        private readonly IVideoProbeService? _videoProbeService;
        private readonly IVideoDownloadService? _videoDownloadService;
        private readonly IMultiStageNarrationEngine? _multiStageNarrationEngine;

        private VideoProject? _currentProject;
        public VideoProject? CurrentProject
        {
            get => _currentProject;
            set => SetProperty(ref _currentProject, value);
        }

        // AI Video Script Personality Engine Services
        private readonly IPersonaManager _personaManager;
        private readonly IStyleEngine _styleEngine;
        private readonly IFewShotManager _fewShotManager;
        private readonly ISystemPromptBuilder _promptBuilder;
        private readonly IPromptVersionManager _promptVersionManager;
        private readonly IScriptGenerator _scriptGenerator;
        private readonly IStyleValidator _styleValidator;

        private CancellationTokenSource? _cts;

        // Core UI Binding Properties
        private string _geminiApiUrl = "http://192.168.1.15:6660/api";
        private string _geminiApiKey = string.Empty;
        private bool _isGeminiKeyVisible;
        private string _selectedGeminiModel = "gemini-3.1-flash-lite";
        private string _geminiTestStatus = "Chưa kiểm tra";
        private string _customTopic = string.Empty;

        private string _selectedTtsProvider = "Edge-TTS";
        private string _ttsApiKey = string.Empty;
        private bool _isTtsKeyVisible;
        private string _selectedTtsVoice = "vi-VN-NamMinhNeural (Nam - Tiếng Việt)";
        private double _speechRate = 1.20;
        private double _speechPitch = 2.0;
        private string _selectedTtsEmotionStyle = "🤖 Nhấn nhá cảm xúc tự nhiên (Đề xuất)";
        private string _ttsTestStatus = "Chưa kiểm tra";
        private MediaPlayer? _ttsPlayer;

        private string _selectedVideoPath = string.Empty;
        private VideoInfo? _videoInfo;
        private bool _hasVideo;

        private bool _enableBackgroundMusic;
        private string _backgroundMusicPath = string.Empty;
        private double _backgroundMusicVolume = 0.10;
        private string _selectedAudioHandlingMode = "Thay thế âm thanh gốc";

        private string _scriptText = string.Empty;
        private string _estimatedScriptDuration = "0 giây";

        private double _progressPercentage;
        private string _statusMessage = "Sẵn sàng";
        private string _errorMessage = string.Empty;
        private bool _hasError;
        private bool _isBusy;
        private bool _canCancel;
        private bool _isAnalyzing;
        private bool _isGenerating;

        private string _outputDirectory = string.Empty;
        private string _finalVideoPath = string.Empty;
        private bool _hasFinalVideo;
        private string _finalVideoDuration = string.Empty;

        // Auto Editing Engine Properties
        private bool _isAutoEditEnabled = true;
        private string _selectedStylePreset = "TikTok Shorts";
        private string _selectedResolution = "1080x1920 (9:16 Dọc TikTok/Shorts)";
        private string _selectedSubtitlePreset = "TikTok (Từng từ hiện lên - Đẹp nhất)";

        private bool _enableSceneDetection = true;
        private bool _enableAutoCut = true;
        private bool _enableAutoZoom = true;
        private bool _enableAutoPan = true;
        private bool _enableAutoSubtitle = true;
        private bool _enableKeywordHighlight = true;
        private bool _enableAutoTransition = true;
        private bool _enableAutoBRoll = false;
        private string _bRollFolderPath = string.Empty;
        private bool _enableMusicDucking = true;
        private string _musicFolderPath = string.Empty;
        private bool _enableSoundEffects = false;
        private string _sfxFolderPath = string.Empty;

        private EditingPlan? _currentEditingPlan;
        private bool _hasEditingPlan;
        private string _hookText = string.Empty;
        private string? _cachedSubtitlePath;
        private string? _cachedMixedAudioPath;

        // Observable Collections
        public ObservableCollection<string> GeminiModels { get; } = new()
        {
            "gemini-3.1-flash-lite",
            "fbb127bbb056c959",
            "9d8ca3786ebdfbea",
            "5bf011840784117a",
            "e051ce1aa80aa576"
        };

        public ObservableCollection<string> NarrationTones { get; } = new()
        {
            "🌍 Thế Giới Có Gì Hay? (Sự Thật & Mẹo Độc Lạ)",
            "🤖 Tự Động Phân Tích (Theo nội dung video)",
            "🔥 Hài hước, Bắt trend TikTok (Dí dỏm, chêm tiếng Anh)",
            "⚡ Kịch tính, Gay cấn (Hồi hộp, dồn dập)",
            "🎙️ Review sắc sảo, Chân thực (Bình luận tình huống)",
            "✨ Cảm xúc, Truyền cảm hứng (Sâu lắng, ấm áp)",
            "💡 Khám phá, Bí ẩn & Kỳ thú (Cuốn hút, tò mò)"
        };

        public ObservableCollection<string> TtsProviders { get; } = new()
        {
            "Edge-TTS",
            "OpenAI",
            "ElevenLabs"
        };

        public ObservableCollection<string> TtsVoices { get; } = new();

        public ObservableCollection<string> TtsEmotionStyles { get; } = new()
        {
            "🤖 Nhấn nhá cảm xúc tự nhiên (Đề xuất)",
            "🔥 Sôi nổi & Bắt trend (Hài hước / Shorts)",
            "☕ Trầm ấm & Truyền cảm (Podcast / Review)",
            "⚡ Tốc độ cao (Dồn dập / Bóc phốt nhanh)",
            "🎙️ Tiêu chuẩn (Đều đặn / Tin tức)"
        };

        public ObservableCollection<string> AudioHandlingModes { get; } = new()
        {
            "Thay thế âm thanh gốc",
            "Giữ âm thanh gốc + voice",
            "Tắt âm thanh gốc"
        };

        public ObservableCollection<string> StylePresets { get; } = new()
        {
            "Thế Giới Có Gì Hay",
            "TikTok Shorts",
            "Documentary",
            "News",
            "Educational",
            "Military/Technology"
        };

        public ObservableCollection<string> Resolutions { get; } = new()
        {
            "1080x1920 (9:16 Dọc TikTok/Shorts)",
            "720x1280 (9:16 Dọc HD)",
            "2160x3840 (9:16 Dọc 4K)",
            "1920x1080 (16:9 Ngang YouTube/Facebook)",
            "1280x720 (16:9 Ngang HD)",
            "1080x1080 (1:1 Vuông Instagram)",
            "Original (Giữ nguyên kích thước video gốc)"
        };

        public ObservableCollection<string> SubtitlePresets { get; } = new()
        {
            "TikTok (Từng từ hiện lên - Đẹp nhất)",
            "TikTok Karaoke (Sáng chữ theo giọng)",
            "Alex Hormozi (1-2 từ to nổi bật)",
            "Documentary (Phim tài liệu)",
            "News (Bản tin)",
            "Clean (Tối giản)"
        };

        public ObservableCollection<string> DurationModes { get; } = new()
        {
            "🤖 Tự Động Thông Minh (Thuật toán tự quyết định)",
            "🎬 Giữ nguyên toàn bộ video gốc (Không cắt)",
            "⚡ Cắt ghép Highlight (Khớp 100% giọng đọc)"
        };

        public ObservableCollection<VideoScene> DetectedScenes { get; } = new();
        public ObservableCollection<SubtitleSegment> SubtitleList { get; } = new();

        // Getters and Setters
        public string GeminiApiUrl
        {
            get => _geminiApiUrl;
            set { if (SetProperty(ref _geminiApiUrl, value)) { OnPropertyChanged(nameof(IsApiKeyRequired)); SaveSettings(); } }
        }

        public bool IsApiKeyRequired => !string.IsNullOrWhiteSpace(GeminiApiUrl) && GeminiApiUrl.Contains("googleapis.com");

        public string GeminiApiKey
        {
            get => _geminiApiKey;
            set { if (SetProperty(ref _geminiApiKey, value)) SaveSettings(); }
        }

        public bool IsGeminiKeyVisible
        {
            get => _isGeminiKeyVisible;
            set => SetProperty(ref _isGeminiKeyVisible, value);
        }

        public string SelectedGeminiModel
        {
            get => _selectedGeminiModel;
            set { if (SetProperty(ref _selectedGeminiModel, value)) SaveSettings(); }
        }

        public string GeminiTestStatus
        {
            get => _geminiTestStatus;
            set => SetProperty(ref _geminiTestStatus, value);
        }

        public string CustomTopic
        {
            get => _customTopic;
            set => SetProperty(ref _customTopic, value);
        }

        public string SelectedTtsProvider
        {
            get => _selectedTtsProvider;
            set
            {
                if (SetProperty(ref _selectedTtsProvider, value))
                {
                    UpdateVoiceList();
                    SaveSettings();
                }
            }
        }

        public string TtsApiKey
        {
            get => _ttsApiKey;
            set { if (SetProperty(ref _ttsApiKey, value)) SaveSettings(); }
        }

        public bool IsTtsKeyVisible
        {
            get => _isTtsKeyVisible;
            set => SetProperty(ref _isTtsKeyVisible, value);
        }

        public string SelectedTtsVoice
        {
            get => _selectedTtsVoice;
            set { if (SetProperty(ref _selectedTtsVoice, value)) SaveSettings(); }
        }

        public double SpeechRate
        {
            get => _speechRate;
            set { if (SetProperty(ref _speechRate, Math.Round(value, 2))) SaveSettings(); }
        }

        public double SpeechPitch
        {
            get => _speechPitch;
            set { if (SetProperty(ref _speechPitch, Math.Round(value, 1))) SaveSettings(); }
        }

        public string SelectedTtsEmotionStyle
        {
            get => _selectedTtsEmotionStyle;
            set { if (SetProperty(ref _selectedTtsEmotionStyle, value)) SaveSettings(); }
        }

        public string TtsTestStatus
        {
            get => _ttsTestStatus;
            set => SetProperty(ref _ttsTestStatus, value);
        }

        public string SelectedVideoPath
        {
            get => _selectedVideoPath;
            set => SetProperty(ref _selectedVideoPath, value);
        }

        public VideoInfo? VideoInfo
        {
            get => _videoInfo;
            set => SetProperty(ref _videoInfo, value);
        }

        public bool HasVideo
        {
            get => _hasVideo;
            set => SetProperty(ref _hasVideo, value);
        }

        public bool EnableBackgroundMusic
        {
            get => _enableBackgroundMusic;
            set { if (SetProperty(ref _enableBackgroundMusic, value)) SaveSettings(); }
        }

        public string BackgroundMusicPath
        {
            get => _backgroundMusicPath;
            set { if (SetProperty(ref _backgroundMusicPath, value)) SaveSettings(); }
        }

        public double BackgroundMusicVolume
        {
            get => _backgroundMusicVolume;
            set { if (SetProperty(ref _backgroundMusicVolume, Math.Round(value, 2))) SaveSettings(); }
        }

        public string SelectedAudioHandlingMode
        {
            get => _selectedAudioHandlingMode;
            set { if (SetProperty(ref _selectedAudioHandlingMode, value)) SaveSettings(); }
        }

        public string ScriptText
        {
            get => _scriptText;
            set { if (SetProperty(ref _scriptText, value)) UpdateEstimatedDuration(); }
        }

        public string EstimatedScriptDuration
        {
            get => _estimatedScriptDuration;
            set => SetProperty(ref _estimatedScriptDuration, value);
        }

        public double ProgressPercentage
        {
            get => _progressPercentage;
            set => SetProperty(ref _progressPercentage, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public bool HasError
        {
            get => _hasError;
            set => SetProperty(ref _hasError, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool CanCancel
        {
            get => _canCancel;
            set => SetProperty(ref _canCancel, value);
        }

        public bool IsAnalyzing
        {
            get => _isAnalyzing;
            set => SetProperty(ref _isAnalyzing, value);
        }

        public bool IsGenerating
        {
            get => _isGenerating;
            set => SetProperty(ref _isGenerating, value);
        }

        public string OutputDirectory
        {
            get => _outputDirectory;
            set { if (SetProperty(ref _outputDirectory, value)) SaveSettings(); }
        }

        public string FinalVideoPath
        {
            get => _finalVideoPath;
            set => SetProperty(ref _finalVideoPath, value);
        }

        public bool HasFinalVideo
        {
            get => _hasFinalVideo;
            set => SetProperty(ref _hasFinalVideo, value);
        }

        public string FinalVideoDuration
        {
            get => _finalVideoDuration;
            set => SetProperty(ref _finalVideoDuration, value);
        }

        // Auto Edit Feature Properties
        public bool IsAutoEditEnabled
        {
            get => _isAutoEditEnabled;
            set { if (SetProperty(ref _isAutoEditEnabled, value)) SaveSettings(); }
        }

        public string SelectedStylePreset
        {
            get => _selectedStylePreset;
            set { if (SetProperty(ref _selectedStylePreset, value)) SaveSettings(); }
        }

        public string SelectedResolution
        {
            get => _selectedResolution;
            set { if (SetProperty(ref _selectedResolution, value)) SaveSettings(); }
        }

        public string SelectedSubtitlePreset
        {
            get => _selectedSubtitlePreset;
            set { if (SetProperty(ref _selectedSubtitlePreset, value)) SaveSettings(); }
        }

        private string _selectedDurationMode = "🤖 Tự Động Thông Minh (Thuật toán tự quyết định)";
        public string SelectedDurationMode
        {
            get => _selectedDurationMode;
            set { if (SetProperty(ref _selectedDurationMode, value)) SaveSettings(); }
        }

        private string _selectedNarrationTone = "🤖 Tự Động Phân Tích (Theo nội dung video)";
        public string SelectedNarrationTone
        {
            get => _selectedNarrationTone;
            set { if (SetProperty(ref _selectedNarrationTone, value)) SaveSettings(); }
        }

        private string _aiTrainingInstruction = "🔥 Táo bạo, nói thẳng nói thật 100%, bóc phốt hài hước, chêm từ Gen Z, trong video có gì nói đó không giữ kẽ";
        public string AiTrainingInstruction
        {
            get => _aiTrainingInstruction;
            set { if (SetProperty(ref _aiTrainingInstruction, value)) SaveSettings(); }
        }

        public bool EnableSceneDetection
        {
            get => _enableSceneDetection;
            set { if (SetProperty(ref _enableSceneDetection, value)) SaveSettings(); }
        }

        public bool EnableAutoCut
        {
            get => _enableAutoCut;
            set { if (SetProperty(ref _enableAutoCut, value)) SaveSettings(); }
        }

        public bool EnableAutoZoom
        {
            get => _enableAutoZoom;
            set { if (SetProperty(ref _enableAutoZoom, value)) SaveSettings(); }
        }

        public bool EnableAutoPan
        {
            get => _enableAutoPan;
            set { if (SetProperty(ref _enableAutoPan, value)) SaveSettings(); }
        }

        public bool EnableAutoSubtitle
        {
            get => _enableAutoSubtitle;
            set { if (SetProperty(ref _enableAutoSubtitle, value)) SaveSettings(); }
        }

        public bool EnableKeywordHighlight
        {
            get => _enableKeywordHighlight;
            set { if (SetProperty(ref _enableKeywordHighlight, value)) SaveSettings(); }
        }

        public bool EnableAutoTransition
        {
            get => _enableAutoTransition;
            set { if (SetProperty(ref _enableAutoTransition, value)) SaveSettings(); }
        }

        public bool EnableAutoBRoll
        {
            get => _enableAutoBRoll;
            set { if (SetProperty(ref _enableAutoBRoll, value)) SaveSettings(); }
        }

        public string BRollFolderPath
        {
            get => _bRollFolderPath;
            set { if (SetProperty(ref _bRollFolderPath, value)) SaveSettings(); }
        }

        public bool EnableMusicDucking
        {
            get => _enableMusicDucking;
            set { if (SetProperty(ref _enableMusicDucking, value)) SaveSettings(); }
        }

        public string MusicFolderPath
        {
            get => _musicFolderPath;
            set { if (SetProperty(ref _musicFolderPath, value)) SaveSettings(); }
        }

        public bool EnableSoundEffects
        {
            get => _enableSoundEffects;
            set { if (SetProperty(ref _enableSoundEffects, value)) SaveSettings(); }
        }

        public string SfxFolderPath
        {
            get => _sfxFolderPath;
            set { if (SetProperty(ref _sfxFolderPath, value)) SaveSettings(); }
        }

        public EditingPlan? CurrentEditingPlan
        {
            get => _currentEditingPlan;
            set => SetProperty(ref _currentEditingPlan, value);
        }

        public bool HasEditingPlan
        {
            get => _hasEditingPlan;
            set => SetProperty(ref _hasEditingPlan, value);
        }

        public string HookText
        {
            get => _hookText;
            set => SetProperty(ref _hookText, value);
        }

        private int _selectedTabIndex = 0;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set => SetProperty(ref _selectedTabIndex, value);
        }

        // Action hooks for Video Player view
        public Action? RequestVideoPlay { get; set; }
        public Action? RequestVideoPause { get; set; }
        public Action? RequestVideoStop { get; set; }
        public Action<string>? RequestLoadVideoPreview { get; set; }

        // Commands
        public ICommand SwitchToStudioCommand { get; }
        public ICommand SwitchToSettingsCommand { get; }
        public ICommand SelectVideoCommand { get; }
        public ICommand SelectMusicCommand { get; }
        public ICommand SelectBRollFolderCommand { get; }
        public ICommand SelectSfxFolderCommand { get; }
        public ICommand TestGeminiCommand { get; }
        public ICommand TestTtsCommand { get; }
        public ICommand AnalyzeVideoCommand { get; }
        public ICommand GenerateNarrationCommand { get; }
        public ICommand AutoEditPipelineCommand { get; }
        public ICommand RenderFinalShortCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand SelectOutputDirectoryCommand { get; }
        public ICommand OpenOutputDirectoryCommand { get; }
        public ICommand PlayVideoCommand { get; }
        public ICommand PauseVideoCommand { get; }
        public ICommand StopVideoCommand { get; }
        public ICommand ToggleGeminiKeyVisibilityCommand { get; }
        public ICommand ToggleTtsKeyVisibilityCommand { get; }

        private string _videoUrlInput = string.Empty;
        public string VideoUrlInput
        {
            get => _videoUrlInput;
            set => SetProperty(ref _videoUrlInput, value);
        }

        public ICommand DownloadAndAutoProduceCommand { get; }
        public ICommand DownloadVideoOnlyCommand { get; }

        // ==================== PERSONA STUDIO PROPERTIES ====================
        public ObservableCollection<PersonaProfile> Personas { get; } = new();

        private PersonaProfile? _selectedPersona;
        public PersonaProfile? SelectedPersona
        {
            get => _selectedPersona;
            set
            {
                if (SetProperty(ref _selectedPersona, value))
                {
                    if (value != null)
                    {
                        _personaManager.SetActivePersona(value.Id);
                        LoadVersionsForSelectedPersona();
                        RefreshCompiledPromptView();

                        if (value.Id == "doi_co_gi_hay")
                        {
                            SelectedTtsVoice = "vi-VN-NamMinhNeural (Nam - Tiếng Việt)";
                            SelectedAudioHandlingMode = "Giữ âm thanh gốc + voice";
                            SelectedDurationMode = "🤖 Tự Động Thông Minh (Thuật toán tự quyết định)";
                        }
                    }
                    OnPropertyChanged(nameof(CuriosityLevel));
                    OnPropertyChanged(nameof(DramaticLevel));
                    OnPropertyChanged(nameof(WeirdnessLevel));
                    OnPropertyChanged(nameof(SarcasmLevel));
                    OnPropertyChanged(nameof(VisualLanguageLevel));
                    OnPropertyChanged(nameof(HookIntensity));
                    OnPropertyChanged(nameof(SuspenseLevel));
                    OnPropertyChanged(nameof(SatisfactionLevel));
                    OnPropertyChanged(nameof(SentenceRhythm));
                    OnPropertyChanged(nameof(NarrationSpeed));
                    OnPropertyChanged(nameof(EndingStyle));
                    OnPropertyChanged(nameof(Verbosity));
                    OnPropertyChanged(nameof(PersonaSystemPromptOverride));
                }
            }
        }

        private string _newPersonaName = string.Empty;
        public string NewPersonaName
        {
            get => _newPersonaName;
            set => SetProperty(ref _newPersonaName, value);
        }

        public int CuriosityLevel
        {
            get => SelectedPersona?.StyleConfig.CuriosityLevel ?? 85;
            set
            {
                if (SelectedPersona != null && SelectedPersona.StyleConfig.CuriosityLevel != value)
                {
                    SelectedPersona.StyleConfig.CuriosityLevel = value;
                    OnPropertyChanged();
                    _personaManager.UpdatePersona(SelectedPersona);
                    RefreshCompiledPromptView();
                }
            }
        }

        public int DramaticLevel
        {
            get => SelectedPersona?.StyleConfig.DramaticLevel ?? 75;
            set
            {
                if (SelectedPersona != null && SelectedPersona.StyleConfig.DramaticLevel != value)
                {
                    SelectedPersona.StyleConfig.DramaticLevel = value;
                    OnPropertyChanged();
                    _personaManager.UpdatePersona(SelectedPersona);
                    RefreshCompiledPromptView();
                }
            }
        }

        public int WeirdnessLevel
        {
            get => SelectedPersona?.StyleConfig.WeirdnessLevel ?? 60;
            set
            {
                if (SelectedPersona != null && SelectedPersona.StyleConfig.WeirdnessLevel != value)
                {
                    SelectedPersona.StyleConfig.WeirdnessLevel = value;
                    OnPropertyChanged();
                    _personaManager.UpdatePersona(SelectedPersona);
                    RefreshCompiledPromptView();
                }
            }
        }

        public int SarcasmLevel
        {
            get => SelectedPersona?.StyleConfig.SarcasmLevel ?? 40;
            set
            {
                if (SelectedPersona != null && SelectedPersona.StyleConfig.SarcasmLevel != value)
                {
                    SelectedPersona.StyleConfig.SarcasmLevel = value;
                    OnPropertyChanged();
                    _personaManager.UpdatePersona(SelectedPersona);
                    RefreshCompiledPromptView();
                }
            }
        }

        public int VisualLanguageLevel
        {
            get => SelectedPersona?.StyleConfig.VisualLanguageLevel ?? 90;
            set
            {
                if (SelectedPersona != null && SelectedPersona.StyleConfig.VisualLanguageLevel != value)
                {
                    SelectedPersona.StyleConfig.VisualLanguageLevel = value;
                    OnPropertyChanged();
                    _personaManager.UpdatePersona(SelectedPersona);
                    RefreshCompiledPromptView();
                }
            }
        }

        public int HookIntensity
        {
            get => SelectedPersona?.StyleConfig.HookIntensity ?? 95;
            set
            {
                if (SelectedPersona != null && SelectedPersona.StyleConfig.HookIntensity != value)
                {
                    SelectedPersona.StyleConfig.HookIntensity = value;
                    OnPropertyChanged();
                    _personaManager.UpdatePersona(SelectedPersona);
                    RefreshCompiledPromptView();
                }
            }
        }

        public int SuspenseLevel
        {
            get => SelectedPersona?.StyleConfig.SuspenseLevel ?? 80;
            set
            {
                if (SelectedPersona != null && SelectedPersona.StyleConfig.SuspenseLevel != value)
                {
                    SelectedPersona.StyleConfig.SuspenseLevel = value;
                    OnPropertyChanged();
                    _personaManager.UpdatePersona(SelectedPersona);
                    RefreshCompiledPromptView();
                }
            }
        }

        public int SatisfactionLevel
        {
            get => SelectedPersona?.StyleConfig.SatisfactionLevel ?? 85;
            set
            {
                if (SelectedPersona != null && SelectedPersona.StyleConfig.SatisfactionLevel != value)
                {
                    SelectedPersona.StyleConfig.SatisfactionLevel = value;
                    OnPropertyChanged();
                    _personaManager.UpdatePersona(SelectedPersona);
                    RefreshCompiledPromptView();
                }
            }
        }

        public string SentenceRhythm
        {
            get => SelectedPersona?.StyleConfig.SentenceRhythm ?? "alternating";
            set
            {
                if (SelectedPersona != null && SelectedPersona.StyleConfig.SentenceRhythm != value)
                {
                    SelectedPersona.StyleConfig.SentenceRhythm = value;
                    OnPropertyChanged();
                    _personaManager.UpdatePersona(SelectedPersona);
                    RefreshCompiledPromptView();
                }
            }
        }

        public string NarrationSpeed
        {
            get => SelectedPersona?.StyleConfig.NarrationSpeed ?? "rapid_1.2x";
            set
            {
                if (SelectedPersona != null && SelectedPersona.StyleConfig.NarrationSpeed != value)
                {
                    SelectedPersona.StyleConfig.NarrationSpeed = value;
                    OnPropertyChanged();
                    _personaManager.UpdatePersona(SelectedPersona);
                    RefreshCompiledPromptView();
                }
            }
        }

        public string EndingStyle
        {
            get => SelectedPersona?.StyleConfig.EndingStyle ?? "mind_bending";
            set
            {
                if (SelectedPersona != null && SelectedPersona.StyleConfig.EndingStyle != value)
                {
                    SelectedPersona.StyleConfig.EndingStyle = value;
                    OnPropertyChanged();
                    _personaManager.UpdatePersona(SelectedPersona);
                    RefreshCompiledPromptView();
                }
            }
        }

        public string Verbosity
        {
            get => SelectedPersona?.StyleConfig.Verbosity ?? "balanced";
            set
            {
                if (SelectedPersona != null && SelectedPersona.StyleConfig.Verbosity != value)
                {
                    SelectedPersona.StyleConfig.Verbosity = value;
                    OnPropertyChanged();
                    _personaManager.UpdatePersona(SelectedPersona);
                    RefreshCompiledPromptView();
                }
            }
        }

        public string PersonaSystemPromptOverride
        {
            get => SelectedPersona?.SystemPromptOverride ?? string.Empty;
            set
            {
                if (SelectedPersona != null && SelectedPersona.SystemPromptOverride != value)
                {
                    SelectedPersona.SystemPromptOverride = value;
                    OnPropertyChanged();
                    _personaManager.UpdatePersona(SelectedPersona);
                    RefreshCompiledPromptView();
                }
            }
        }

        public ObservableCollection<string> SentenceRhythmOptions { get; } = new()
        {
            "alternating",
            "rapid_fire",
            "slow_burn",
            "syncopated"
        };

        public ObservableCollection<string> EndingStyleOptions { get; } = new()
        {
            "mind_bending",
            "punchline",
            "loop_seamless",
            "cliffhanger",
            "call_to_action",
            "payoff_climax"
        };

        public ObservableCollection<string> VerbosityOptions { get; } = new()
        {
            "concise",
            "balanced",
            "detailed"
        };

        public ObservableCollection<string> NarrationSpeedOptions { get; } = new()
        {
            "rapid_1.2x",
            "fast_1.1x",
            "normal_1.0x"
        };

        // Few-Shot Examples
        public ObservableCollection<FewShotExample> FewShotExamples { get; } = new();

        private FewShotExample? _selectedFewShotExample;
        public FewShotExample? SelectedFewShotExample
        {
            get => _selectedFewShotExample;
            set => SetProperty(ref _selectedFewShotExample, value);
        }

        private string _newExampleTopic = string.Empty;
        public string NewExampleTopic
        {
            get => _newExampleTopic;
            set => SetProperty(ref _newExampleTopic, value);
        }

        private string _newExamplePositive = string.Empty;
        public string NewExamplePositive
        {
            get => _newExamplePositive;
            set => SetProperty(ref _newExamplePositive, value);
        }

        private string _newExampleNegative = string.Empty;
        public string NewExampleNegative
        {
            get => _newExampleNegative;
            set => SetProperty(ref _newExampleNegative, value);
        }

        private string _newExampleExplanation = string.Empty;
        public string NewExampleExplanation
        {
            get => _newExampleExplanation;
            set => SetProperty(ref _newExampleExplanation, value);
        }

        // Testing Arena
        public ObservableCollection<string> OutputModes { get; } = new()
        {
            "script_only",
            "structured_script",
            "video_generation_prompt",
            "full_video_package"
        };

        private string _selectedOutputMode = "script_only";
        public string SelectedOutputMode
        {
            get => _selectedOutputMode;
            set
            {
                if (SetProperty(ref _selectedOutputMode, value))
                {
                    RefreshCompiledPromptView();
                }
            }
        }

        private string _testTopic = "Cắt xà phòng cát động lực chuyển màu cực đã mắt";
        public string TestTopic
        {
            get => _testTopic;
            set
            {
                if (SetProperty(ref _testTopic, value))
                {
                    RefreshCompiledPromptView();
                }
            }
        }

        private double _testDurationSeconds = 30.0;
        public double TestDurationSeconds
        {
            get => _testDurationSeconds;
            set
            {
                if (SetProperty(ref _testDurationSeconds, value))
                {
                    RefreshCompiledPromptView();
                }
            }
        }

        private string _testGeneratedOutput = string.Empty;
        public string TestGeneratedOutput
        {
            get => _testGeneratedOutput;
            set => SetProperty(ref _testGeneratedOutput, value);
        }

        private string _previousTestOutput = string.Empty;
        public string PreviousTestOutput
        {
            get => _previousTestOutput;
            set => SetProperty(ref _previousTestOutput, value);
        }

        private bool _hasPreviousTestOutput;
        public bool HasPreviousTestOutput
        {
            get => _hasPreviousTestOutput;
            set => SetProperty(ref _hasPreviousTestOutput, value);
        }

        private int _testQualityScore = 100;
        public int TestQualityScore
        {
            get => _testQualityScore;
            set => SetProperty(ref _testQualityScore, value);
        }

        private string _testValidationSummary = "Sẵn sàng thử nghiệm Persona";
        public string TestValidationSummary
        {
            get => _testValidationSummary;
            set => SetProperty(ref _testValidationSummary, value);
        }

        private int _testTokensUsed;
        public int TestTokensUsed
        {
            get => _testTokensUsed;
            set => SetProperty(ref _testTokensUsed, value);
        }

        private long _testDurationMs;
        public long TestDurationMs
        {
            get => _testDurationMs;
            set => SetProperty(ref _testDurationMs, value);
        }

        private bool _isTestingPrompt;
        public bool IsTestingPrompt
        {
            get => _isTestingPrompt;
            set => SetProperty(ref _isTestingPrompt, value);
        }

        // Compiled System Prompt Viewer
        private string _compiledSystemPromptView = string.Empty;
        public string CompiledSystemPromptView
        {
            get => _compiledSystemPromptView;
            set => SetProperty(ref _compiledSystemPromptView, value);
        }

        private int _compiledEstimatedTokens;
        public int CompiledEstimatedTokens
        {
            get => _compiledEstimatedTokens;
            set => SetProperty(ref _compiledEstimatedTokens, value);
        }

        private double _compiledEstimatedCost;
        public double CompiledEstimatedCost
        {
            get => _compiledEstimatedCost;
            set => SetProperty(ref _compiledEstimatedCost, value);
        }

        // Prompt Versions
        public ObservableCollection<PromptVersion> PromptVersions { get; } = new();

        private PromptVersion? _selectedPromptVersion;
        public PromptVersion? SelectedPromptVersion
        {
            get => _selectedPromptVersion;
            set => SetProperty(ref _selectedPromptVersion, value);
        }

        private string _versionNote = string.Empty;
        public string VersionNote
        {
            get => _versionNote;
            set => SetProperty(ref _versionNote, value);
        }

        // Persona Studio Commands
        public ICommand SwitchToPersonaStudioCommand { get; }
        public ICommand CreatePersonaCommand { get; }
        public ICommand DuplicatePersonaCommand { get; }
        public ICommand DeletePersonaCommand { get; }
        public ICommand SetDefaultPersonaCommand { get; }
        public ICommand ResetPersonasToDefaultCommand { get; }
        public ICommand SavePersonaCommand { get; }
        public ICommand AddFewShotExampleCommand { get; }
        public ICommand DeleteFewShotExampleCommand { get; }
        public ICommand TestPromptCommand { get; }
        public ICommand ApplyTestScriptToStudioCommand { get; }
        public ICommand RefreshCompiledPromptCommand { get; }
        public ICommand SavePromptVersionCommand { get; }
        public ICommand RestorePromptVersionCommand { get; }
        public ICommand DeletePromptVersionCommand { get; }

        public MainViewModel(
            IGeminiService geminiService,
            ITtsService ttsService,
            IFfmpegService ffmpegService,
            IVideoService videoService,
            ISettingsService settingsService,
            IFileService fileService,
            ISceneDetectionService sceneDetectionService,
            IEditingPlanService editingPlanService,
            ISubtitleService subtitleService,
            IAudioMixService audioMixService,
            IBRollService bRollService,
            IProxyVideoService proxyVideoService,
            IVideoComposerService videoComposerService,
            IPersonaManager personaManager,
            IStyleEngine styleEngine,
            IFewShotManager fewShotManager,
            ISystemPromptBuilder promptBuilder,
            IPromptVersionManager promptVersionManager,
            IScriptGenerator scriptGenerator,
            IStyleValidator styleValidator,
            ITopicToVideoService? topicToVideoService = null,
            IProjectService? projectService = null,
            IVideoProbeService? videoProbeService = null,
            IVideoDownloadService? videoDownloadService = null,
            IMultiStageNarrationEngine? multiStageNarrationEngine = null)
        {
            _geminiService = geminiService;
            _ttsService = ttsService;
            _ffmpegService = ffmpegService;
            _videoService = videoService;
            _settingsService = settingsService;
            _fileService = fileService;
            _sceneDetectionService = sceneDetectionService;
            _editingPlanService = editingPlanService;
            _subtitleService = subtitleService;
            _audioMixService = audioMixService;
            _bRollService = bRollService;
            _proxyVideoService = proxyVideoService;
            _videoComposerService = videoComposerService;
            _personaManager = personaManager;
            _styleEngine = styleEngine;
            _fewShotManager = fewShotManager;
            _promptBuilder = promptBuilder;
            _promptVersionManager = promptVersionManager;
            _scriptGenerator = scriptGenerator;
            _styleValidator = styleValidator;
            _topicToVideoService = topicToVideoService;
            _projectService = projectService;
            _videoProbeService = videoProbeService;
            _videoDownloadService = videoDownloadService;
            _multiStageNarrationEngine = multiStageNarrationEngine;

            // Initialize commands
            SwitchToStudioCommand = new RelayCommand(() => SelectedTabIndex = 0);
            SwitchToSettingsCommand = new RelayCommand(() => SelectedTabIndex = 1);
            SwitchToPersonaStudioCommand = new RelayCommand(() => SelectedTabIndex = 2);

            DownloadAndAutoProduceCommand = new RelayCommand(async () => await DownloadAndAutoProduceAsync(), () => !IsBusy);
            DownloadVideoOnlyCommand = new RelayCommand(async () => await DownloadVideoOnlyAsync(), () => !IsBusy);

            SelectVideoCommand = new RelayCommand(ExecuteSelectVideo, () => !IsBusy);
            SelectMusicCommand = new RelayCommand(ExecuteSelectMusic, () => !IsBusy);
            SelectBRollFolderCommand = new RelayCommand(ExecuteSelectBRollFolder, () => !IsBusy);
            SelectSfxFolderCommand = new RelayCommand(ExecuteSelectSfxFolder, () => !IsBusy);
            TestGeminiCommand = new RelayCommand(async () => await ExecuteTestGeminiAsync(), () => !IsBusy);
            TestTtsCommand = new RelayCommand(async () => await ExecuteTestTtsAsync(), () => !IsBusy);
            AnalyzeVideoCommand = new RelayCommand(async () => await ExecuteAnalyzeVideoAsync(), () => !IsBusy && HasVideo);
            GenerateNarrationCommand = new RelayCommand(async () => await ExecuteGenerateNarrationAsync(), () => !IsBusy && !string.IsNullOrWhiteSpace(ScriptText));
            AutoEditPipelineCommand = new RelayCommand(async () => await ExecuteAutoEditPipelineAsync(), () => !IsBusy && HasVideo);
            RenderFinalShortCommand = new RelayCommand(async () => await ExecuteRenderFinalShortAsync(), () => !IsBusy && HasVideo && HasEditingPlan);
            CancelCommand = new RelayCommand(ExecuteCancel, () => IsBusy && CanCancel);
            SelectOutputDirectoryCommand = new RelayCommand(ExecuteSelectOutputDirectory, () => !IsBusy);
            OpenOutputDirectoryCommand = new RelayCommand(ExecuteOpenOutputDirectory);
            PlayVideoCommand = new RelayCommand(() => RequestVideoPlay?.Invoke());
            PauseVideoCommand = new RelayCommand(() => RequestVideoPause?.Invoke());
            StopVideoCommand = new RelayCommand(() => RequestVideoStop?.Invoke());
            ToggleGeminiKeyVisibilityCommand = new RelayCommand(() => IsGeminiKeyVisible = !IsGeminiKeyVisible);
            ToggleTtsKeyVisibilityCommand = new RelayCommand(() => IsTtsKeyVisible = !IsTtsKeyVisible);

            // Persona Studio Commands
            CreatePersonaCommand = new RelayCommand(ExecuteCreatePersona, () => !IsBusy);
            DuplicatePersonaCommand = new RelayCommand(ExecuteDuplicatePersona, () => !IsBusy && SelectedPersona != null);
            DeletePersonaCommand = new RelayCommand(ExecuteDeletePersona, () => !IsBusy && SelectedPersona != null && !SelectedPersona.IsBuiltIn);
            SetDefaultPersonaCommand = new RelayCommand(ExecuteSetDefaultPersona, () => !IsBusy && SelectedPersona != null);
            ResetPersonasToDefaultCommand = new RelayCommand(ExecuteResetPersonasToDefault, () => !IsBusy);
            SavePersonaCommand = new RelayCommand(ExecuteSavePersona, () => !IsBusy && SelectedPersona != null);
            AddFewShotExampleCommand = new RelayCommand(ExecuteAddFewShotExample, () => !IsBusy);
            DeleteFewShotExampleCommand = new RelayCommand(ExecuteDeleteFewShotExample, () => !IsBusy && SelectedFewShotExample != null);
            TestPromptCommand = new RelayCommand(async () => await ExecuteTestPromptAsync(), () => !IsBusy && !IsTestingPrompt);
            ApplyTestScriptToStudioCommand = new RelayCommand(ExecuteApplyTestScriptToStudio, () => !string.IsNullOrWhiteSpace(TestGeneratedOutput));
            RefreshCompiledPromptCommand = new RelayCommand(RefreshCompiledPromptView);
            SavePromptVersionCommand = new RelayCommand(ExecuteSavePromptVersion, () => SelectedPersona != null);
            RestorePromptVersionCommand = new RelayCommand(ExecuteRestorePromptVersion, () => SelectedPromptVersion != null);
            DeletePromptVersionCommand = new RelayCommand(ExecuteDeletePromptVersion, () => SelectedPromptVersion != null);

            LoadSavedSettings();
            UpdateVoiceList();
            InitializePersonaStudio();
        }

        private void LoadSavedSettings()
        {
            var s = _settingsService.CurrentSettings;
            _geminiApiUrl = !string.IsNullOrWhiteSpace(s.GeminiApiUrl) ? s.GeminiApiUrl : "http://192.168.1.15:6660/api";
            _geminiApiKey = string.Empty;
            _selectedGeminiModel = (string.IsNullOrWhiteSpace(s.GeminiModel) || s.GeminiModel.StartsWith("gemini-2.") || s.GeminiModel.StartsWith("gemini-1.")) ? "gemini-3.1-flash-lite" : s.GeminiModel;
            _selectedTtsProvider = string.IsNullOrWhiteSpace(s.TtsProvider) ? "Edge-TTS" : s.TtsProvider;
            _ttsApiKey = s.TtsApiKey;
            _selectedTtsVoice = string.IsNullOrWhiteSpace(s.TtsVoice) ? "vi-VN-NamMinhNeural (Nam - Tiếng Việt)" : s.TtsVoice;
            _speechRate = s.SpeechRate > 0 ? s.SpeechRate : 1.20;
            _speechPitch = s.SpeechPitch;
            _selectedTtsEmotionStyle = string.IsNullOrWhiteSpace(s.TtsEmotionStyle) ? "🤖 Nhấn nhá cảm xúc tự nhiên (Đề xuất)" : s.TtsEmotionStyle;
            _outputDirectory = string.IsNullOrWhiteSpace(s.OutputDirectory) ? AppSettings.GetDefaultOutputDirectory() : s.OutputDirectory;
            _enableBackgroundMusic = s.EnableBackgroundMusic;
            _backgroundMusicPath = s.BackgroundMusicPath;
            _backgroundMusicVolume = s.BackgroundMusicVolume > 0 ? s.BackgroundMusicVolume : 0.10;
            _selectedAudioHandlingMode = string.IsNullOrWhiteSpace(s.AudioHandlingMode) ? "Thay thế âm thanh gốc" : s.AudioHandlingMode;

            _isAutoEditEnabled = s.IsAutoEditEnabled;
            _selectedStylePreset = string.IsNullOrWhiteSpace(s.AutoEditStylePreset) ? "TikTok Shorts" : s.AutoEditStylePreset;
            _selectedResolution = (s.OutputResolution != null && Resolutions.Any(r => r.Contains(s.OutputResolution) || s.OutputResolution.Contains(r.Split(' ')[0])))
                ? Resolutions.First(r => r.Contains(s.OutputResolution) || s.OutputResolution.Contains(r.Split(' ')[0]))
                : "1080x1920 (9:16 Dọc TikTok/Shorts)";
            _selectedSubtitlePreset = (s.SubtitleStylePreset != null && SubtitlePresets.Contains(s.SubtitleStylePreset))
                ? s.SubtitleStylePreset
                : "TikTok (Từng từ hiện lên - Đẹp nhất)";
            _selectedDurationMode = (s.DurationMode != null && s.DurationMode.Contains("Không cắt"))
                ? "🎬 Giữ nguyên toàn bộ video gốc (Không cắt)"
                : ((s.DurationMode != null && s.DurationMode.Contains("Highlight"))
                    ? "⚡ Cắt ghép Highlight (Khớp 100% giọng đọc)"
                    : "🤖 Tự Động Thông Minh (Thuật toán tự quyết định)");
            _selectedNarrationTone = (s.NarrationTone != null && NarrationTones.Any(t => t.Contains(s.NarrationTone) || s.NarrationTone.Contains(t.Split(' ')[1])))
                ? NarrationTones.First(t => t.Contains(s.NarrationTone) || s.NarrationTone.Contains(t.Split(' ')[1]))
                : "🤖 Tự Động Phân Tích (Theo nội dung video)";
            _aiTrainingInstruction = string.IsNullOrWhiteSpace(s.AiTrainingInstruction)
                ? "🔥 Táo bạo, nói thẳng nói thật 100%, bóc phốt hài hước, chêm từ Gen Z, trong video có gì nói đó không giữ kẽ"
                : s.AiTrainingInstruction;
            _enableSceneDetection = s.EnableSceneDetection;
            _enableAutoCut = s.EnableAutoCut;
            _enableAutoZoom = s.EnableAutoZoom;
            _enableAutoPan = s.EnableAutoPan;
            _enableAutoSubtitle = s.EnableAutoSubtitle;
            _enableKeywordHighlight = s.EnableKeywordHighlight;
            _enableAutoTransition = s.EnableAutoTransition;
            _enableAutoBRoll = s.EnableAutoBRoll;
            _bRollFolderPath = s.BRollFolderPath;
            _enableMusicDucking = s.EnableMusicDucking;
            _musicFolderPath = s.MusicFolderPath;
            _enableSoundEffects = s.EnableSoundEffects;
            _sfxFolderPath = s.SfxFolderPath;
        }

        private void SaveSettings()
        {
            var s = new AppSettings
            {
                GeminiApiUrl = GeminiApiUrl,
                GeminiApiKey = string.Empty,
                GeminiModel = SelectedGeminiModel,
                TtsProvider = SelectedTtsProvider,
                TtsApiKey = TtsApiKey,
                TtsVoice = SelectedTtsVoice,
                SpeechRate = SpeechRate,
                SpeechPitch = SpeechPitch,
                TtsEmotionStyle = SelectedTtsEmotionStyle,
                OutputDirectory = OutputDirectory,
                EnableBackgroundMusic = EnableBackgroundMusic,
                BackgroundMusicPath = BackgroundMusicPath,
                BackgroundMusicVolume = BackgroundMusicVolume,
                AudioHandlingMode = SelectedAudioHandlingMode,
                IsAutoEditEnabled = IsAutoEditEnabled,
                AutoEditStylePreset = SelectedStylePreset,
                OutputResolution = SelectedResolution,
                SubtitleStylePreset = SelectedSubtitlePreset,
                DurationMode = SelectedDurationMode,
                NarrationTone = SelectedNarrationTone,
                AiTrainingInstruction = AiTrainingInstruction,
                EnableSceneDetection = EnableSceneDetection,
                EnableAutoCut = EnableAutoCut,
                EnableAutoZoom = EnableAutoZoom,
                EnableAutoPan = EnableAutoPan,
                EnableAutoSubtitle = EnableAutoSubtitle,
                EnableKeywordHighlight = EnableKeywordHighlight,
                EnableAutoTransition = EnableAutoTransition,
                EnableAutoBRoll = EnableAutoBRoll,
                BRollFolderPath = BRollFolderPath,
                EnableMusicDucking = EnableMusicDucking,
                MusicFolderPath = MusicFolderPath,
                EnableSoundEffects = EnableSoundEffects,
                SfxFolderPath = SfxFolderPath
            };
            _settingsService.SaveSettings(s);
        }

        private void UpdateVoiceList()
        {
            var voices = _ttsService.GetSupportedVoices(SelectedTtsProvider);
            TtsVoices.Clear();
            foreach (var v in voices)
            {
                TtsVoices.Add(v);
            }

            if (TtsVoices.Count > 0 && !TtsVoices.Contains(SelectedTtsVoice))
            {
                SelectedTtsVoice = TtsVoices[0];
            }
        }

        public async Task LoadVideoFileAsync(string filePath)
        {
            ClearError();
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                SetError("Tệp video không tồn tại.");
                return;
            }

            if (!_videoService.IsSupportedVideoExtension(filePath))
            {
                SetError("Định dạng video không được hỗ trợ. Vui lòng chọn .mp4, .mkv, .mov, .avi, .webm.");
                return;
            }

            try
            {
                StatusMessage = "Đang đọc thông tin video...";
                var info = await _videoService.GetVideoInfoAsync(filePath);
                VideoInfo = info;
                SelectedVideoPath = filePath;
                HasVideo = true;

                // Tự động xóa sạch kịch bản cũ và dữ liệu biên tập cũ để làm lại hoàn toàn từ đầu
                ScriptText = string.Empty;
                HookText = string.Empty;
                CurrentEditingPlan = null;
                HasEditingPlan = false;
                DetectedScenes.Clear();
                SubtitleList.Clear();

                StatusMessage = $"Đã tải video: {info.FileName} ({info.DurationFormatted})";
                _fileService.Log($"Đã nạp video mới: {filePath}. Đã làm mới kịch bản và kế hoạch dựng.");

                // Tự động phân tích bối cảnh âm thanh và khung hình trong nền để gợi ý chủ đề chính xác
                if (_videoProbeService != null)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var probe = await _videoProbeService.ProbeVideoAsync(filePath);
                            if (probe != null && probe.Success)
                            {
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    if (string.IsNullOrWhiteSpace(CustomTopic) && !string.IsNullOrWhiteSpace(probe.SuggestedTopic))
                                    {
                                        CustomTopic = probe.SuggestedTopic;
                                        StatusMessage = $"Đã nhận diện bối cảnh: {probe.SuggestedTopic}";
                                    }
                                });
                            }
                        }
                        catch { }
                    });
                }
            }
            catch (Exception ex)
            {
                SetError($"Lỗi đọc metadata video: {ex.Message}");
                _fileService.Log($"Lỗi nạp video: {filePath}", "ERROR", ex);
            }
        }

        private async void ExecuteSelectVideo()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Chọn tệp video để biên tập",
                Filter = "Video Files (*.mp4;*.mkv;*.mov;*.avi;*.webm)|*.mp4;*.mkv;*.mov;*.avi;*.webm|All Files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                await LoadVideoFileAsync(dialog.FileName);
            }
        }

        public void ResetProjectState()
        {
            ScriptText = string.Empty;
            HookText = string.Empty;
            CustomTopic = string.Empty;
            CurrentEditingPlan = null;
            HasEditingPlan = false;
            DetectedScenes.Clear();
            SubtitleList.Clear();
            VideoInfo = null;
            SelectedVideoPath = string.Empty;
            HasVideo = false;
            StatusMessage = "Đã làm mới toàn bộ dự án để bắt đầu từ đầu.";
            _fileService.Log("Đã xóa sạch kịch bản cũ và làm mới trạng thái dự án.");
        }

        private void ExecuteSelectMusic()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Chọn tệp nhạc nền",
                Filter = "Audio Files (*.mp3;*.wav;*.m4a;*.aac)|*.mp3;*.wav;*.m4a;*.aac|All Files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                BackgroundMusicPath = dialog.FileName;
            }
        }

        private void ExecuteSelectBRollFolder()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Chọn thư mục chứa B-Roll clips",
                InitialDirectory = BRollFolderPath
            };

            if (dialog.ShowDialog() == true)
            {
                BRollFolderPath = dialog.FolderName;
            }
        }

        private void ExecuteSelectSfxFolder()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Chọn thư mục chứa Sound Effects (SFX)",
                InitialDirectory = SfxFolderPath
            };

            if (dialog.ShowDialog() == true)
            {
                SfxFolderPath = dialog.FolderName;
            }
        }

        private void ExecuteSelectOutputDirectory()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Chọn thư mục lưu video kết quả",
                InitialDirectory = OutputDirectory
            };

            if (dialog.ShowDialog() == true)
            {
                OutputDirectory = dialog.FolderName;
            }
        }

        private void ExecuteOpenOutputDirectory()
        {
            try
            {
                string dir = string.IsNullOrWhiteSpace(OutputDirectory) ? AppSettings.GetDefaultOutputDirectory() : OutputDirectory;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                Process.Start(new ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                SetError($"Không thể mở thư mục: {ex.Message}");
            }
        }

        private async Task ExecuteTestGeminiAsync()
        {
            ClearError();
            if (IsApiKeyRequired && string.IsNullOrWhiteSpace(GeminiApiKey))
            {
                SetError("Vui lòng nhập Gemini API Key trước khi kiểm tra.");
                GeminiTestStatus = "✗ Thiếu API Key";
                return;
            }

            try
            {
                GeminiTestStatus = "Đang kiểm tra...";
                bool ok = await _geminiService.TestApiKeyAsync(GeminiApiKey, SelectedGeminiModel, GeminiApiUrl);
                GeminiTestStatus = ok ? "✓ Kết nối thành công" : "✗ Kết nối thất bại";
            }
            catch (Exception ex)
            {
                GeminiTestStatus = "✗ Lỗi kết nối";
                SetError($"Lỗi kiểm tra Gemini: {ex.Message}");
            }
        }

        public static string MapEmotionStyle(string uiStyle)
        {
            if (string.IsNullOrWhiteSpace(uiStyle)) return "Dynamic";
            if (uiStyle.Contains("Sôi nổi") || uiStyle.Contains("Bắt trend")) return "Cheerful";
            if (uiStyle.Contains("Trầm ấm")) return "Calm";
            if (uiStyle.Contains("Tốc độ")) return "FastPaced";
            if (uiStyle.Contains("Tiêu chuẩn")) return "Standard";
            return "Dynamic";
        }

        public TtsSettings BuildCurrentTtsSettings()
        {
            return new TtsSettings
            {
                Provider = SelectedTtsProvider,
                ApiKey = TtsApiKey,
                Voice = SelectedTtsVoice,
                Speed = SpeechRate,
                Pitch = SpeechPitch,
                EmotionStyle = MapEmotionStyle(SelectedTtsEmotionStyle)
            };
        }

        private async Task ExecuteTestTtsAsync()
        {
            ClearError();
            var settings = BuildCurrentTtsSettings();

            if ((SelectedTtsProvider == "OpenAI" || SelectedTtsProvider == "ElevenLabs") && string.IsNullOrWhiteSpace(TtsApiKey))
            {
                SetError($"Vui lòng nhập API Key cho {SelectedTtsProvider}.");
                TtsTestStatus = "✗ Thiếu API Key";
                return;
            }

            try
            {
                TtsTestStatus = "Đang tạo giọng đọc nhấn nhá...";
                string tempAudio = Path.Combine(Path.GetTempPath(), $"tts_test_{Guid.NewGuid():N}.mp3");
                string testSampleText = !string.IsNullOrWhiteSpace(HookText)
                    ? HookText.Trim()
                    : (!string.IsNullOrWhiteSpace(ScriptText)
                        ? (ScriptText.Split(new[] { '.', '!', '?', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "Xin chào, đây là giọng đọc thuyết minh AI của bạn.")
                        : "Xin chào, đây là giọng đọc thuyết minh AI của bạn.");
                await _ttsService.GenerateSpeechAsync(testSampleText, settings, tempAudio);

                if (File.Exists(tempAudio) && new FileInfo(tempAudio).Length > 0)
                {
                    TtsTestStatus = "✓ Đang phát nghe thử...";
                    PlayAudioPreview(tempAudio);
                }
                else
                {
                    TtsTestStatus = "✗ Không nhận được âm thanh";
                }
            }
            catch (Exception ex)
            {
                TtsTestStatus = "✗ Lỗi kết nối";
                SetError($"Lỗi kiểm tra TTS: {ex.Message}");
            }
        }

        private void PlayAudioPreview(string audioPath)
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _ttsPlayer?.Close();
                    _ttsPlayer = new MediaPlayer();
                    _ttsPlayer.Open(new Uri(audioPath));
                    _ttsPlayer.MediaEnded += (s, e) =>
                    {
                        TtsTestStatus = "✓ Nghe thử thành công";
                    };
                    _ttsPlayer.Play();
                });
            }
            catch (Exception ex)
            {
                _fileService.Log($"Không thể phát preview audio: {ex.Message}", "WARN");
            }
        }

        #region Classic Pipeline

        private async Task ExecuteAnalyzeVideoAsync()
        {
            ClearError();

            if (!HasVideo || VideoInfo == null || !File.Exists(SelectedVideoPath))
            {
                SetError("Vui lòng chọn video hợp lệ trước khi phân tích.");
                return;
            }

            if (IsApiKeyRequired && string.IsNullOrWhiteSpace(GeminiApiKey))
            {
                StatusMessage = "Chưa cấu hình Gemini API Key: Đang kích hoạt Smart Engine tự động tạo kịch bản...";
            }

            if (!_ffmpegService.IsFfmpegAvailable || !_ffmpegService.IsFfprobeAvailable)
            {
                SetError("Không tìm thấy FFmpeg/FFprobe. Vui lòng đảm bảo tệp nằm trong Assets\\ffmpeg.");
                return;
            }

            _cts = new CancellationTokenSource();
            IsBusy = true;
            CanCancel = true;
            IsAnalyzing = true;
            ProgressPercentage = 5;
            StatusMessage = "Bắt đầu phân tích video...";

            try
            {
                var result = await _geminiService.AnalyzeVideoAsync(
                    GeminiApiKey,
                    SelectedGeminiModel,
                    SelectedVideoPath,
                    VideoInfo.DurationSeconds,
                    GeminiApiUrl,
                    (status, prog) =>
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = status;
                            ProgressPercentage = prog;
                        });
                    },
                    _cts.Token);

                ScriptText = result.FormattedScriptText;
                ProgressPercentage = 100;
                StatusMessage = "Phân tích hoàn tất! Bạn có thể xem và chỉnh sửa kịch bản bên dưới.";
                _fileService.Log($"Phân tích video thành công cho '{VideoInfo.FileName}'.");
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Đã hủy thao tác phân tích video.";
                ProgressPercentage = 0;
            }
            catch (Exception ex)
            {
                SetError($"Lỗi phân tích video: {ex.Message}");
                StatusMessage = "Phân tích thất bại.";
                _fileService.Log($"Lỗi phân tích video: {ex.Message}", "ERROR", ex);
            }
            finally
            {
                IsBusy = false;
                CanCancel = false;
                IsAnalyzing = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        public async Task ExecuteGenerateScriptOnlyAsync()
        {
            ClearError();

            if (!HasVideo || VideoInfo == null || !File.Exists(SelectedVideoPath))
            {
                SetError("Vui lòng chọn video trước khi tạo kịch bản.");
                return;
            }

            _cts = new CancellationTokenSource();
            IsBusy = true;
            CanCancel = true;
            IsAnalyzing = true;
            ProgressPercentage = 10;
            StatusMessage = "AI đang phân tích video để viết kịch bản và câu Hook...";

            try
            {
                List<VideoScene> scenes = new();
                if (EnableSceneDetection)
                {
                    ProgressPercentage = 20;
                    StatusMessage = "Đang nhận diện các phân cảnh...";
                    scenes = await _sceneDetectionService.DetectScenesAsync(SelectedVideoPath, VideoInfo.DurationSeconds, _cts.Token);
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        DetectedScenes.Clear();
                        foreach (var sc in scenes) DetectedScenes.Add(sc);
                    });
                }

                ProgressPercentage = 40;
                StatusMessage = "AI đang sáng tạo kịch bản (áp dụng Persona & Mật độ câu từ)...";

                var currentSettings = _settingsService.CurrentSettings;
                if ((SelectedResolution.Contains("Original") || SelectedResolution.Contains("Gốc") || SelectedResolution.Contains("gốc")) && VideoInfo != null && VideoInfo.Width > 0 && VideoInfo.Height > 0)
                {
                    currentSettings.OutputResolution = $"Original ({VideoInfo.Width}x{VideoInfo.Height})";
                }
                else
                {
                    currentSettings.OutputResolution = SelectedResolution;
                }
                currentSettings.GeminiApiUrl = GeminiApiUrl;
                currentSettings.SpeechRate = SpeechRate;
                currentSettings.DurationMode = SelectedDurationMode;
                currentSettings.NarrationTone = SelectedNarrationTone;
                currentSettings.EnableAutoCut = EnableAutoCut;

                var plan = await _editingPlanService.GenerateEditingPlanAsync(
                    GeminiApiKey,
                    SelectedGeminiModel,
                    SelectedVideoPath,
                    VideoInfo.DurationSeconds,
                    SelectedStylePreset,
                    scenes,
                    currentSettings,
                    null, // generate fresh script
                    CustomTopic,
                    (st, p) =>
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = st;
                            ProgressPercentage = p;
                        });
                    },
                    _cts.Token);

                CurrentEditingPlan = plan;
                HasEditingPlan = true;
                HookText = plan.Hook;
                ScriptText = plan.NarrationScript;

                ProgressPercentage = 100;
                StatusMessage = "Đã tạo kịch bản xong! Bạn có thể chỉnh sửa kịch bản hoặc bấm 'Dựng Video 9:16'.";
                _fileService.Log($"Tạo kịch bản thành công cho '{VideoInfo.FileName}'.");
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Đã hủy tạo kịch bản.";
                ProgressPercentage = 0;
            }
            catch (Exception ex)
            {
                SetError($"Lỗi tạo kịch bản: {ex.Message}");
                StatusMessage = "Tạo kịch bản thất bại.";
                _fileService.Log($"Lỗi tạo kịch bản: {ex.Message}", "ERROR", ex);
            }
            finally
            {
                IsBusy = false;
                CanCancel = false;
                IsAnalyzing = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private async Task ExecuteGenerateNarrationAsync()
        {
            ClearError();

            if (string.IsNullOrWhiteSpace(ScriptText))
            {
                SetError("Kịch bản thuyết minh đang rỗng.");
                return;
            }

            if (!HasVideo || VideoInfo == null || !File.Exists(SelectedVideoPath))
            {
                SetError("Không tìm thấy tệp video gốc.");
                return;
            }

            if ((SelectedTtsProvider == "OpenAI" || SelectedTtsProvider == "ElevenLabs") && string.IsNullOrWhiteSpace(TtsApiKey))
            {
                SetError($"Vui lòng cung cấp API Key cho dịch vụ TTS ({SelectedTtsProvider}).");
                return;
            }

            _cts = new CancellationTokenSource();
            IsBusy = true;
            CanCancel = true;
            IsGenerating = true;
            ProgressPercentage = 5;
            StatusMessage = "Đang khởi tạo quá trình tạo lồng tiếng...";

            string tempDir = _fileService.CreateTempDirectory();
            string rawAudioPath = Path.Combine(tempDir, "narration_raw.mp3");
            string syncedAudioPath = Path.Combine(tempDir, "narration_synced.mp3");

            try
            {
                ProgressPercentage = 15;
                StatusMessage = $"Đang tạo giọng đọc thuyết minh ({SelectedTtsProvider})...";

                var ttsSettings = BuildCurrentTtsSettings();

                await _ttsService.GenerateSpeechAsync(ScriptText, ttsSettings, rawAudioPath, _cts.Token);

                double actualAudioDuration = await _ffmpegService.GetMediaDurationAsync(rawAudioPath, _cts.Token);
                if (actualAudioDuration <= 0) actualAudioDuration = 40.0;

                double outroPadding = 1.5;
                double speechAndOutro = actualAudioDuration + outroPadding;
                double targetDuration;

                if (SelectedDurationMode.Contains("Highlight") || SelectedDurationMode.Contains("Cắt ghép"))
                {
                    targetDuration = speechAndOutro;
                }
                else if (SelectedDurationMode.Contains("Toàn bộ") || SelectedDurationMode.Contains("Không cắt"))
                {
                    targetDuration = Math.Max(VideoInfo.DurationSeconds, speechAndOutro);
                }
                else
                {
                    // Tự Động Thông Minh: nếu giọng đọc dừng sớm hơn video gốc > 2.5s, kết thúc video tại speechAndOutro
                    double gap = VideoInfo.DurationSeconds - speechAndOutro;
                    targetDuration = (gap <= 2.5) ? Math.Max(VideoInfo.DurationSeconds, speechAndOutro) : Math.Min(VideoInfo.DurationSeconds, speechAndOutro);
                }

                bool isHighlightCut = targetDuration < VideoInfo.DurationSeconds - 1.0;

                ProgressPercentage = 45;
                StatusMessage = "Đang kiểm tra thời lượng âm thanh...";
                await _ffmpegService.AdjustAudioDurationAsync(rawAudioPath, syncedAudioPath, targetDuration, _cts.Token);

                string finalAudioToMerge = File.Exists(syncedAudioPath) ? syncedAudioPath : rawAudioPath;

                ProgressPercentage = 75;
                StatusMessage = isHighlightCut
                    ? $"Đang cắt ghép các đoạn quan trọng ({VideoInfo.DurationSeconds:F0}s -> {targetDuration:F0}s) khớp với bài thuyết minh..."
                    : "Đang tiến hành ghép audio vào video (FFmpeg)...";

                string outputDir = string.IsNullOrWhiteSpace(OutputDirectory) ? AppSettings.GetDefaultOutputDirectory() : OutputDirectory;
                string finalOutputPath = _fileService.GetUniqueOutputPath(outputDir, VideoInfo.FileName);

                string audioHandlingModeInternal = SelectedAudioHandlingMode switch
                {
                    "Giữ âm thanh gốc + voice" => "KeepOriginalAudio",
                    "Tắt âm thanh gốc" => "MuteOriginalAudio",
                    _ => "ReplaceOriginalAudio"
                };

                if (isHighlightCut)
                {
                    string topic = EditingPlanService.ExtractTopicFromFileName(SelectedVideoPath);
                    var highlightScenes = EditingPlanService.SelectHighlightScenes(VideoInfo.DurationSeconds, targetDuration, DetectedScenes.ToList(), topic);
                    var quickPlan = new EditingPlan
                    {
                        Scenes = highlightScenes,
                        OutputWidth = VideoInfo.Width > 0 ? VideoInfo.Width : 1080,
                        OutputHeight = VideoInfo.Height > 0 ? VideoInfo.Height : 1920,
                        FocusX = 0.5,
                        FocusY = 0.5
                    };
                    await _videoComposerService.ComposeVerticalShortAsync(
                        SelectedVideoPath,
                        finalAudioToMerge,
                        null,
                        quickPlan,
                        finalOutputPath,
                        cancellationToken: _cts.Token);
                }
                else
                {
                    await _ffmpegService.MergeVideoAndAudioAsync(
                        SelectedVideoPath,
                        finalAudioToMerge,
                        EnableBackgroundMusic ? BackgroundMusicPath : null,
                        BackgroundMusicVolume,
                        audioHandlingModeInternal,
                        finalOutputPath,
                        _cts.Token);
                }

                double finalDur = await _ffmpegService.GetMediaDurationAsync(finalOutputPath, _cts.Token);
                FinalVideoPath = finalOutputPath;
                HasFinalVideo = true;
                FinalVideoDuration = TimeSpan.FromSeconds(finalDur).ToString(@"hh\:mm\:ss\.ff");

                ProgressPercentage = 100;
                StatusMessage = "Xuất video thành công!";
                RequestLoadVideoPreview?.Invoke(finalOutputPath);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Đã hủy quá trình.";
                ProgressPercentage = 0;
            }
            catch (Exception ex)
            {
                SetError($"Lỗi tạo video lồng tiếng: {ex.Message}");
                StatusMessage = "Xử lý thất bại.";
                _fileService.Log($"Lỗi tạo lồng tiếng: {ex.Message}", "ERROR", ex);
            }
            finally
            {
                _fileService.DeleteTempDirectory(tempDir);
                IsBusy = false;
                CanCancel = false;
                IsGenerating = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        #endregion

        #region 1-Click Social Video Download & Auto Pipeline

        public async Task<string> DownloadVideoOnlyAsync(string? url = null)
        {
            string targetUrl = !string.IsNullOrWhiteSpace(url) ? url.Trim() : VideoUrlInput?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(targetUrl))
            {
                SetError("Vui lòng nhập đường link video (TikTok, YouTube Shorts, Facebook, Reels, Douyin, X...).");
                return string.Empty;
            }

            if (_videoDownloadService == null)
            {
                SetError("Dịch vụ tải video chưa được khởi tạo.");
                return string.Empty;
            }

            ClearError();
            IsBusy = true;
            CanCancel = true;
            _cts = new CancellationTokenSource();
            ProgressPercentage = 10;
            StatusMessage = "Đang tải video từ mạng xã hội về máy...";

            try
            {
                var downloadResult = await _videoDownloadService.DownloadVideoAsync(
                    targetUrl,
                    null,
                    (msg, pct) =>
                    {
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            StatusMessage = msg;
                            ProgressPercentage = pct;
                        });
                    },
                    _cts.Token);

                if (!downloadResult.Success || string.IsNullOrEmpty(downloadResult.FilePath) || !File.Exists(downloadResult.FilePath))
                {
                    throw new InvalidOperationException(downloadResult.ErrorMessage ?? "Không thể tải video từ đường link đã cung cấp.");
                }

                await LoadVideoFileAsync(downloadResult.FilePath);
                if (!string.IsNullOrWhiteSpace(downloadResult.Title) && string.IsNullOrWhiteSpace(CustomTopic))
                {
                    CustomTopic = downloadResult.Title;
                }

                StatusMessage = $"Đã tải video thành công: {downloadResult.Title ?? Path.GetFileName(downloadResult.FilePath)}";
                ProgressPercentage = 100;
                return downloadResult.FilePath;
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Đã hủy tải video.";
                return string.Empty;
            }
            catch (Exception ex)
            {
                SetError($"Lỗi khi tải video: {ex.Message}");
                StatusMessage = "Tải video thất bại.";
                return string.Empty;
            }
            finally
            {
                IsBusy = false;
                CanCancel = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        public async Task<string> DownloadAndAutoProduceAsync(string? url = null)
        {
            string targetUrl = !string.IsNullOrWhiteSpace(url) ? url.Trim() : VideoUrlInput?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(targetUrl))
            {
                SetError("Vui lòng nhập đường link video (TikTok, YouTube Shorts, Facebook, Reels, Douyin, X...).");
                return string.Empty;
            }

            if (_videoDownloadService == null)
            {
                SetError("Dịch vụ tải video chưa được khởi tạo.");
                return string.Empty;
            }

            ClearError();
            _fileService.Log($"[1-Click Pipeline] Bắt đầu tự động tải và dựng trọn gói video từ link: {targetUrl}");

            string downloadedFilePath = string.Empty;
            string videoTitle = string.Empty;

            // Bước 1: Tải video
            IsBusy = true;
            CanCancel = true;
            _cts = new CancellationTokenSource();
            ProgressPercentage = 5;
            StatusMessage = "⚡ [1-Click] Đang tải video từ mạng xã hội...";

            try
            {
                var downloadResult = await _videoDownloadService.DownloadVideoAsync(
                    targetUrl,
                    null,
                    (msg, pct) =>
                    {
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            StatusMessage = $"⚡ [1-Click] {msg}";
                            ProgressPercentage = Math.Min(35, pct * 0.35);
                        });
                    },
                    _cts.Token);

                if (!downloadResult.Success || string.IsNullOrEmpty(downloadResult.FilePath) || !File.Exists(downloadResult.FilePath))
                {
                    throw new InvalidOperationException(downloadResult.ErrorMessage ?? "Không thể tải video từ URL.");
                }

                downloadedFilePath = downloadResult.FilePath;
                videoTitle = downloadResult.Title ?? string.Empty;
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Đã hủy tiến trình 1-Click.";
                IsBusy = false;
                CanCancel = false;
                _cts?.Dispose();
                _cts = null;
                return string.Empty;
            }
            catch (Exception ex)
            {
                SetError($"[1-Click] Lỗi tải video: {ex.Message}");
                StatusMessage = "Tải video thất bại.";
                IsBusy = false;
                CanCancel = false;
                _cts?.Dispose();
                _cts = null;
                return string.Empty;
            }
            finally
            {
                IsBusy = false;
                CanCancel = false;
                _cts?.Dispose();
                _cts = null;
            }

            // Bước 2: Nạp video vào hệ thống
            await LoadVideoFileAsync(downloadedFilePath);
            if (!string.IsNullOrWhiteSpace(videoTitle) && string.IsNullOrWhiteSpace(CustomTopic))
            {
                CustomTopic = videoTitle;
            }

            // Bước 3: Tự động chạy Auto Edit Pipeline (Kể chuyện AI, TTS, Subtitles, Cắt cúp)
            StatusMessage = "⚡ [1-Click] Đang phân tích video và tạo kịch bản hấp dẫn...";
            await ExecuteAutoEditPipelineAsync();

            // Bước 4: Tự động render xuất file video 9:16 chất lượng cao hoàn thiện
            if (HasEditingPlan)
            {
                StatusMessage = "⚡ [1-Click] Đang xuất video 9:16 hoàn chỉnh...";
                await ExecuteRenderFinalShortAsync();

                if (HasFinalVideo && !string.IsNullOrEmpty(FinalVideoPath))
                {
                    StatusMessage = "🎉 [1-Click Thành Công] Đã xuất video hoàn tất! Bạn có thể lấy file và đăng ngay.";
                    ProgressPercentage = 100;
                    _fileService.Log($"[1-Click Pipeline] Hoàn tất 100%! Video sẵn sàng tại: {FinalVideoPath}");
                    return FinalVideoPath;
                }
            }

            return string.Empty;
        }

        #endregion

        #region AI Auto Video Editing Engine Pipeline

        public async Task ExecuteAutoEditPipelineAsync()
        {
            ClearError();

            if (!HasVideo || VideoInfo == null || !File.Exists(SelectedVideoPath))
            {
                SetError("Vui lòng chọn video hợp lệ trước khi thực hiện.");
                return;
            }

            if (IsApiKeyRequired && string.IsNullOrWhiteSpace(GeminiApiKey))
            {
                StatusMessage = "Chưa cấu hình Gemini API Key: Đang kích hoạt Smart Director tự động lên kế hoạch dựng 9:16...";
            }

            _cts = new CancellationTokenSource();
            IsBusy = true;
            CanCancel = true;
            IsAnalyzing = true;
            ProgressPercentage = 5;
            StatusMessage = "Khởi động AI Auto Video Editing Engine...";

            string tempDir = _fileService.CreateTempDirectory();

            try
            {
                // BƯỚC 1: AI Scene Detection
                List<VideoScene> scenes = new();
                if (EnableSceneDetection)
                {
                    ProgressPercentage = 15;
                    StatusMessage = "Đang nhận diện các phân đoạn (Scene Detection)...";
                    scenes = await _sceneDetectionService.DetectScenesAsync(SelectedVideoPath, VideoInfo.DurationSeconds, _cts.Token);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        DetectedScenes.Clear();
                        foreach (var sc in scenes) DetectedScenes.Add(sc);
                    });
                }

                // BƯỚC 2: AI Auto Editing Plan (Gemini)
                ProgressPercentage = 30;
                StatusMessage = "AI đang phân tích nội dung, tạo hook và lên kế hoạch dựng 9:16...";

                var currentSettings = _settingsService.CurrentSettings;
                if ((SelectedResolution.Contains("Original") || SelectedResolution.Contains("Gốc") || SelectedResolution.Contains("gốc")) && VideoInfo != null && VideoInfo.Width > 0 && VideoInfo.Height > 0)
                {
                    currentSettings.OutputResolution = $"Original ({VideoInfo.Width}x{VideoInfo.Height})";
                }
                else
                {
                    currentSettings.OutputResolution = SelectedResolution;
                }
                currentSettings.GeminiApiUrl = GeminiApiUrl;
                currentSettings.SpeechRate = SpeechRate;
                currentSettings.DurationMode = SelectedDurationMode;
                currentSettings.NarrationTone = SelectedNarrationTone;
                currentSettings.EnableAutoCut = EnableAutoCut;

                string? existingScript = string.IsNullOrWhiteSpace(ScriptText) ? null : ScriptText.Trim();

                var plan = await _editingPlanService.GenerateEditingPlanAsync(
                    GeminiApiKey,
                    SelectedGeminiModel,
                    SelectedVideoPath,
                    VideoInfo.DurationSeconds,
                    SelectedStylePreset,
                    scenes,
                    currentSettings,
                    existingScript,
                    CustomTopic,
                    (st, p) =>
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = st;
                            ProgressPercentage = p;
                        });
                    },
                    _cts.Token);

                CurrentEditingPlan = plan;
                HasEditingPlan = true;
                HookText = plan.Hook;
                
                // If user hadn't provided a custom script, display the generated one in editor
                if (string.IsNullOrWhiteSpace(existingScript))
                {
                    ScriptText = plan.NarrationScript;
                }

                // Cập nhật Subtitle list
                Application.Current.Dispatcher.Invoke(() =>
                {
                    SubtitleList.Clear();
                    if (plan.Subtitles != null)
                    {
                        foreach (var sub in plan.Subtitles) SubtitleList.Add(sub);
                    }
                });

                // BƯỚC 3: Tạo giọng đọc TTS
                ProgressPercentage = 55;
                StatusMessage = $"Đang tạo giọng đọc thuyết minh ({SelectedTtsProvider})...";

                string rawAudioPath = Path.Combine(tempDir, "auto_narration.mp3");
                var ttsSettings = BuildCurrentTtsSettings();

                var ttsResult = await _ttsService.GenerateSpeechWithTimestampsAsync(ScriptText, ttsSettings, rawAudioPath, _cts.Token);

                double actualAudioDuration = await _ffmpegService.GetMediaDurationAsync(rawAudioPath, _cts.Token);
                if (actualAudioDuration <= 0) actualAudioDuration = (ttsResult.DurationSeconds > 0) ? ttsResult.DurationSeconds : 40.0;
                ttsResult.DurationSeconds = actualAudioDuration;

                double outroPadding = 1.5; // Đệm 1.5 giây sau khi dứt giọng nói để câu cuối không bao giờ bị cắt cụt
                double speechAndOutro = actualAudioDuration + outroPadding;
                double targetShortDuration;

                if (SelectedDurationMode.Contains("Highlight") || SelectedDurationMode.Contains("Cắt ghép"))
                {
                    // Cắt ghép highlight ăn khớp 100% từng câu thuyết minh với phân cảnh hành động
                    plan.Scenes = EditingPlanService.AlignScenesToSpeech(plan, ttsResult, VideoInfo.DurationSeconds, scenes, outroPadding);
                    targetShortDuration = Math.Max(plan.Scenes.Sum(s => Math.Max(0, s.End - s.Start)), speechAndOutro);
                    _fileService.Log($"Đồng bộ {plan.Scenes.Count} cảnh Highlight khớp từng câu giọng đọc ({actualAudioDuration:F1}s + {outroPadding:F1}s outro = {targetShortDuration:F1}s).");
                }
                else if (SelectedDurationMode.Contains("Toàn bộ") || SelectedDurationMode.Contains("Không cắt"))
                {
                    // Chế độ Giữ nguyên toàn bộ video gốc: Người dùng yêu cầu giữ nguyên video
                    plan.Scenes = new List<VideoScene>
                    {
                        new()
                        {
                            Id = "scene_full_01",
                            Start = 0.0,
                            End = VideoInfo.DurationSeconds,
                            Description = "Toàn bộ video gốc",
                            Importance = "High",
                            ImportanceScore = 1.0,
                            FocusX = plan.FocusX,
                            FocusY = plan.FocusY
                        }
                    };
                    targetShortDuration = Math.Max(VideoInfo.DurationSeconds, speechAndOutro);
                    _fileService.Log($"Giữ toàn bộ video gốc: {targetShortDuration:F1}s (Giọng đọc: {actualAudioDuration:F1}s).");
                }
                else
                {
                    // CHẾ ĐỘ MẶC ĐỊNH: "🤖 Tự Động Thông Minh (Thuật toán tự quyết định)"
                    // Thuật toán thông minh:
                    // 1. Nếu giọng đọc đã phủ kín sát cuối video (chênh lệch <= 2.5s): Giữ trọn vẹn toàn bộ video gốc!
                    // 2. Nếu giọng đọc dừng sớm hơn video gốc > 2.5s (ví dụ: video 46s nhưng bài thuyết minh kết thúc ở 33s):
                    //    -> Tự động kết thúc video tại điểm dứt lời thoại (speechAndOutro), không để lại 10-15s chết lặng!
                    double gap = VideoInfo.DurationSeconds - speechAndOutro;
                    if (gap <= 2.5)
                    {
                        plan.Scenes = new List<VideoScene>
                        {
                            new()
                            {
                                Id = "scene_full_01",
                                Start = 0.0,
                                End = VideoInfo.DurationSeconds,
                                Description = "Toàn bộ video gốc",
                                Importance = "High",
                                ImportanceScore = 1.0,
                                FocusX = plan.FocusX,
                                FocusY = plan.FocusY
                            }
                        };
                        targetShortDuration = Math.Max(VideoInfo.DurationSeconds, speechAndOutro);
                        _fileService.Log($"[Smart Auto] Giọng đọc phủ kín ({actualAudioDuration:F1}s/{VideoInfo.DurationSeconds:F1}s, chênh lệch {gap:F1}s). Giữ trọn vẹn toàn bộ video gốc.");
                    }
                    else
                    {
                        double trimmedEnd = Math.Min(VideoInfo.DurationSeconds, speechAndOutro);
                        plan.Scenes = new List<VideoScene>
                        {
                            new()
                            {
                                Id = "scene_auto_smart",
                                Start = 0.0,
                                End = trimmedEnd,
                                Description = "Đoạn video khớp trọn vẹn lời thuyết minh",
                                Importance = "High",
                                ImportanceScore = 1.0,
                                FocusX = plan.FocusX,
                                FocusY = plan.FocusY
                            }
                        };
                        targetShortDuration = trimmedEnd;
                        _fileService.Log($"[Smart Auto] Lời thuyết minh kết thúc tại {actualAudioDuration:F1}s. Tự động kết thúc video tại {targetShortDuration:F1}s để khớp 100% lời nói, loại bỏ {gap:F1}s im lặng!");
                    }
                }

                // Đồng bộ thời lượng audio
                string syncedAudioPath = Path.Combine(tempDir, "auto_synced.mp3");
                await _ffmpegService.AdjustAudioDurationAsync(rawAudioPath, syncedAudioPath, targetShortDuration, _cts.Token);
                string audioToMix = File.Exists(syncedAudioPath) ? syncedAudioPath : rawAudioPath;

                // BƯỚC 4: Tạo Subtitle ASS đồng bộ 100% khớp với âm thanh
                ProgressPercentage = 70;
                StatusMessage = "Đang đồng bộ phụ đề chuẩn xác theo nhịp âm thanh...";

                List<SubtitleSegment> subSegments;
                if (ttsResult.WordTimestamps != null && ttsResult.WordTimestamps.Count > 0)
                {
                    subSegments = _subtitleService.BuildSubtitlesFromWordTimestamps(ttsResult.WordTimestamps, actualAudioDuration);
                }
                else
                {
                    subSegments = _subtitleService.SplitNarrationToSubtitles(ScriptText, actualAudioDuration);
                }

                // Ghi đè vào plan và cập nhật giao diện để phụ đề khớp 100% với giọng nói
                plan.Subtitles = subSegments;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    SubtitleList.Clear();
                    foreach (var sub in subSegments) SubtitleList.Add(sub);
                });

                string assPath = Path.Combine(tempDir, "auto_subtitles.ass");
                _subtitleService.GenerateAssSubtitleFile(
                    subSegments,
                    SelectedSubtitlePreset,
                    assPath,
                    plan.OutputWidth,
                    plan.OutputHeight,
                    EnableKeywordHighlight);

                _cachedSubtitlePath = assPath;

                // BƯỚC 5: Mix Audio (Voice + BGM Ducking + SFX)
                ProgressPercentage = 80;
                StatusMessage = "Đang phối khí âm thanh (Voice + Music Ducking + SFX)...";

                string mixedAudioPath = Path.Combine(tempDir, "auto_mixed_audio.aac");
                await _audioMixService.MixAudioTracksAsync(
                    audioToMix,
                    EnableBackgroundMusic ? BackgroundMusicPath : null,
                    EnableSoundEffects ? plan.SoundEffects : null,
                    plan.MusicVolume,
                    EnableMusicDucking,
                    plan.DuckingVolume,
                    targetShortDuration,
                    mixedAudioPath,
                    _cts.Token);

                _cachedMixedAudioPath = mixedAudioPath;

                // BƯỚC 6: Render Proxy Preview tốc độ cao (540x960)
                ProgressPercentage = 90;
                StatusMessage = "Đang xuất bản video xem trước 9:16 (Proxy Preview)...";

                string proxyOutPath = Path.Combine(tempDir, "preview_proxy.mp4");
                await _proxyVideoService.GenerateProxyPreviewAsync(
                    SelectedVideoPath,
                    mixedAudioPath,
                    assPath,
                    plan,
                    proxyOutPath,
                    _cts.Token);

                ProgressPercentage = 100;
                StatusMessage = "Đã dựng xong bản xem trước 9:16! Bạn có thể xem ngay và bấm 'Render Video 9:16' để xuất bản.";
                _fileService.Log("Hoàn thành tự dựng video AI (Proxy Preview).");

                // Nạp video preview vào player
                RequestLoadVideoPreview?.Invoke(proxyOutPath);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Đã hủy quy trình tự dựng video.";
                ProgressPercentage = 0;
            }
            catch (Exception ex)
            {
                SetError($"Lỗi trong quy trình Auto Video Editing: {ex.Message}");
                StatusMessage = "Dựng video thất bại.";
                _fileService.Log($"Lỗi Auto Video Editing: {ex.Message}", "ERROR", ex);
            }
            finally
            {
                IsBusy = false;
                CanCancel = false;
                IsAnalyzing = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        public async Task ExecuteRenderFinalShortAsync()
        {
            ClearError();

            if (!HasVideo || VideoInfo == null || !File.Exists(SelectedVideoPath) || CurrentEditingPlan == null)
            {
                SetError("Chưa có kế hoạch dựng video hoàn chỉnh. Vui lòng chạy phân tích trước.");
                return;
            }

            _cts = new CancellationTokenSource();
            IsBusy = true;
            CanCancel = true;
            IsGenerating = true;
            ProgressPercentage = 5;
            StatusMessage = "Bắt đầu render video chất lượng cao 9:16...";

            string tempDir = _fileService.CreateTempDirectory();

            try
            {
                string outputDir = string.IsNullOrWhiteSpace(OutputDirectory) ? AppSettings.GetDefaultOutputDirectory() : OutputDirectory;
                string finalOutputPath = _fileService.GetUniqueOutputPath(outputDir, VideoInfo.FileName, suffix: "_short_9x16");

                // Check or regenerate mixed audio and subtitle if user edited script
                string rawAudioPath = Path.Combine(tempDir, "final_narration.mp3");
                string syncedAudioPath = Path.Combine(tempDir, "final_synced.mp3");

                var ttsSettings = BuildCurrentTtsSettings();

                ProgressPercentage = 15;
                StatusMessage = "Đang tổng hợp giọng đọc từ kịch bản hoàn chỉnh...";
                var ttsResult = await _ttsService.GenerateSpeechWithTimestampsAsync(ScriptText, ttsSettings, rawAudioPath, _cts.Token);

                double actualAudioDuration = await _ffmpegService.GetMediaDurationAsync(rawAudioPath, _cts.Token);
                if (actualAudioDuration <= 0) actualAudioDuration = (ttsResult.DurationSeconds > 0) ? ttsResult.DurationSeconds : 40.0;
                ttsResult.DurationSeconds = actualAudioDuration;

                double outroPadding = 1.5;
                double speechAndOutro = actualAudioDuration + outroPadding;
                double targetShortDuration;

                if (SelectedDurationMode.Contains("Highlight") || SelectedDurationMode.Contains("Cắt ghép"))
                {
                    CurrentEditingPlan.Scenes = EditingPlanService.AlignScenesToSpeech(CurrentEditingPlan, ttsResult, VideoInfo.DurationSeconds, DetectedScenes.ToList(), outroPadding);
                    targetShortDuration = Math.Max(CurrentEditingPlan.Scenes.Sum(s => Math.Max(0, s.End - s.Start)), speechAndOutro);
                    _fileService.Log($"Đồng bộ {CurrentEditingPlan.Scenes.Count} cảnh Highlight khớp từng câu giọng đọc ({actualAudioDuration:F1}s + {outroPadding:F1}s outro = {targetShortDuration:F1}s).");
                }
                else if (SelectedDurationMode.Contains("Toàn bộ") || SelectedDurationMode.Contains("Không cắt"))
                {
                    CurrentEditingPlan.Scenes = new List<VideoScene>
                    {
                        new()
                        {
                            Id = "scene_full_01",
                            Start = 0.0,
                            End = VideoInfo.DurationSeconds,
                            Description = "Toàn bộ video gốc",
                            Importance = "High",
                            ImportanceScore = 1.0,
                            FocusX = CurrentEditingPlan.FocusX,
                            FocusY = CurrentEditingPlan.FocusY
                        }
                    };
                    targetShortDuration = Math.Max(VideoInfo.DurationSeconds, speechAndOutro);
                    _fileService.Log($"Giữ toàn bộ video gốc: {targetShortDuration:F1}s (Giọng đọc: {actualAudioDuration:F1}s).");
                }
                else
                {
                    // Tự Động Thông Minh: nếu giọng đọc dừng sớm > 2.5s, kết thúc video tại speechAndOutro
                    double gap = VideoInfo.DurationSeconds - speechAndOutro;
                    if (gap <= 2.5)
                    {
                        CurrentEditingPlan.Scenes = new List<VideoScene>
                        {
                            new()
                            {
                                Id = "scene_full_01",
                                Start = 0.0,
                                End = VideoInfo.DurationSeconds,
                                Description = "Toàn bộ video gốc",
                                Importance = "High",
                                ImportanceScore = 1.0,
                                FocusX = CurrentEditingPlan.FocusX,
                                FocusY = CurrentEditingPlan.FocusY
                            }
                        };
                        targetShortDuration = Math.Max(VideoInfo.DurationSeconds, speechAndOutro);
                        _fileService.Log($"[Smart Auto] Giọng đọc phủ kín ({actualAudioDuration:F1}s/{VideoInfo.DurationSeconds:F1}s, chênh lệch {gap:F1}s). Giữ trọn vẹn video.");
                    }
                    else
                    {
                        double trimmedEnd = Math.Min(VideoInfo.DurationSeconds, speechAndOutro);
                        CurrentEditingPlan.Scenes = new List<VideoScene>
                        {
                            new()
                            {
                                Id = "scene_auto_smart",
                                Start = 0.0,
                                End = trimmedEnd,
                                Description = "Đoạn video khớp trọn vẹn lời thuyết minh",
                                Importance = "High",
                                ImportanceScore = 1.0,
                                FocusX = CurrentEditingPlan.FocusX,
                                FocusY = CurrentEditingPlan.FocusY
                            }
                        };
                        targetShortDuration = trimmedEnd;
                        _fileService.Log($"[Smart Auto] Lời thuyết minh kết thúc tại {actualAudioDuration:F1}s. Tự động kết thúc video tại {targetShortDuration:F1}s để khớp 100% lời nói, loại bỏ {gap:F1}s im lặng!");
                    }
                }

                await _ffmpegService.AdjustAudioDurationAsync(rawAudioPath, syncedAudioPath, targetShortDuration, _cts.Token);
                string audioToMix = File.Exists(syncedAudioPath) ? syncedAudioPath : rawAudioPath;

                // Subtitles
                ProgressPercentage = 30;
                StatusMessage = "Đang xuất bản phụ đề phong cách " + SelectedSubtitlePreset + "...";

                List<SubtitleSegment> subSegments;
                if (ttsResult.WordTimestamps != null && ttsResult.WordTimestamps.Count > 0)
                {
                    subSegments = _subtitleService.BuildSubtitlesFromWordTimestamps(ttsResult.WordTimestamps, actualAudioDuration);
                }
                else
                {
                    subSegments = _subtitleService.SplitNarrationToSubtitles(ScriptText, actualAudioDuration);
                }

                CurrentEditingPlan.Subtitles = subSegments;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    SubtitleList.Clear();
                    foreach (var sub in subSegments) SubtitleList.Add(sub);
                });

                string assPath = Path.Combine(tempDir, "final_subtitles.ass");
                _subtitleService.GenerateAssSubtitleFile(
                    subSegments,
                    SelectedSubtitlePreset,
                    assPath,
                    CurrentEditingPlan.OutputWidth,
                    CurrentEditingPlan.OutputHeight,
                    EnableKeywordHighlight);

                // Mixed audio
                ProgressPercentage = 45;
                StatusMessage = "Đang hòa trộn âm thanh chất lượng studio 48kHz...";
                string mixedAudioPath = Path.Combine(tempDir, "final_mixed_audio.aac");
                await _audioMixService.MixAudioTracksAsync(
                    audioToMix,
                    EnableBackgroundMusic ? BackgroundMusicPath : null,
                    EnableSoundEffects ? CurrentEditingPlan.SoundEffects : null,
                    CurrentEditingPlan.MusicVolume,
                    EnableMusicDucking,
                    CurrentEditingPlan.DuckingVolume,
                    targetShortDuration,
                    mixedAudioPath,
                    _cts.Token);

                // Final Compose
                ProgressPercentage = 60;
                StatusMessage = "Đang kết xuất video 9:16 (Crop, Scale, Zoom, Subtitles, Audio)...";
                await _videoComposerService.ComposeVerticalShortAsync(
                    SelectedVideoPath,
                    mixedAudioPath,
                    assPath,
                    CurrentEditingPlan,
                    finalOutputPath,
                    crf: 18,
                    preset: "medium",
                    (st, p) =>
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = st;
                            ProgressPercentage = 60 + (p * 0.35);
                        });
                    },
                    _cts.Token);

                double finalDur = await _ffmpegService.GetMediaDurationAsync(finalOutputPath, _cts.Token);
                FinalVideoPath = finalOutputPath;
                HasFinalVideo = true;
                FinalVideoDuration = TimeSpan.FromSeconds(finalDur).ToString(@"hh\:mm\:ss\.ff");

                ProgressPercentage = 100;
                StatusMessage = "Xuất video 9:16 hoàn tất thành công!";
                _fileService.Log($"Xuất Short video thành công: {finalOutputPath}");

                RequestLoadVideoPreview?.Invoke(finalOutputPath);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Đã hủy xuất video.";
                ProgressPercentage = 0;
            }
            catch (Exception ex)
            {
                SetError($"Lỗi xuất video 9:16: {ex.Message}");
                StatusMessage = "Xuất video thất bại.";
                _fileService.Log($"Lỗi xuất video 9:16: {ex.Message}", "ERROR", ex);
            }
            finally
            {
                _fileService.DeleteTempDirectory(tempDir);
                IsBusy = false;
                CanCancel = false;
                IsGenerating = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        #endregion

        private void ExecuteCancel()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                StatusMessage = "Đang dừng tiến trình...";
                _cts.Cancel();
            }
        }

        private void UpdateEstimatedDuration()
        {
            if (string.IsNullOrWhiteSpace(ScriptText))
            {
                EstimatedScriptDuration = "0 giây";
                return;
            }

            string pureText = TtsService.StripTimestampsAndFormatText(ScriptText);
            var words = Regex.Split(pureText.Trim(), @"\s+");
            int wordCount = words.Length;

            double wordsPerSec = 4.15 * (SpeechRate > 0 ? SpeechRate : 1.20);
            double estimatedSec = wordCount / wordsPerSec;

            EstimatedScriptDuration = $"{Math.Round(estimatedSec, 1)} giây (~{wordCount} từ)";
        }

        #region Persona Studio Operations

        private void InitializePersonaStudio()
        {
            try
            {
                // 1. Load Personas
                var list = _personaManager.GetAllPersonas();
                Personas.Clear();
                foreach (var p in list)
                {
                    Personas.Add(p);
                }

                var active = _personaManager.GetActivePersona();
                _selectedPersona = Personas.FirstOrDefault(p => p.Id == active.Id) ?? Personas.FirstOrDefault();
                OnPropertyChanged(nameof(SelectedPersona));

                // 2. Load Few-Shot Examples
                var examples = _fewShotManager.GetAllExamples();
                FewShotExamples.Clear();
                foreach (var ex in examples)
                {
                    FewShotExamples.Add(ex);
                }
                SelectedFewShotExample = FewShotExamples.FirstOrDefault();

                // 3. Load Versions & Refresh Prompt Preview
                LoadVersionsForSelectedPersona();
                RefreshCompiledPromptView();
            }
            catch (Exception ex)
            {
                _fileService.Log($"Lỗi khởi tạo Persona Studio: {ex.Message}", "WARN", ex);
            }
        }

        private void LoadVersionsForSelectedPersona()
        {
            PromptVersions.Clear();
            if (SelectedPersona != null)
            {
                var versions = _promptVersionManager.GetVersionsForPersona(SelectedPersona.Id);
                foreach (var v in versions)
                {
                    PromptVersions.Add(v);
                }
                SelectedPromptVersion = PromptVersions.FirstOrDefault();
            }
        }

        public void RefreshCompiledPromptView()
        {
            if (SelectedPersona == null) return;
            try
            {
                var compiled = _promptBuilder.BuildSystemPrompt(
                    SelectedPersona,
                    SelectedOutputMode,
                    TestDurationSeconds,
                    TestTopic,
                    null);

                CompiledSystemPromptView = $"=== SYSTEM INSTRUCTION ===\n{compiled.SystemInstruction}\n\n=== USER PROMPT ===\n{compiled.UserPrompt}";
                CompiledEstimatedTokens = compiled.EstimatedTokens;
                CompiledEstimatedCost = compiled.EstimatedCostUsd;
            }
            catch (Exception ex)
            {
                _fileService.Log($"Lỗi biên dịch System Prompt xem thử: {ex.Message}", "WARN");
            }
        }

        private void ExecuteCreatePersona()
        {
            string name = string.IsNullOrWhiteSpace(NewPersonaName)
                ? $"Persona Mới {Personas.Count + 1}"
                : NewPersonaName.Trim();

            var newProfile = new PersonaProfile
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Description = "Persona tùy chỉnh theo phong cách riêng của bạn.",
                VoiceTone = "Sắc sảo, tự nhiên, lôi cuốn",
                IsBuiltIn = false,
                IsDefault = false,
                StyleConfig = SelectedPersona?.StyleConfig.Clone() ?? new StyleEngineConfig()
            };

            _personaManager.CreatePersona(newProfile);
            Personas.Add(newProfile);
            SelectedPersona = newProfile;
            NewPersonaName = string.Empty;
            StatusMessage = $"Đã tạo Persona mới: {name}";
        }

        private void ExecuteDuplicatePersona()
        {
            if (SelectedPersona == null) return;
            string copyName = $"{SelectedPersona.Name} (Bản sao)";
            var duplicated = _personaManager.DuplicatePersona(SelectedPersona.Id, copyName);
            Personas.Add(duplicated);
            SelectedPersona = duplicated;
            StatusMessage = $"Đã nhân bản Persona: {copyName}";
        }

        private void ExecuteDeletePersona()
        {
            if (SelectedPersona == null) return;
            if (SelectedPersona.IsBuiltIn)
            {
                SetError("Không thể xóa Persona mẫu mặc định của hệ thống.");
                return;
            }

            string name = SelectedPersona.Name;
            _personaManager.DeletePersona(SelectedPersona.Id);
            Personas.Remove(SelectedPersona);
            SelectedPersona = Personas.FirstOrDefault(p => p.IsDefault) ?? Personas.FirstOrDefault();
            StatusMessage = $"Đã xóa Persona: {name}";
        }

        private void ExecuteSetDefaultPersona()
        {
            if (SelectedPersona == null) return;
            _personaManager.SetDefaultPersona(SelectedPersona.Id);
            foreach (var p in Personas)
            {
                p.IsDefault = (p.Id == SelectedPersona.Id);
            }
            StatusMessage = $"Đã đặt Persona '{SelectedPersona.Name}' làm mặc định cho mọi tác vụ!";
        }

        private void ExecuteResetPersonasToDefault()
        {
            _personaManager.ResetToDefaults();
            Personas.Clear();
            foreach (var p in _personaManager.GetAllPersonas())
            {
                Personas.Add(p);
            }
            SelectedPersona = Personas.FirstOrDefault(p => p.IsDefault) ?? Personas.FirstOrDefault();
            StatusMessage = "Đã khôi phục toàn bộ danh sách Persona về mặc định hệ thống.";
        }

        private void ExecuteSavePersona()
        {
            if (SelectedPersona == null) return;
            _personaManager.UpdatePersona(SelectedPersona);
            RefreshCompiledPromptView();
            StatusMessage = $"Đã lưu cấu hình Persona '{SelectedPersona.Name}'.";
        }

        private void ExecuteAddFewShotExample()
        {
            if (string.IsNullOrWhiteSpace(NewExampleTopic) ||
                string.IsNullOrWhiteSpace(NewExamplePositive))
            {
                SetError("Vui lòng nhập chủ đề và nội dung ví dụ mẫu đúng phong cách.");
                return;
            }

            var ex = new FewShotExample
            {
                Id = Guid.NewGuid().ToString("N"),
                Topic = NewExampleTopic.Trim(),
                PositiveExample = NewExamplePositive.Trim(),
                NegativeExample = NewExampleNegative.Trim(),
                Explanation = NewExampleExplanation.Trim(),
                PersonaId = SelectedPersona?.Id
            };

            _fewShotManager.AddOrUpdateExample(ex);
            FewShotExamples.Add(ex);
            SelectedFewShotExample = ex;

            NewExampleTopic = string.Empty;
            NewExamplePositive = string.Empty;
            NewExampleNegative = string.Empty;
            NewExampleExplanation = string.Empty;

            RefreshCompiledPromptView();
            StatusMessage = "Đã thêm ví dụ mẫu Few-Shot thành công!";
        }

        private void ExecuteDeleteFewShotExample()
        {
            if (SelectedFewShotExample == null) return;
            _fewShotManager.DeleteExample(SelectedFewShotExample.Id);
            FewShotExamples.Remove(SelectedFewShotExample);
            SelectedFewShotExample = FewShotExamples.FirstOrDefault();
            RefreshCompiledPromptView();
            StatusMessage = "Đã xóa ví dụ mẫu.";
        }

        private async Task ExecuteTestPromptAsync()
        {
            if (SelectedPersona == null)
            {
                SetError("Chưa chọn Persona để thử nghiệm.");
                return;
            }

            if (IsApiKeyRequired && string.IsNullOrWhiteSpace(GeminiApiKey))
            {
                SetError("Vui lòng cấu hình Gemini API Key trước khi thử nghiệm (Cài Đặt & API).");
                return;
            }

            ClearError();
            IsTestingPrompt = true;
            StatusMessage = $"Đang gọi AI để thử nghiệm Persona '{SelectedPersona.Name}'...";

            try
            {
                if (!string.IsNullOrWhiteSpace(TestGeneratedOutput))
                {
                    PreviousTestOutput = TestGeneratedOutput;
                    HasPreviousTestOutput = true;
                }

                var res = await _scriptGenerator.GenerateAsync(
                    SelectedPersona,
                    SelectedOutputMode,
                    TestDurationSeconds,
                    TestTopic,
                    null,
                    GeminiApiKey,
                    SelectedGeminiModel,
                    "Gemini",
                    GeminiApiUrl,
                    null,
                    CancellationToken.None);

                if (res.Success)
                {
                    TestGeneratedOutput = res.OutputText;
                    TestQualityScore = res.ValidationResult.QualityScore;
                    TestTokensUsed = res.TotalTokens;
                    TestDurationMs = res.DurationMs;

                    var issuesCount = res.ValidationResult.Issues.Count;
                    TestValidationSummary = issuesCount == 0
                        ? $"Hoàn hảo! ({TestQualityScore}/100 điểm, {TestTokensUsed} tokens, {TestDurationMs}ms)"
                        : $"Đạt {TestQualityScore}/100 điểm ({issuesCount} lưu ý: {string.Join("; ", res.ValidationResult.Issues)})";

                    StatusMessage = $"Thử nghiệm Persona thành công! Điểm chất lượng: {TestQualityScore}/100.";
                }
                else
                {
                    SetError($"Lỗi khi thử nghiệm Persona: {res.ErrorMessage}");
                    StatusMessage = "Thử nghiệm thất bại.";
                }
            }
            catch (Exception ex)
            {
                SetError($"Ngoại lệ khi thử nghiệm Persona: {ex.Message}");
                _fileService.Log($"Lỗi thử nghiệm prompt: {ex.Message}", "ERROR", ex);
            }
            finally
            {
                IsTestingPrompt = false;
            }
        }

        private void ExecuteApplyTestScriptToStudio()
        {
            if (string.IsNullOrWhiteSpace(TestGeneratedOutput)) return;
            ScriptText = TestGeneratedOutput;
            SelectedTabIndex = 0; // Quay về tab Studio
            StatusMessage = "Đã áp dụng kịch bản vừa thử nghiệm vào Studio Dựng Video!";
        }

        private void ExecuteSavePromptVersion()
        {
            if (SelectedPersona == null) return;
            string note = string.IsNullOrWhiteSpace(VersionNote)
                ? $"Phiên bản {DateTime.Now:dd/MM/yyyy HH:mm}"
                : VersionNote.Trim();

            var version = _promptVersionManager.SaveVersion(SelectedPersona, CompiledSystemPromptView, note);
            PromptVersions.Insert(0, version);
            SelectedPromptVersion = version;
            VersionNote = string.Empty;
            StatusMessage = $"Đã lưu phiên bản {version.VersionTag} cho Persona '{SelectedPersona.Name}'.";
        }

        private void ExecuteRestorePromptVersion()
        {
            if (SelectedPromptVersion == null || SelectedPersona == null) return;

            SelectedPersona.StyleConfig = SelectedPromptVersion.StyleConfigSnapshot.Clone();
            _personaManager.UpdatePersona(SelectedPersona);

            OnPropertyChanged(nameof(CuriosityLevel));
            OnPropertyChanged(nameof(DramaticLevel));
            OnPropertyChanged(nameof(WeirdnessLevel));
            OnPropertyChanged(nameof(SarcasmLevel));
            OnPropertyChanged(nameof(VisualLanguageLevel));
            OnPropertyChanged(nameof(HookIntensity));
            OnPropertyChanged(nameof(SuspenseLevel));
            OnPropertyChanged(nameof(SatisfactionLevel));
            OnPropertyChanged(nameof(SentenceRhythm));
            OnPropertyChanged(nameof(NarrationSpeed));
            OnPropertyChanged(nameof(EndingStyle));
            OnPropertyChanged(nameof(Verbosity));

            RefreshCompiledPromptView();
            StatusMessage = $"Đã khôi phục Persona về phiên bản {SelectedPromptVersion.VersionTag}!";
        }

        private void ExecuteDeletePromptVersion()
        {
            if (SelectedPromptVersion == null) return;
            string tag = SelectedPromptVersion.VersionTag;
            _promptVersionManager.DeleteVersion(SelectedPromptVersion.Id);
            PromptVersions.Remove(SelectedPromptVersion);
            SelectedPromptVersion = PromptVersions.FirstOrDefault();
            StatusMessage = $"Đã xóa bản lưu {tag}.";
        }

        #endregion

        #region Topic To Video & Project Management

        public async Task<VideoProject?> CreateProjectFromTopicAsync(string topic, string personaId, string aspectRatio = "9:16", int sceneCount = 5)
        {
            ClearError();
            if (_topicToVideoService == null)
            {
                SetError("Dịch vụ TopicToVideoService chưa được kích hoạt.");
                return null;
            }

            _cts = new CancellationTokenSource();
            IsBusy = true;
            CanCancel = true;
            ProgressPercentage = 5;
            StatusMessage = $"Khởi tạo dự án từ chủ đề: {topic}...";

            try
            {
                var settings = _settingsService.CurrentSettings;
                var proj = await _topicToVideoService.CreateProjectFromTopicAsync(
                    topic,
                    personaId,
                    aspectRatio,
                    sceneCount,
                    GeminiApiKey,
                    SelectedGeminiModel,
                    settings,
                    (msg, p) =>
                    {
                        App.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = msg;
                            ProgressPercentage = p;
                        });
                    },
                    _cts.Token);

                CurrentProject = proj;
                HookText = proj.Hook;
                ScriptText = proj.FullScript;

                if (!string.IsNullOrEmpty(proj.ProxyVideoPath))
                {
                    RequestLoadVideoPreview?.Invoke(proj.ProxyVideoPath);
                }

                StatusMessage = "Tạo video từ chủ đề hoàn tất!";
                return proj;
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Đã hủy tạo video từ chủ đề.";
                return null;
            }
            catch (Exception ex)
            {
                SetError($"Lỗi tạo video từ chủ đề: {ex.Message}");
                StatusMessage = "Tạo video thất bại.";
                return null;
            }
            finally
            {
                IsBusy = false;
                CanCancel = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        public async Task<string?> RenderCurrentProjectAsync()
        {
            ClearError();
            if (CurrentProject == null || _topicToVideoService == null)
            {
                SetError("Chưa có dự án để xuất video.");
                return null;
            }

            _cts = new CancellationTokenSource();
            IsBusy = true;
            CanCancel = true;
            ProgressPercentage = 5;
            StatusMessage = "Đang xuất bản video hoàn thiện...";

            try
            {
                string finalPath = await _topicToVideoService.AssembleProjectVideoAsync(
                    CurrentProject,
                    isDraftPreview: false,
                    (msg, p) =>
                    {
                        App.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = msg;
                            ProgressPercentage = p;
                        });
                    },
                    _cts.Token);

                CurrentProject.FinalVideoPath = finalPath;
                FinalVideoPath = finalPath;
                HasFinalVideo = true;

                if (_projectService != null)
                {
                    await _projectService.SaveProjectAsync(CurrentProject);
                }

                RequestLoadVideoPreview?.Invoke(finalPath);
                StatusMessage = "Xuất video MP4 hoàn tất thành công!";
                return finalPath;
            }
            catch (Exception ex)
            {
                SetError($"Lỗi xuất video: {ex.Message}");
                StatusMessage = "Xuất video thất bại.";
                return null;
            }
            finally
            {
                IsBusy = false;
                CanCancel = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        public async Task<VideoProject?> SaveCurrentProjectAsync()
        {
            if (CurrentProject == null || _projectService == null) return null;
            CurrentProject.Hook = HookText;
            CurrentProject.FullScript = ScriptText;
            return await _projectService.SaveProjectAsync(CurrentProject);
        }

        public async Task<VideoProject?> LoadProjectAsync(string idOrPath)
        {
            if (_projectService == null) return null;
            var proj = await _projectService.LoadProjectAsync(idOrPath);
            if (proj != null)
            {
                CurrentProject = proj;
                HookText = proj.Hook;
                ScriptText = proj.FullScript;
                if (!string.IsNullOrEmpty(proj.FinalVideoPath) && File.Exists(proj.FinalVideoPath))
                {
                    FinalVideoPath = proj.FinalVideoPath;
                    HasFinalVideo = true;
                    RequestLoadVideoPreview?.Invoke(proj.FinalVideoPath);
                }
                else if (!string.IsNullOrEmpty(proj.ProxyVideoPath) && File.Exists(proj.ProxyVideoPath))
                {
                    RequestLoadVideoPreview?.Invoke(proj.ProxyVideoPath);
                }
            }
            return proj;
        }

        public async Task<List<VideoProject>> GetRecentProjectsAsync()
        {
            if (_projectService == null) return new List<VideoProject>();
            return await _projectService.GetRecentProjectsAsync();
        }

        #endregion

        public void SetError(string message)
        {
            ErrorMessage = message;
            HasError = true;
        }

        public void ClearError()
        {
            ErrorMessage = string.Empty;
            HasError = false;
        }
    }
}

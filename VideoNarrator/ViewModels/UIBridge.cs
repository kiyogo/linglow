using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using VideoNarrator.Models;

namespace VideoNarrator.ViewModels
{
    [ClassInterface(ClassInterfaceType.AutoDual)]
    [ComVisible(true)]
    public class UIBridge
    {
        private readonly MainViewModel _viewModel;
        private CoreWebView2? _coreWebView;

        public UIBridge(MainViewModel viewModel)
        {
            _viewModel = viewModel;

            _viewModel.PropertyChanged += (s, e) =>
            {
                switch (e.PropertyName)
                {
                    case nameof(_viewModel.StatusMessage):
                        PostMessage(new { type = "status", message = _viewModel.StatusMessage });
                        StatusChanged?.Invoke(_viewModel.StatusMessage);
                        break;

                    case nameof(_viewModel.ProgressPercentage):
                        PostMessage(new { type = "progress", percentage = _viewModel.ProgressPercentage });
                        break;

                    case nameof(_viewModel.IsBusy):
                        PostMessage(new { type = "busy", isBusy = _viewModel.IsBusy, canCancel = _viewModel.CanCancel });
                        ProcessingChanged?.Invoke(_viewModel.IsBusy);
                        break;

                    case nameof(_viewModel.ErrorMessage):
                    case nameof(_viewModel.HasError):
                        PostMessage(new { type = "error", message = _viewModel.ErrorMessage, hasError = _viewModel.HasError });
                        break;

                    case nameof(_viewModel.HookText):
                        PostMessage(new { type = "hook", hook = _viewModel.HookText });
                        break;

                    case nameof(_viewModel.ScriptText):
                        PostMessage(new { type = "script", script = _viewModel.ScriptText });
                        break;

                    case nameof(_viewModel.CustomTopic):
                        PostMessage(new { type = "customTopic", topic = _viewModel.CustomTopic });
                        break;

                    case nameof(_viewModel.VideoUrlInput):
                        PostMessage(new { type = "videoUrlInput", url = _viewModel.VideoUrlInput });
                        break;

                    case nameof(_viewModel.IsSearchingVideos):
                        PostMessage(new { type = "isSearchingVideos", isSearching = _viewModel.IsSearchingVideos });
                        break;

                    case nameof(_viewModel.HasDiscoveredVideos):
                    case nameof(_viewModel.DiscoveredVideos):
                        PostMessage(new { type = "videosDiscovered", videos = _viewModel.DiscoveredVideos.ToList() });
                        break;

                    case nameof(_viewModel.HasVideo):
                    case nameof(_viewModel.VideoInfo):
                        if (_viewModel.HasVideo && _viewModel.VideoInfo != null)
                        {
                            PostMessage(new
                            {
                                type = "videoLoaded",
                                path = _viewModel.SelectedVideoPath,
                                fileName = _viewModel.VideoInfo.FileName,
                                duration = _viewModel.VideoInfo.DurationSeconds,
                                width = _viewModel.VideoInfo.Width,
                                height = _viewModel.VideoInfo.Height,
                                fps = _viewModel.VideoInfo.Fps,
                                mediaUrl = ToMediaUrl(_viewModel.SelectedVideoPath)
                            });
                        }
                        break;

                    case nameof(_viewModel.FinalVideoPath):
                    case nameof(_viewModel.HasFinalVideo):
                        if (_viewModel.HasFinalVideo && !string.IsNullOrEmpty(_viewModel.FinalVideoPath))
                        {
                            double dur = _viewModel.VideoInfo?.DurationSeconds ?? 0;
                            PostMessage(new
                            {
                                type = "finalVideoReady",
                                path = _viewModel.FinalVideoPath,
                                fileName = Path.GetFileName(_viewModel.FinalVideoPath),
                                duration = dur,
                                mediaUrl = ToMediaUrl(_viewModel.FinalVideoPath)
                            });
                        }
                        break;

                    case nameof(_viewModel.CurrentProject):
                        if (_viewModel.CurrentProject != null)
                        {
                            PostMessage(new
                            {
                                type = "projectLoaded",
                                project = SerializeProject(_viewModel.CurrentProject)
                            });
                        }
                        break;
                }
            };
        }

        public void AttachWebView(CoreWebView2 coreWebView)
        {
            _coreWebView = coreWebView;
        }

        public static string ToMediaUrl(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return string.Empty;
            return $"https://localmedia.internal/?path={Uri.EscapeDataString(filePath)}";
        }

        public void NotifyPreviewReady(string previewPath)
        {
            PostMessage(new
            {
                type = "previewReady",
                path = previewPath,
                mediaUrl = ToMediaUrl(previewPath)
            });
        }

        private void PostMessage(object payload)
        {
            try
            {
                if (_coreWebView != null)
                {
                    string json = JsonSerializer.Serialize(payload);
                    App.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            _coreWebView?.PostWebMessageAsString(json);
                        }
                        catch { }
                    }));
                }
            }
            catch { }
        }

        // ==================== COM INTERFACES FOR JS ====================

        public void SelectVideoFile()
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    var dialog = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = "Chọn tệp Video đầu vào",
                        Filter = "Video Files|*.mp4;*.mkv;*.mov;*.avi;*.webm|All Files|*.*",
                        CheckFileExists = true,
                        Multiselect = false
                    };

                    var owner = Application.Current?.MainWindow;
                    if (owner != null && owner.IsVisible)
                    {
                        owner.Activate();
                    }

                    bool? result = (owner != null && owner.IsVisible)
                        ? dialog.ShowDialog(owner)
                        : dialog.ShowDialog();

                    if (result == true && !string.IsNullOrEmpty(dialog.FileName))
                    {
                        _ = _viewModel.LoadVideoFileAsync(dialog.FileName);
                        FileSelected?.Invoke(dialog.FileName);
                    }
                }
                catch (Exception ex)
                {
                    _viewModel.SetError($"Không thể mở hộp thoại chọn tệp: {ex.Message}");
                }
            });
        }

        public void LoadVideoPath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;
            App.Current.Dispatcher.Invoke(() =>
            {
                _ = _viewModel.LoadVideoFileAsync(filePath);
                FileSelected?.Invoke(filePath);
            });
        }

        public void GenerateScriptOnly()
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                _ = _viewModel.ExecuteGenerateScriptOnlyAsync();
            });
        }

        public void StartAutoEdit()
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                _ = _viewModel.ExecuteAutoEditPipelineAsync();
            });
        }

        public void DownloadAndAutoProduce(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            App.Current?.Dispatcher?.Invoke(() =>
            {
                _ = _viewModel.DownloadAndAutoProduceAsync(url);
            });
        }

        public void DownloadVideoOnly(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            App.Current?.Dispatcher?.Invoke(() =>
            {
                _ = _viewModel.DownloadVideoOnlyAsync(url);
            });
        }

        public void SearchVideos(string query)
        {
            App.Current?.Dispatcher?.Invoke(() =>
            {
                _ = _viewModel.SearchVideosAsync(query);
            });
        }

        public void AutoDiscoverAndProduce(string topic)
        {
            App.Current?.Dispatcher?.Invoke(() =>
            {
                _ = _viewModel.AutoDiscoverAndProduceAsync(topic);
            });
        }

        public void SelectDiscoveredVideo(string url, string title)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            App.Current?.Dispatcher?.Invoke(() =>
            {
                _viewModel.VideoUrlInput = url;
                if (!string.IsNullOrWhiteSpace(title))
                {
                    _viewModel.CustomTopic = title;
                }
                _ = _viewModel.DownloadAndAutoProduceAsync(url);
            });
        }

        public void RenderFinalShort()
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                _ = _viewModel.ExecuteRenderFinalShortAsync();
            });
        }

        public void CancelProcess()
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                if (_viewModel.CancelCommand.CanExecute(null))
                {
                    _viewModel.CancelCommand.Execute(null);
                }
            });
        }

        public string GetInitialStateJson()
        {
            var personas = _viewModel.Personas.Select(p => new
            {
                id = p.Id,
                name = p.Name,
                description = p.Description,
                tone = p.VoiceTone,
                isDefault = p.IsDefault
            }).ToList();

            var state = new
            {
                personas = personas,
                selectedPersonaId = _viewModel.SelectedPersona?.Id ?? "mad_muse",
                geminiApiUrl = _viewModel.GeminiApiUrl,
                geminiModels = _viewModel.GeminiModels.ToList(),
                selectedGeminiModel = _viewModel.SelectedGeminiModel,
                ttsProviders = _viewModel.TtsProviders.ToList(),
                selectedTtsProvider = _viewModel.SelectedTtsProvider,
                ttsVoices = _viewModel.TtsVoices.ToList(),
                selectedTtsVoice = _viewModel.SelectedTtsVoice,
                speechRate = _viewModel.SpeechRate,
                speechPitch = _viewModel.SpeechPitch,
                stylePresets = _viewModel.StylePresets.ToList(),
                selectedStylePreset = _viewModel.SelectedStylePreset,
                resolutions = _viewModel.Resolutions.ToList(),
                selectedResolution = _viewModel.SelectedResolution,
                subtitlePresets = _viewModel.SubtitlePresets.ToList(),
                selectedSubtitlePreset = _viewModel.SelectedSubtitlePreset,
                enableAutoCut = _viewModel.EnableAutoCut,
                enableAutoZoom = _viewModel.EnableAutoZoom,
                enableAutoPan = _viewModel.EnableAutoPan,
                enableAutoSubtitle = _viewModel.EnableAutoSubtitle,
                enableKeywordHighlight = _viewModel.EnableKeywordHighlight,
                enableMusicDucking = _viewModel.EnableMusicDucking,
                enableSoundEffects = _viewModel.EnableSoundEffects,
                geminiApiKey = _viewModel.GeminiApiKey,
                ttsApiKey = _viewModel.TtsApiKey,
                outputDirectory = _viewModel.OutputDirectory,
                hasVideo = _viewModel.HasVideo,
                videoPath = _viewModel.SelectedVideoPath,
                videoUrl = ToMediaUrl(_viewModel.SelectedVideoPath),
                videoName = _viewModel.VideoInfo?.FileName ?? "",
                videoDuration = _viewModel.VideoInfo?.DurationSeconds ?? 0,
                videoWidth = _viewModel.VideoInfo?.Width ?? 0,
                videoHeight = _viewModel.VideoInfo?.Height ?? 0,
                videoFps = _viewModel.VideoInfo?.Fps ?? 0,
                durationModes = _viewModel.DurationModes.ToList(),
                selectedDurationMode = _viewModel.SelectedDurationMode,
                videoUrlInput = _viewModel.VideoUrlInput,
                isSearchingVideos = _viewModel.IsSearchingVideos,
                discoveredVideos = _viewModel.DiscoveredVideos.ToList(),
                customTopic = _viewModel.CustomTopic,
                enableBackgroundMusic = _viewModel.EnableBackgroundMusic,
                backgroundMusicVolume = _viewModel.BackgroundMusicVolume,
                hookText = _viewModel.HookText,
                scriptText = _viewModel.ScriptText,
                statusMessage = _viewModel.StatusMessage,
                progressPercentage = _viewModel.ProgressPercentage,
                isBusy = _viewModel.IsBusy,
                canCancel = _viewModel.CanCancel,
                hasFinalVideo = _viewModel.HasFinalVideo,
                finalVideoPath = _viewModel.FinalVideoPath,
                finalVideoUrl = ToMediaUrl(_viewModel.FinalVideoPath),
                hasError = _viewModel.HasError,
                errorMessage = _viewModel.ErrorMessage,
                currentProject = _viewModel.CurrentProject != null ? SerializeProject(_viewModel.CurrentProject) : null,
                aspectRatio = _viewModel.CurrentProject?.AspectRatio ?? "9:16"
            };

            return JsonSerializer.Serialize(state);
        }

        public void SetPersona(string personaId)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                var persona = _viewModel.Personas.FirstOrDefault(p => p.Id == personaId);
                if (persona != null) _viewModel.SelectedPersona = persona;
            });
        }

        public void SetStylePreset(string preset)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                _viewModel.SelectedStylePreset = preset;
            });
        }

        public void SetResolution(string resolution)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                _viewModel.SelectedResolution = resolution;
            });
        }

        public void SetSubtitlePreset(string preset)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                _viewModel.SelectedSubtitlePreset = preset;
            });
        }

        public void SetDurationMode(string mode)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                _viewModel.SelectedDurationMode = mode;
            });
        }

        public void SetCustomTopic(string topic)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                _viewModel.CustomTopic = topic ?? string.Empty;
            });
        }

        public void SetBackgroundMusic(bool enabled, double volume)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                _viewModel.EnableBackgroundMusic = enabled;
                _viewModel.BackgroundMusicVolume = Math.Clamp(volume, 0.0, 1.0);
            });
        }

        public void SetTtsProvider(string provider)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                _viewModel.SelectedTtsProvider = provider;
            });
        }

        public void SetTtsVoice(string voice)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                _viewModel.SelectedTtsVoice = voice;
            });
        }

        public void SetSpeechRate(double rate)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                _viewModel.SpeechRate = Math.Clamp(rate, 0.5, 2.0);
            });
        }

        public void SetSpeechPitch(double pitch)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                _viewModel.SpeechPitch = Math.Clamp(pitch, -10.0, 10.0);
            });
        }

        public void SetGeminiModel(string model)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                _viewModel.SelectedGeminiModel = model;
            });
        }

        public void UpdateScript(string script)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                _viewModel.ScriptText = script ?? string.Empty;
            });
        }

        public void UpdateHook(string hook)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                _viewModel.HookText = hook ?? string.Empty;
            });
        }

        public void ClearScript()
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                _viewModel.ScriptText = string.Empty;
                _viewModel.HookText = string.Empty;
            });
        }

        public void ResetProjectState()
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                _viewModel.ResetProjectState();
            });
        }

        public void SetToggle(string featureName, bool enabled)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                switch (featureName.ToLowerInvariant())
                {
                    case "autocut":
                        _viewModel.EnableAutoCut = enabled;
                        break;
                    case "autozoom":
                        _viewModel.EnableAutoZoom = enabled;
                        break;
                    case "autopan":
                        _viewModel.EnableAutoPan = enabled;
                        break;
                    case "autosubtitle":
                        _viewModel.EnableAutoSubtitle = enabled;
                        break;
                    case "keywordhighlight":
                        _viewModel.EnableKeywordHighlight = enabled;
                        break;
                    case "musicducking":
                        _viewModel.EnableMusicDucking = enabled;
                        break;
                    case "soundeffects":
                        _viewModel.EnableSoundEffects = enabled;
                        break;
                    case "scenedetection":
                        _viewModel.EnableSceneDetection = enabled;
                        break;
                }
            });
        }

        public void SaveSettings(string geminiKey, string ttsKey)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                _viewModel.GeminiApiKey = geminiKey ?? string.Empty;
                _viewModel.TtsApiKey = ttsKey ?? string.Empty;
            });
        }

        public void SaveFullSettings(string geminiKey, string geminiModel, string ttsProvider, string ttsKey, string ttsVoice, string outputDir, string geminiApiUrl = "")
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrEmpty(geminiApiUrl)) _viewModel.GeminiApiUrl = geminiApiUrl;
                _viewModel.GeminiApiKey = geminiKey ?? string.Empty;
                if (!string.IsNullOrEmpty(geminiModel)) _viewModel.SelectedGeminiModel = geminiModel;
                if (!string.IsNullOrEmpty(ttsProvider)) _viewModel.SelectedTtsProvider = ttsProvider;
                _viewModel.TtsApiKey = ttsKey ?? string.Empty;
                if (!string.IsNullOrEmpty(ttsVoice)) _viewModel.SelectedTtsVoice = ttsVoice;
                if (!string.IsNullOrEmpty(outputDir)) _viewModel.OutputDirectory = outputDir;
            });
        }

        public void OpenOutputFolder()
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    string dir = _viewModel.OutputDirectory;
                    if (!string.IsNullOrEmpty(_viewModel.FinalVideoPath) && File.Exists(_viewModel.FinalVideoPath))
                    {
                        Process.Start("explorer.exe", $"/select,\"{_viewModel.FinalVideoPath}\"");
                        return;
                    }
                    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    {
                        dir = AppSettings.GetDefaultOutputDirectory();
                    }
                    Directory.CreateDirectory(dir);
                    Process.Start("explorer.exe", $"\"{dir}\"");
                }
                catch (Exception ex)
                {
                    _viewModel.SetError($"Không thể mở thư mục: {ex.Message}");
                }
            });
        }

        public void OpenFinalVideo()
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(_viewModel.FinalVideoPath) && File.Exists(_viewModel.FinalVideoPath))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = _viewModel.FinalVideoPath,
                            UseShellExecute = true
                        });
                    }
                }
                catch (Exception ex)
                {
                    _viewModel.SetError($"Không thể phát video hoàn thiện: {ex.Message}");
                }
            });
        }

        public string GetStatus() => _viewModel.StatusMessage;
        public bool IsProcessing() => _viewModel.IsBusy;

        // ==================== PROJECT & TOPIC METHODS ====================

        public static object SerializeProject(VideoProject p)
        {
            return new
            {
                id = p.Id,
                name = p.Name,
                title = p.Name,
                topic = p.Topic,
                personaId = p.PersonaId,
                aspectRatio = p.AspectRatio,
                outputWidth = p.OutputWidth,
                outputHeight = p.OutputHeight,
                resolution = $"{p.OutputWidth}x{p.OutputHeight}",
                hook = p.Hook,
                fullScript = p.FullScript,
                createdAt = p.CreatedAt.ToString("o"),
                updatedAt = p.UpdatedAt.ToString("o"),
                proxyVideoPath = p.ProxyVideoPath,
                proxyVideoUrl = ToMediaUrl(p.ProxyVideoPath),
                finalVideoPath = p.FinalVideoPath,
                finalVideoUrl = ToMediaUrl(p.FinalVideoPath),
                scenes = p.Scenes.Select(s => new
                {
                    id = s.SceneId,
                    sceneId = s.SceneId,
                    index = s.Index,
                    narration = s.Narration,
                    visualPrompt = s.VisualPrompt,
                    mediaPath = s.MediaPath,
                    mediaUrl = ToMediaUrl(s.MediaPath),
                    isVideoClip = s.IsVideoClip,
                    duration = s.Duration,
                    transitionType = s.TransitionType,
                    zoomDirection = s.ZoomDirection
                }).ToList(),
                audioSettings = p.AudioSettings != null ? new
                {
                    ttsProvider = p.AudioSettings.TtsProvider,
                    ttsVoice = p.AudioSettings.TtsVoice,
                    speechRate = p.AudioSettings.SpeechRate,
                    speechPitch = p.AudioSettings.SpeechPitch,
                    backgroundMusicPath = p.AudioSettings.BackgroundMusicPath,
                    backgroundMusicVolume = p.AudioSettings.BackgroundMusicVolume,
                    enableMusicDucking = p.AudioSettings.EnableMusicDucking,
                    enableSoundEffects = p.AudioSettings.EnableSoundEffects
                } : null,
                subtitleSettings = p.SubtitleSettings != null ? new
                {
                    presetName = p.SubtitleSettings.PresetName,
                    enableKeywordHighlight = p.SubtitleSettings.EnableKeywordHighlight,
                    fontSize = p.SubtitleSettings.FontSize,
                    primaryColor = p.SubtitleSettings.PrimaryColor,
                    highlightColor = p.SubtitleSettings.HighlightColor
                } : null
            };
        }

        public void CreateTopicVideo(string topic, string personaId, string aspectRatio, int sceneCount)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                _ = _viewModel.CreateProjectFromTopicAsync(topic, personaId, aspectRatio, sceneCount);
            });
        }

        public void RenderProject()
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                _ = _viewModel.RenderCurrentProjectAsync();
            });
        }

        public void SaveProject()
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                _ = _viewModel.SaveCurrentProjectAsync();
            });
        }

        public async Task<string> LoadProject(string idOrPath)
        {
            var proj = await _viewModel.LoadProjectAsync(idOrPath);
            if (proj == null) return "null";
            return JsonSerializer.Serialize(SerializeProject(proj));
        }

        public async Task<string> GetRecentProjects()
        {
            var list = await _viewModel.GetRecentProjectsAsync();
            var mapped = list.Select(p => new
            {
                id = p.Id,
                name = p.Name,
                title = p.Name,
                topic = p.Topic,
                aspectRatio = p.AspectRatio,
                sceneCount = p.Scenes.Count,
                hasFinalVideo = !string.IsNullOrEmpty(p.FinalVideoPath) && File.Exists(p.FinalVideoPath),
                finalVideoUrl = ToMediaUrl(p.FinalVideoPath),
                createdAt = p.CreatedAt.ToString("g"),
                updatedAt = p.UpdatedAt.ToString("g")
            }).ToList();
            return JsonSerializer.Serialize(mapped);
        }

        public string GetCurrentProjectJson()
        {
            if (_viewModel.CurrentProject == null) return "null";
            return JsonSerializer.Serialize(SerializeProject(_viewModel.CurrentProject));
        }

        public void UpdateSceneNarration(string sceneId, string narration)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                if (_viewModel.CurrentProject == null) return;
                var sc = _viewModel.CurrentProject.Scenes.FirstOrDefault(s => s.SceneId == sceneId);
                if (sc != null)
                {
                    sc.Narration = narration ?? string.Empty;
                    _viewModel.CurrentProject.UpdatedAt = DateTime.Now;
                    _ = _viewModel.SaveCurrentProjectAsync();
                }
            });
        }

        public void UpdateSceneVisualPrompt(string sceneId, string visualPrompt)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                if (_viewModel.CurrentProject == null) return;
                var sc = _viewModel.CurrentProject.Scenes.FirstOrDefault(s => s.SceneId == sceneId);
                if (sc != null)
                {
                    sc.VisualPrompt = visualPrompt ?? string.Empty;
                    _viewModel.CurrentProject.UpdatedAt = DateTime.Now;
                    _ = _viewModel.SaveCurrentProjectAsync();
                }
            });
        }

        public void SetAspectRatio(string aspectRatio)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                string norm = (aspectRatio ?? "").ToLowerInvariant().Trim();
                if (norm != "9:16" && norm != "16:9" && norm != "1:1" && norm != "original")
                    norm = "9:16";

                if (_viewModel.CurrentProject != null)
                {
                    _viewModel.CurrentProject.ApplyAspectRatio(norm);
                }
                _viewModel.SelectedResolution = norm switch
                {
                    "16:9" => "1920x1080 (16:9 Ngang YouTube/Facebook)",
                    "1:1" => "1080x1080 (1:1 Vuông Instagram)",
                    "original" => "Original (Giữ nguyên kích thước video gốc)",
                    _ => "1080x1920 (9:16 Dọc TikTok/Shorts)"
                };
            });
        }

        // ==================== CAMELCASE WRAPPERS FOR JS BRIDGE ====================

        public void selectVideo() => SelectVideoFile();
        public void loadVideo(string filePath) => LoadVideoPath(filePath);
        public void analyzeVideo() => StartAutoEdit();
        public void generateScript() => GenerateScriptOnly();
        public void generateVoice()
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                if (_viewModel.TestTtsCommand.CanExecute(null))
                {
                    _viewModel.TestTtsCommand.Execute(null);
                }
            });
        }
        public void createVideo()
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrEmpty(_viewModel.SelectedVideoPath))
                {
                    _ = _viewModel.ExecuteAutoEditPipelineAsync();
                }
                else
                {
                    _ = _viewModel.ExecuteRenderFinalShortAsync();
                }
            });
        }
        public double getProgress() => _viewModel.ProgressPercentage;
        public void cancelProcess() => CancelProcess();
        public void openOutputFolder() => OpenOutputFolder();
        public void openFinalVideo() => OpenFinalVideo();
        public void saveSettings(string geminiKey, string ttsKey, string model = "", string ttsVoice = "", string outDir = "", string geminiApiUrl = "")
        {
            SaveFullSettings(geminiKey, model, "", ttsKey, ttsVoice, outDir, geminiApiUrl);
        }
        public string loadSettings()
        {
            var s = new
            {
                geminiApiUrl = _viewModel.GeminiApiUrl,
                geminiApiKey = _viewModel.GeminiApiKey,
                geminiModel = _viewModel.SelectedGeminiModel,
                ttsApiKey = _viewModel.TtsApiKey,
                ttsVoice = _viewModel.SelectedTtsVoice,
                ttsProvider = _viewModel.SelectedTtsProvider,
                outputDir = _viewModel.OutputDirectory,
                speechRate = _viewModel.SpeechRate,
                speechPitch = _viewModel.SpeechPitch
            };
            return JsonSerializer.Serialize(s);
        }

        // Legacy events preserved for backwards-compatibility
        public delegate void FileSelectedHandler(string filename);
        public event FileSelectedHandler? FileSelected;

        public delegate void StatusChangedHandler(string status);
        public event StatusChangedHandler? StatusChanged;

        public delegate void ProcessingChangedHandler(bool isProcessing);
        public event ProcessingChangedHandler? ProcessingChanged;
    }
}

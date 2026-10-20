using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using VideoNarrator.ViewModels;

namespace VideoNarrator
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private UIBridge? _uiBridge;

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            
            InitializeWebViewAsync();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_DLGMODALFRAME);
                SendMessage(hwnd, WM_SETICON, new IntPtr(0), IntPtr.Zero);
                SendMessage(hwnd, WM_SETICON, new IntPtr(1), IntPtr.Zero);
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
            }
            catch
            {
                // Ignore Win32 failure
            }
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_DLGMODALFRAME = 0x0001;
        private const int SWP_NOSIZE = 0x0001;
        private const int SWP_NOMOVE = 0x0002;
        private const int SWP_NOZORDER = 0x0004;
        private const int SWP_FRAMECHANGED = 0x0020;
        private const uint WM_SETICON = 0x0080;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        private async void InitializeWebViewAsync()
        {
            try
            {
                var userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoNarrator", "WebView2");
                var options = new CoreWebView2EnvironmentOptions
                {
                    AdditionalBrowserArguments = 
                        "--allow-file-access-from-files " +
                        "--autoplay-policy=no-user-gesture-required " +
                        "--disable-web-security " +
                        "--disable-features=CalculateNativeWinOcclusion,IntensiveWakeUpThrottling " +
                        "--disable-backgrounding-occluded-windows " +
                        "--disable-background-timer-throttling " +
                        "--disable-renderer-backgrounding " +
                        "--enable-gpu-rasterization " +
                        "--enable-zero-copy " +
                        "--ignore-gpu-blocklist " +
                        "--force-gpu-rasterization"
                };
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
                await webView.EnsureCoreWebView2Async(env);

                // Prevent white/black flash during tab switches and activation
                webView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(19, 19, 20);

                // Keep DevTools available for inspection
                webView.CoreWebView2.Settings.AreDevToolsEnabled = true;

                // Local media streamer handler (Allows previewing any local file safely via https://localmedia.internal/?path=...)
                webView.CoreWebView2.AddWebResourceRequestedFilter("https://localmedia.internal/*", CoreWebView2WebResourceContext.All);
                webView.CoreWebView2.WebResourceRequested += (sender, args) =>
                {
                    try
                    {
                        var uri = new Uri(args.Request.Uri);
                        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                        string? filePath = query["path"];
                        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                        {
                            var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            string ext = Path.GetExtension(filePath).ToLowerInvariant();
                            string mime = ext switch
                            {
                                ".mp4" => "video/mp4",
                                ".webm" => "video/webm",
                                ".mkv" => "video/x-matroska",
                                ".mov" => "video/quicktime",
                                ".avi" => "video/x-msvideo",
                                ".mp3" => "audio/mpeg",
                                ".wav" => "audio/wav",
                                ".aac" => "audio/aac",
                                ".jpg" or ".jpeg" => "image/jpeg",
                                ".png" => "image/png",
                                _ => "application/octet-stream"
                            };

                            var response = webView.CoreWebView2.Environment.CreateWebResourceResponse(
                                fileStream,
                                200,
                                "OK",
                                $"Content-Type: {mime}\r\nAccept-Ranges: bytes\r\nAccess-Control-Allow-Origin: *\r\n");
                            args.Response = response;
                        }
                    }
                    catch
                    {
                        // Ignore stream errors
                    }
                };

                // Setup Bridge
                _uiBridge = new UIBridge(_viewModel);
                _uiBridge.AttachWebView(webView.CoreWebView2);
                webView.CoreWebView2.AddHostObjectToScript("bridge", _uiBridge);

                // Wire up auto preview loading
                _viewModel.RequestLoadVideoPreview = (previewPath) =>
                {
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        _uiBridge?.NotifyPreviewReady(previewPath);
                    });
                };

                // Find local index.html with multiple fallback paths
                string htmlPath = ResolveHtmlPath();

                if (File.Exists(htmlPath))
                {
                    webView.NavigationCompleted += async (s, e) =>
                    {
                        try
                        {
                            if (App.DemoDataRequested)
                            {
                                await Task.Delay(800);
                                string demoScript = "🌍 [00:00 - 00:02] ĐÂY LÀ CÁCH MỘT SỐ NGƯỜI GIAN LẬN KHI ĐÁNH GOLF...\\n⚡ [00:02 - 00:05] Luật chỉ cho bạn đúng 3 phút để tìm quả bóng trong bụi rậm.\\n🔍 [00:05 - 00:09] Thay vì mò mẫm, họ đeo một chiếc kính lọc quang phổ đặc biệt.\\n🎯 [00:09 - 00:13] Mọi cành lá bị triệt tiêu sắc xanh, chỉ còn quả bóng phát sáng rực rỡ!\\n👉 [00:13 - 00:15] Bấm like nếu bạn thấy mánh khóe này quá đỉnh!";
                                string js = $@"
                                    const scriptEl = document.getElementById('scriptInput');
                                    if (scriptEl) {{ scriptEl.value = '{demoScript}'; }}
                                    const topicEl = document.getElementById('topicInput');
                                    if (topicEl) {{ topicEl.value = 'Mẹo gian lận tinh vi khi đánh golf'; }}
                                    const personaSelect = document.getElementById('personaSelect');
                                    if (personaSelect) {{ personaSelect.value = 'the_gioi_co_gi_hay'; }}
                                ";
                                await webView.CoreWebView2.ExecuteScriptAsync(js);
                            }

                            if (App.OpenSettingsRequested)
                            {
                                await Task.Delay(1000);
                                await webView.CoreWebView2.ExecuteScriptAsync("document.getElementById('settingsModal')?.classList.remove('hidden');");
                            }
                        }
                        catch { }
                    };

                    webView.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);
                }
                else
                {
                    MessageBox.Show($"Could not find UI files at {htmlPath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize WebView2: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string ResolveHtmlPath()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "index.html"),
                Path.Combine(Directory.GetCurrentDirectory(), "VideoNarrator", "wwwroot", "index.html"),
                Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "index.html"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot", "index.html"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "VideoNarrator", "wwwroot", "index.html")
            };

            foreach (var candidate in candidates)
            {
                try
                {
                    string full = Path.GetFullPath(candidate);
                    if (File.Exists(full))
                    {
                        return full;
                    }
                }
                catch { }
            }

            return Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
        }

        private void OnWindowDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private async void OnWindowDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                {
                    string filePath = files[0];
                    if (File.Exists(filePath))
                    {
                        await _viewModel.LoadVideoFileAsync(filePath);
                    }
                }
            }
        }
    }
}
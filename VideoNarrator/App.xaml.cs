using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using VideoNarrator.Services;
using VideoNarrator.Services.PersonalityEngine;
using VideoNarrator.ViewModels;

namespace VideoNarrator
{
    public partial class App : Application
    {
        private ServiceProvider? _serviceProvider;

        public static bool DemoDataRequested { get; private set; }
        public static bool OpenSettingsRequested { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (e.Args.Length > 0)
            {
                foreach (var arg in e.Args)
                {
                    if (string.Equals(arg, "--demo-data", StringComparison.OrdinalIgnoreCase))
                        DemoDataRequested = true;
                    else if (string.Equals(arg, "--open-settings", StringComparison.OrdinalIgnoreCase))
                        OpenSettingsRequested = true;
                }
            }

            if (e.Args.Length > 0 && e.Args[0] == "--test")
            {
                var reports = VideoNarrator.Tests.PersonalityEngineTestSuite.RunAllTests();
                string reportPath = Path.Combine(AppContext.BaseDirectory, "test_report.txt");
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"=== KẾT QUẢ KIỂM THỬ PERSONALITY & NARRATION ENGINE ({reports.Count(r => r.Passed)}/{reports.Count} TIÊU CHÍ ĐẠT) ===");
                foreach (var r in reports)
                {
                    sb.AppendLine($"[{(r.Passed ? "PASS" : "FAIL")}] {r.TestName}\n    -> {r.Details}\n");
                }
                File.WriteAllText(reportPath, sb.ToString(), System.Text.Encoding.UTF8);
                Console.WriteLine(sb.ToString());
                Shutdown(0);
                return;
            }

            if (e.Args.Length > 0 && e.Args[0] == "--test-video")
            {
                try
                {
                    string videoPath = e.Args.Length > 1 ? string.Join(" ", System.Linq.Enumerable.Skip(e.Args, 1)).Trim('\"') : @"C:\Users\nguye\Downloads\Download (24).mp4";
                    if (!File.Exists(videoPath))
                    {
                        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "test_video_result.txt"), $"ERROR: Video không tồn tại: {videoPath}", System.Text.Encoding.UTF8);
                        Shutdown(1);
                        return;
                    }
                    var sc = new ServiceCollection();
                    ConfigureServices(sc);
                    using var sp = sc.BuildServiceProvider();
                    var eps = sp.GetRequiredService<IEditingPlanService>();
                    var ss = sp.GetRequiredService<ISettingsService>();
                    var vs = sp.GetRequiredService<IVideoService>();
                    var settings = ss.LoadSettings();
                    var info = Task.Run(() => vs.GetVideoInfoAsync(videoPath)).GetAwaiter().GetResult();
                    double dur = info.DurationSeconds;
                    var plan = Task.Run(() => eps.GenerateEditingPlanAsync(
                        settings.GeminiApiKey,
                        settings.GeminiModel,
                        videoPath,
                        dur,
                        "TikTok Shorts",
                        null,
                        settings,
                        null,
                        null,
                        (msg, p) => Console.WriteLine($"[{p:F0}%] {msg}")
                    )).GetAwaiter().GetResult();
                    var sbOut = new System.Text.StringBuilder();
                    sbOut.AppendLine($"Testing with video: {videoPath}, duration: {dur:F1}s, Model: {settings.GeminiModel}");
                    sbOut.AppendLine("\n=== GENERATED NARRATION SCRIPT ===");
                    sbOut.AppendLine(plan.NarrationScript);
                    int wc = plan.NarrationScript.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                    double estSec = wc / 4.1;
                    sbOut.AppendLine($"\nWord Count: {wc} words (~{estSec:F1}s at 4.1 wps for video {dur:F1}s)");
                    sbOut.AppendLine("\n=== HOOK ===");
                    sbOut.AppendLine(plan.Hook);
                    sbOut.AppendLine("=================================");
                    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "test_video_result.txt"), sbOut.ToString(), System.Text.Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "test_video_result.txt"), $"ERROR: {ex.Message}\n{ex}", System.Text.Encoding.UTF8);
                }
                Shutdown(0);
                return;
            }

            // Global exception catching to ensure the application NEVER crashes abruptly
            DispatcherUnhandledException += (s, args) =>
            {
                try
                {
                    string logPath = Path.Combine(AppContext.BaseDirectory, "crash.log");
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] DispatcherUnhandledException:\n{args.Exception}\n\n");
                    MessageBox.Show($"Đã xảy ra lỗi:\n{args.Exception.Message}\n\nChi tiết kỹ thuật đã được ghi lại tại tệp crash.log.",
                        "Thông báo lỗi ứng dụng", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch { }
                args.Handled = true; // Prevents process termination
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                try
                {
                    string logPath = Path.Combine(AppContext.BaseDirectory, "crash.log");
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] AppDomain.UnhandledException:\n{args.ExceptionObject}\n\n");
                }
                catch { }
            };

            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                try
                {
                    string logPath = Path.Combine(AppContext.BaseDirectory, "crash.log");
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TaskScheduler.UnobservedTaskException:\n{args.Exception}\n\n");
                }
                catch { }
                args.SetObserved();
            };

            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<HttpClient>(sp => new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(3)
            });

            services.AddSingleton<IFileService, FileService>();
            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddSingleton<IFfmpegService, FfmpegService>();
            services.AddSingleton<IVideoService, VideoService>();
            services.AddSingleton<IVideoDownloadService, VideoDownloadService>();
            services.AddSingleton<IGeminiService, GeminiService>();
            services.AddSingleton<ITtsService, TtsService>();

            // AI Auto Video Editing Services
            services.AddSingleton<IVideoProbeService, VideoProbeService>();
            services.AddSingleton<ISceneDetectionService, SceneDetectionService>();
            services.AddSingleton<IEditingPlanService, EditingPlanService>();
            services.AddSingleton<ISubtitleService, SubtitleService>();
            services.AddSingleton<IAudioMixService, AudioMixService>();
            services.AddSingleton<IBRollService, BRollService>();
            services.AddSingleton<IProxyVideoService, ProxyVideoService>();
            services.AddSingleton<IVideoComposerService, VideoComposerService>();
            services.AddSingleton<IImageGenService, ImageGenService>();
            services.AddSingleton<IProjectService, ProjectService>();
            services.AddSingleton<ITopicToVideoService, TopicToVideoService>();

            // AI Video Script Personality Engine Services
            services.AddSingleton<IStyleEngine, StyleEngine>();
            services.AddSingleton<IFewShotManager, FewShotManager>();
            services.AddSingleton<IPromptVersionManager, PromptVersionManager>();
            services.AddSingleton<IPersonaManager, PersonaManager>();
            services.AddSingleton<ISystemPromptBuilder, SystemPromptBuilder>();
            services.AddSingleton<IStyleValidator, StyleValidator>();
            services.AddSingleton<IOutputFormatter, OutputFormatter>();
            services.AddSingleton<IMultiStageNarrationEngine, MultiStageNarrationEngine>();
            services.AddSingleton<GeminiAiProvider>();
            services.AddSingleton<OpenAiCompatibleProvider>();
            services.AddSingleton<IScriptGenerator, ScriptGenerator>();

            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _serviceProvider?.Dispose();
            base.OnExit(e);
        }
    }
}

using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VideoNarrator.Helpers;

namespace VideoNarrator.Services
{
    public interface IImageGenService
    {
        Task<string> GenerateImageForSceneAsync(
            string visualPrompt,
            int width,
            int height,
            string outputDirectory,
            int sceneIndex,
            CancellationToken cancellationToken = default);
    }

    public class ImageGenService : IImageGenService
    {
        private readonly HttpClient _httpClient;
        private readonly IFileService _fileService;
        private readonly IFfmpegService _ffmpegService;

        public ImageGenService(HttpClient httpClient, IFileService fileService, IFfmpegService ffmpegService)
        {
            _httpClient = httpClient;
            _fileService = fileService;
            _ffmpegService = ffmpegService;
        }

        public async Task<string> GenerateImageForSceneAsync(
            string visualPrompt,
            int width,
            int height,
            string outputDirectory,
            int sceneIndex,
            CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(outputDirectory);
            string fileName = $"scene_{sceneIndex + 1:D2}_{Guid.NewGuid():N}.jpg";
            string targetPath = Path.Combine(outputDirectory, fileName);

            // Clean prompt for AI image generation
            string prompt = string.IsNullOrWhiteSpace(visualPrompt)
                ? "cinematic scene, ultra detailed 8k photography, dramatic lighting"
                : visualPrompt.Trim();

            // Append quality enhancers
            string enhancedPrompt = $"{prompt}, cinematic, 8k resolution, highly detailed, photorealistic, masterpiece";
            string encodedPrompt = Uri.EscapeDataString(enhancedPrompt);

            int seed = Random.Shared.Next(10000, 999999);
            string url = $"https://image.pollinations.ai/prompt/{encodedPrompt}?width={width}&height={height}&nologo=true&enhance=true&seed={seed}";

            try
            {
                _fileService.Log($"Đang tải ảnh AI cho Cảnh {sceneIndex + 1}: {url}");
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(25)); // Timeout after 25s per image

                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseContentRead, cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
                    if (bytes != null && bytes.Length > 2048) // Valid image usually > 2KB
                    {
                        await File.WriteAllBytesAsync(targetPath, bytes, cancellationToken);
                        _fileService.Log($"Đã tải ảnh AI Cảnh {sceneIndex + 1} thành công ({bytes.Length} bytes): {targetPath}");
                        return targetPath;
                    }
                }
            }
            catch (Exception ex)
            {
                _fileService.Log($"Lỗi tải ảnh AI qua Pollinations ({ex.Message}), kích hoạt fallback tạo ảnh nền...", "WARN");
            }

            // Fallback: Create high quality cinematic gradient image via FFmpeg if network failed
            try
            {
                await CreateGradientFallbackImageAsync(width, height, targetPath, sceneIndex, cancellationToken);
                return targetPath;
            }
            catch (Exception ex)
            {
                _fileService.Log($"Lỗi tạo ảnh fallback: {ex.Message}", "ERROR");
                throw;
            }
        }

        private async Task CreateGradientFallbackImageAsync(
            int width,
            int height,
            string outputPath,
            int sceneIndex,
            CancellationToken cancellationToken)
        {
            string[] colors = { "#0f172a", "#1e1b4b", "#14532d", "#701a75", "#7c2d12", "#134e4a" };
            string color = colors[sceneIndex % colors.Length];
            string qOut = ProcessHelper.QuoteArgument(outputPath);

            // Generates a 1-frame color image via FFmpeg
            string args = $"-y -f lavfi -i color=c={color}:s={width}x{height}:d=1 -frames:v 1 {qOut}";
            await ProcessHelper.RunProcessAsync(_ffmpegService.FfmpegPath, args, cancellationToken);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VideoNarrator.Models;

namespace VideoNarrator.Services
{
    public interface IBRollProvider
    {
        Task<string?> FindBRollClipAsync(BRollRequest request, string? bRollFolder, CancellationToken cancellationToken = default);
    }

    public interface IBRollService
    {
        Task<List<BRollRequest>> ResolveBRollClipsAsync(List<BRollRequest> requests, string? bRollFolder, CancellationToken cancellationToken = default);
    }

    public class BRollService : IBRollService, IBRollProvider
    {
        private readonly IFileService _fileService;
        private static readonly string[] SupportedExtensions = { ".mp4", ".mov", ".webm", ".jpg", ".jpeg", ".png", ".webp" };

        public BRollService(IFileService fileService)
        {
            _fileService = fileService;
        }

        public Task<string?> FindBRollClipAsync(BRollRequest request, string? bRollFolder, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(bRollFolder) || !Directory.Exists(bRollFolder))
            {
                _fileService.Log("B-roll unavailable, using original footage.");
                return Task.FromResult<string?>(null);
            }

            try
            {
                var files = Directory.GetFiles(bRollFolder, "*.*", SearchOption.AllDirectories)
                    .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .ToList();

                if (files.Count == 0)
                {
                    _fileService.Log("B-roll unavailable, using original footage.");
                    return Task.FromResult<string?>(null);
                }

                string query = request.Query.ToLowerInvariant().Trim();
                var queryTokens = query.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);

                // Find file whose name contains most query tokens
                string? bestMatch = null;
                int maxScore = 0;

                foreach (var file in files)
                {
                    string name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                    int score = 0;
                    foreach (var token in queryTokens)
                    {
                        if (name.Contains(token)) score++;
                    }

                    if (score > maxScore)
                    {
                        maxScore = score;
                        bestMatch = file;
                    }
                }

                if (bestMatch != null && maxScore > 0)
                {
                    _fileService.Log($"Tìm thấy B-roll khớp query '{request.Query}': {bestMatch}");
                    return Task.FromResult<string?>(bestMatch);
                }

                // If no token matched, pick first available clip if any
                _fileService.Log($"Không tìm thấy B-roll chính xác cho query '{request.Query}'. B-roll unavailable, using original footage.");
                return Task.FromResult<string?>(null);
            }
            catch (Exception ex)
            {
                _fileService.Log($"Lỗi quét thư mục B-roll: {ex.Message}. B-roll unavailable, using original footage.", "WARN", ex);
                return Task.FromResult<string?>(null);
            }
        }

        public async Task<List<BRollRequest>> ResolveBRollClipsAsync(List<BRollRequest> requests, string? bRollFolder, CancellationToken cancellationToken = default)
        {
            if (requests == null || requests.Count == 0) return new List<BRollRequest>();

            var resolved = new List<BRollRequest>();
            foreach (var req in requests)
            {
                string? path = await FindBRollClipAsync(req, bRollFolder, cancellationToken);
                req.MatchedFilePath = path;
                resolved.Add(req);
            }

            return resolved;
        }
    }
}

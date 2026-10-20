using System;
using System.IO;
using VideoNarrator.Models;

namespace VideoNarrator.Services
{
    public interface IFileService
    {
        string CreateTempDirectory();
        void DeleteTempDirectory(string tempPath);
        string GetUniqueOutputPath(string outputDirectory, string baseFileName, string suffix = "_narrated", string extension = ".mp4");
        void Log(string message, string level = "INFO", Exception? ex = null);
        string LogFilePath { get; }
    }

    public class FileService : IFileService
    {
        private readonly string _logsDirectory;
        private readonly string _logFilePath;
        private readonly object _logLock = new();

        public string LogFilePath => _logFilePath;

        public FileService()
        {
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VideoNarrator");
            _logsDirectory = Path.Combine(appData, "logs");
            Directory.CreateDirectory(_logsDirectory);
            _logFilePath = Path.Combine(_logsDirectory, "app.log");
        }

        public string CreateTempDirectory()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "VideoNarrator", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            return tempRoot;
        }

        public void DeleteTempDirectory(string tempPath)
        {
            try
            {
                if (Directory.Exists(tempPath))
                {
                    Directory.Delete(tempPath, recursive: true);
                }
            }
            catch (Exception ex)
            {
                Log($"Không thể xóa thư mục tạm thời {tempPath}: {ex.Message}", "WARN", ex);
            }
        }

        public string GetUniqueOutputPath(string outputDirectory, string originalFileName, string suffix = "_narrated", string extension = ".mp4")
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                outputDirectory = AppSettings.GetDefaultOutputDirectory();
            }
            Directory.CreateDirectory(outputDirectory);

            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(originalFileName);
            string candidateName = $"{fileNameWithoutExt}{suffix}{extension}";
            string fullPath = Path.Combine(outputDirectory, candidateName);

            int counter = 1;
            while (File.Exists(fullPath))
            {
                candidateName = $"{fileNameWithoutExt}{suffix}_{counter}{extension}";
                fullPath = Path.Combine(outputDirectory, candidateName);
                counter++;
            }

            return fullPath;
        }

        public void Log(string message, string level = "INFO", Exception? ex = null)
        {
            try
            {
                lock (_logLock)
                {
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    string logEntry = $"[{timestamp}] [{level}] {message}";
                    if (ex != null)
                    {
                        logEntry += Environment.NewLine + $"Exception: {ex.GetType().FullName}: {ex.Message}" +
                                    Environment.NewLine + ex.StackTrace;
                    }

                    File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);
                }
            }
            catch
            {
                // Avoid crashing on logging failure
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using VideoNarrator.Helpers;
using VideoNarrator.Models;

namespace VideoNarrator.Services
{
    public interface IProjectService
    {
        string ProjectsDirectory { get; }
        Task<VideoProject> SaveProjectAsync(VideoProject project);
        Task<VideoProject?> LoadProjectAsync(string projectIdOrPath);
        Task<List<VideoProject>> GetRecentProjectsAsync();
        Task DeleteProjectAsync(string projectId);
    }

    public class ProjectService : IProjectService
    {
        private readonly string _projectsDir;
        private readonly IFileService _fileService;

        public string ProjectsDirectory => _projectsDir;

        public ProjectService(IFileService fileService)
        {
            _fileService = fileService;
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _projectsDir = Path.Combine(appData, "VideoNarrator", "projects");
            Directory.CreateDirectory(_projectsDir);
        }

        public async Task<VideoProject> SaveProjectAsync(VideoProject project)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (string.IsNullOrWhiteSpace(project.Id)) project.Id = Guid.NewGuid().ToString("N");
            
            project.UpdatedAt = DateTime.Now;

            string filePath = Path.Combine(_projectsDir, $"{project.Id}.vnproj");
            string json = JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true });
            
            await File.WriteAllTextAsync(filePath, json);
            _fileService.Log($"Đã lưu dự án '{project.Name}' ({project.Id}): {filePath}");
            
            return project;
        }

        public async Task<VideoProject?> LoadProjectAsync(string projectIdOrPath)
        {
            string filePath = File.Exists(projectIdOrPath)
                ? projectIdOrPath
                : Path.Combine(_projectsDir, $"{projectIdOrPath}.vnproj");

            if (!File.Exists(filePath))
            {
                return null;
            }

            try
            {
                string json = await File.ReadAllTextAsync(filePath);
                var project = JsonHelper.DeserializeSafe<VideoProject>(json);
                return project;
            }
            catch (Exception ex)
            {
                _fileService.Log($"Lỗi mở dự án {filePath}: {ex.Message}", "ERROR", ex);
                return null;
            }
        }

        public async Task<List<VideoProject>> GetRecentProjectsAsync()
        {
            var result = new List<VideoProject>();
            if (!Directory.Exists(_projectsDir)) return result;

            var files = Directory.GetFiles(_projectsDir, "*.vnproj")
                .OrderByDescending(File.GetLastWriteTime)
                .Take(20);

            foreach (var f in files)
            {
                try
                {
                    string json = await File.ReadAllTextAsync(f);
                    var p = JsonHelper.DeserializeSafe<VideoProject>(json);
                    if (p != null)
                    {
                        result.Add(p);
                    }
                }
                catch { }
            }

            return result;
        }

        public Task DeleteProjectAsync(string projectId)
        {
            string filePath = Path.Combine(_projectsDir, $"{projectId}.vnproj");
            if (File.Exists(filePath))
            {
                try { File.Delete(filePath); } catch { }
            }
            return Task.CompletedTask;
        }
    }
}

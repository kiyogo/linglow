using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VideoNarrator.Helpers
{
    public class ProcessResult
    {
        public int ExitCode { get; set; }
        public string StandardOutput { get; set; } = string.Empty;
        public string StandardError { get; set; } = string.Empty;
        public bool IsSuccess => ExitCode == 0;
    }

    public static class ProcessHelper
    {
        public static string QuoteArgument(string argument)
        {
            if (string.IsNullOrEmpty(argument))
            {
                return "\"\"";
            }

            if (!argument.Contains(' ') && !argument.Contains('\t') && !argument.Contains('"'))
            {
                return argument;
            }

            var sb = new StringBuilder();
            sb.Append('"');

            for (int i = 0; i < argument.Length; i++)
            {
                int backslashCount = 0;
                while (i < argument.Length && argument[i] == '\\')
                {
                    backslashCount++;
                    i++;
                }

                if (i == argument.Length)
                {
                    sb.Append('\\', backslashCount * 2);
                    break;
                }
                else if (argument[i] == '"')
                {
                    sb.Append('\\', backslashCount * 2 + 1);
                    sb.Append('"');
                }
                else
                {
                    sb.Append('\\', backslashCount);
                    sb.Append(argument[i]);
                }
            }

            sb.Append('"');
            return sb.ToString();
        }

        public static async Task<ProcessResult> RunProcessAsync(
            string executablePath,
            string arguments,
            CancellationToken cancellationToken = default,
            Action<string>? onOutputLine = null,
            Action<string>? onErrorLine = null)
        {
            if (!File.Exists(executablePath))
            {
                throw new FileNotFoundException($"Không tìm thấy tệp thực thi: {executablePath}", executablePath);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            var stdoutBuilder = new StringBuilder();
            var stderrBuilder = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    stdoutBuilder.AppendLine(e.Data);
                    onOutputLine?.Invoke(e.Data);
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    stderrBuilder.AppendLine(e.Data);
                    onErrorLine?.Invoke(e.Data);
                }
            };

            if (!process.Start())
            {
                throw new InvalidOperationException($"Không thể khởi động tiến trình: {executablePath}");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var registration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Ignore kill errors if already terminated
                }
            });

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Ignore
                }
                throw;
            }

            // Đảm bảo toàn bộ luồng stdout và stderr bất đồng bộ đã được đẩy hết vào buffer
            process.WaitForExit();

            return new ProcessResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = stdoutBuilder.ToString(),
                StandardError = stderrBuilder.ToString()
            };
        }
    }
}

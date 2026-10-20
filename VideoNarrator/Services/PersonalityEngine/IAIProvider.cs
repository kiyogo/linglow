using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VideoNarrator.Models;

namespace VideoNarrator.Services.PersonalityEngine
{
    public interface IAIProvider
    {
        string ProviderName { get; }
        Task<AIResponse> GenerateAsync(AIRequest request, string apiKey, string? baseUrl = null, CancellationToken cancellationToken = default);
        Task<bool> TestConnectionAsync(string apiKey, string model, string? baseUrl = null, CancellationToken cancellationToken = default);
    }

    public class GeminiAiProvider : IAIProvider
    {
        private readonly HttpClient _httpClient;
        private readonly IFileService _fileService;
        private const string DefaultBaseUrl = "https://generativelanguage.googleapis.com";

        public string ProviderName => "Google Gemini";

        public GeminiAiProvider(HttpClient httpClient, IFileService fileService)
        {
            _httpClient = httpClient;
            _fileService = fileService;
        }

        public async Task<AIResponse> GenerateAsync(AIRequest request, string apiKey, string? baseUrl = null, CancellationToken cancellationToken = default)
        {
            string baseEndpoint = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.TrimEnd('/');
            bool isCustomApi = !baseEndpoint.Contains("googleapis.com", StringComparison.OrdinalIgnoreCase);

            if (!isCustomApi && string.IsNullOrWhiteSpace(apiKey))
            {
                return new AIResponse { Success = false, ErrorMessage = "Chưa cung cấp Gemini API Key." };
            }

            string model = string.IsNullOrWhiteSpace(request.Model) ? (isCustomApi ? "gemini-3.1-flash-lite" : "gemini-3.5-flash") : request.Model;
            var candidateModels = new List<string> { model };

            string[] defaultFallbacks = isCustomApi
                ? new[] { "gemini-3.1-flash-lite", "fbb127bbb056c959", "9d8ca3786ebdfbea", "5bf011840784117a", "e051ce1aa80aa576" }
                : new[] { "gemini-3.1-flash-lite", "gemini-3.5-flash", "gemini-flash-lite-latest", "gemini-flash-latest" };

            foreach (var fallback in defaultFallbacks)
            {
                if (!candidateModels.Contains(fallback)) candidateModels.Add(fallback);
            }
            if (candidateModels.Count > 3)
            {
                candidateModels = candidateModels.Take(3).ToList();
            }

            var sw = Stopwatch.StartNew();

            for (int attempt = 0; attempt < candidateModels.Count; attempt++)
            {
                if (cancellationToken.IsCancellationRequested) break;
                string currentModel = candidateModels[attempt];

                string url;
                string jsonBody;

                if (isCustomApi)
                {
                    url = $"{baseEndpoint}/generate";
                    string fullPrompt = string.IsNullOrWhiteSpace(request.SystemInstruction)
                        ? request.UserPrompt
                        : $"{request.SystemInstruction}\n\n{request.UserPrompt}";

                    var customPayload = new
                    {
                        prompt = fullPrompt,
                        model = currentModel
                    };
                    jsonBody = JsonSerializer.Serialize(customPayload);
                }
                else
                {
                    url = $"{baseEndpoint}/v1beta/models/{currentModel}:generateContent?key={apiKey}";
                    var partsList = new List<object> { new { text = request.UserPrompt } };
                    var contentObj = new
                    {
                        contents = new[]
                        {
                            new
                            {
                                role = "user",
                                parts = partsList.ToArray()
                            }
                        },
                        systemInstruction = string.IsNullOrWhiteSpace(request.SystemInstruction) ? null : new
                        {
                            parts = new[] { new { text = request.SystemInstruction } }
                        },
                        generationConfig = new
                        {
                            temperature = Math.Clamp(request.Temperature, 0.0, 2.0),
                            topP = Math.Clamp(request.TopP, 0.0, 1.0),
                            maxOutputTokens = Math.Clamp(request.MaxTokens, 256, 8192),
                            responseMimeType = (request.ResponseFormat == "json") ? "application/json" : "text/plain"
                        }
                    };
                    jsonBody = JsonSerializer.Serialize(contentObj, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
                }

                try
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(40));

                    using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
                    httpRequest.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                    using var httpResponse = await _httpClient.SendAsync(httpRequest, timeoutCts.Token);
                    string responseBody = await httpResponse.Content.ReadAsStringAsync(timeoutCts.Token);

                    if (httpResponse.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(responseBody);
                        var root = doc.RootElement;
                        string? text = null;

                        if (root.TryGetProperty("text", out var textProp))
                        {
                            text = textProp.GetString();
                        }
                        else if (root.TryGetProperty("candidates", out var cands) && cands.GetArrayLength() > 0)
                        {
                            var first = cands[0];
                            if (first.TryGetProperty("content", out var content) &&
                                content.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0)
                            {
                                text = parts[0].GetProperty("text").GetString();
                            }
                        }
                        else if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                        {
                            var choice = choices[0];
                            if (choice.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var cnt))
                            {
                                text = cnt.GetString();
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            sw.Stop();
                            int inTokens = 0, outTokens = 0;
                            if (root.TryGetProperty("usageMetadata", out var usage))
                            {
                                if (usage.TryGetProperty("promptTokenCount", out var pt)) inTokens = pt.GetInt32();
                                if (usage.TryGetProperty("candidatesTokenCount", out var ct)) outTokens = ct.GetInt32();
                            }

                            return new AIResponse
                            {
                                Success = true,
                                Text = text,
                                RawResponse = responseBody,
                                InputTokens = inTokens,
                                OutputTokens = outTokens,
                                DurationMs = sw.ElapsedMilliseconds
                            };
                        }
                    }
                    else
                    {
                        int code = (int)httpResponse.StatusCode;
                        _fileService.Log($"AI model '{currentModel}' trả về lỗi HTTP {code}", "WARN");

                        // If overloaded (503) or rate-limited (429), try next model fallback
                        if (code == 503 || code == 429 || code == 404 || code == 500)
                        {
                            if (attempt < candidateModels.Count - 1)
                            {
                                await Task.Delay(800, cancellationToken);
                                continue;
                            }
                        }

                        return new AIResponse
                        {
                            Success = false,
                            ErrorMessage = $"Lỗi Gemini HTTP {code}: {responseBody}",
                            RawResponse = responseBody
                        };
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    _fileService.Log($"Gemini model '{currentModel}' quá thời gian chờ (timeout 15s).", "WARN");
                    if (attempt < candidateModels.Count - 1)
                    {
                        await Task.Delay(500, cancellationToken);
                        continue;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _fileService.Log($"Ngoại lệ khi gọi Gemini '{currentModel}': {ex.Message}", "WARN");
                    if (attempt == candidateModels.Count - 1)
                    {
                        return new AIResponse { Success = false, ErrorMessage = $"Lỗi kết nối Gemini: {ex.Message}" };
                    }
                }
            }

            return new AIResponse { Success = false, ErrorMessage = "Tất cả model Gemini đều không khả dụng hoặc bị quá tải." };
        }

        public async Task<bool> TestConnectionAsync(string apiKey, string model, string? baseUrl = null, CancellationToken cancellationToken = default)
        {
            var req = new AIRequest
            {
                Model = model,
                UserPrompt = "Xin chào, phản hồi 'OK' nếu bạn đọc được tin nhắn này.",
                MaxTokens = 16
            };
            var res = await GenerateAsync(req, apiKey, baseUrl, cancellationToken);
            return res.Success && !string.IsNullOrWhiteSpace(res.Text);
        }
    }

    public class OpenAiCompatibleProvider : IAIProvider
    {
        private readonly HttpClient _httpClient;
        private readonly IFileService _fileService;
        private const string DefaultBaseUrl = "https://api.openai.com/v1";

        public string ProviderName => "OpenAI / Compatible";

        public OpenAiCompatibleProvider(HttpClient httpClient, IFileService fileService)
        {
            _httpClient = httpClient;
            _fileService = fileService;
        }

        public async Task<AIResponse> GenerateAsync(AIRequest request, string apiKey, string? baseUrl = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new AIResponse { Success = false, ErrorMessage = "Chưa cung cấp OpenAI API Key." };
            }

            string baseEndpoint = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.TrimEnd('/');
            string url = $"{baseEndpoint}/chat/completions";
            string model = string.IsNullOrWhiteSpace(request.Model) ? "gpt-4o-mini" : request.Model;

            var messages = new List<object>();
            if (!string.IsNullOrWhiteSpace(request.SystemInstruction))
            {
                messages.Add(new { role = "system", content = request.SystemInstruction });
            }
            messages.Add(new { role = "user", content = request.UserPrompt });

            var payload = new Dictionary<string, object>
            {
                ["model"] = model,
                ["messages"] = messages,
                ["temperature"] = Math.Clamp(request.Temperature, 0.0, 2.0),
                ["top_p"] = Math.Clamp(request.TopP, 0.0, 1.0),
                ["max_tokens"] = Math.Clamp(request.MaxTokens, 256, 4096)
            };

            if (request.ResponseFormat == "json")
            {
                payload["response_format"] = new { type = "json_object" };
            }

            string jsonBody = JsonSerializer.Serialize(payload);
            var sw = Stopwatch.StartNew();

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
                    httpRequest.Headers.Add("Authorization", $"Bearer {apiKey}");
                    httpRequest.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                    using var httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken);
                    string responseBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

                    if (httpResponse.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(responseBody);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                        {
                            var first = choices[0];
                            if (first.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var contentProp))
                            {
                                string text = contentProp.GetString() ?? "";
                                sw.Stop();

                                int inTokens = 0, outTokens = 0;
                                if (root.TryGetProperty("usage", out var usage))
                                {
                                    if (usage.TryGetProperty("prompt_tokens", out var pt)) inTokens = pt.GetInt32();
                                    if (usage.TryGetProperty("completion_tokens", out var ct)) outTokens = ct.GetInt32();
                                }

                                return new AIResponse
                                {
                                    Success = true,
                                    Text = text,
                                    RawResponse = responseBody,
                                    InputTokens = inTokens,
                                    OutputTokens = outTokens,
                                    DurationMs = sw.ElapsedMilliseconds
                                };
                            }
                        }
                    }
                    else
                    {
                        int code = (int)httpResponse.StatusCode;
                        _fileService.Log($"OpenAI API HTTP {code}: {responseBody}", "WARN");
                        if (code == 429 || code == 503)
                        {
                            if (attempt < 2)
                            {
                                await Task.Delay(1200, cancellationToken);
                                continue;
                            }
                        }

                        return new AIResponse
                        {
                            Success = false,
                            ErrorMessage = $"OpenAI lỗi HTTP {code}: {responseBody}",
                            RawResponse = responseBody
                        };
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _fileService.Log($"Ngoại lệ khi gọi OpenAI API: {ex.Message}", "WARN");
                    if (attempt == 2)
                    {
                        return new AIResponse { Success = false, ErrorMessage = $"Lỗi kết nối OpenAI: {ex.Message}" };
                    }
                }
            }

            return new AIResponse { Success = false, ErrorMessage = "Không thể kết nối đến OpenAI API sau nhiều lần thử." };
        }

        public async Task<bool> TestConnectionAsync(string apiKey, string model, string? baseUrl = null, CancellationToken cancellationToken = default)
        {
            var req = new AIRequest
            {
                Model = model,
                UserPrompt = "Hi, reply 'OK' if you see this.",
                MaxTokens = 16
            };
            var res = await GenerateAsync(req, apiKey, baseUrl, cancellationToken);
            return res.Success && !string.IsNullOrWhiteSpace(res.Text);
        }
    }
}

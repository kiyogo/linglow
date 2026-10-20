using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VideoNarrator.Models;

namespace VideoNarrator.Services.PersonalityEngine
{
    public class ScriptGenerationResult
    {
        public bool Success { get; set; }
        public string OutputText { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public CompiledPrompt CompiledPrompt { get; set; } = new();
        public StyleValidationResult ValidationResult { get; set; } = new();
        public int TotalTokens { get; set; }
        public long DurationMs { get; set; }
        public StructuredScriptOutput? StructuredScript { get; set; }
        public VideoGenerationPromptOutput? VideoPrompt { get; set; }
        public EditingPlan? EditingPlan { get; set; }
    }

    public interface IScriptGenerator
    {
        Task<ScriptGenerationResult> GenerateAsync(
            PersonaProfile persona,
            string outputMode,
            double durationSeconds,
            string? topic,
            string? customInstructions,
            string apiKey,
            string model,
            string providerType = "Gemini",
            string? baseUrl = null,
            List<VideoScene>? detectedScenes = null,
            CancellationToken cancellationToken = default);
    }

    public class ScriptGenerator : IScriptGenerator
    {
        private readonly ISystemPromptBuilder _promptBuilder;
        private readonly IStyleValidator _validator;
        private readonly IOutputFormatter _formatter;
        private readonly GeminiAiProvider _geminiProvider;
        private readonly OpenAiCompatibleProvider _openAiProvider;
        private readonly IFileService _fileService;

        public ScriptGenerator(
            ISystemPromptBuilder promptBuilder,
            IStyleValidator validator,
            IOutputFormatter formatter,
            GeminiAiProvider geminiProvider,
            OpenAiCompatibleProvider openAiProvider,
            IFileService fileService)
        {
            _promptBuilder = promptBuilder;
            _validator = validator;
            _formatter = formatter;
            _geminiProvider = geminiProvider;
            _openAiProvider = openAiProvider;
            _fileService = fileService;
        }

        public async Task<ScriptGenerationResult> GenerateAsync(
            PersonaProfile persona,
            string outputMode,
            double durationSeconds,
            string? topic,
            string? customInstructions,
            string apiKey,
            string model,
            string providerType = "Gemini",
            string? baseUrl = null,
            List<VideoScene>? detectedScenes = null,
            CancellationToken cancellationToken = default)
        {
            var result = new ScriptGenerationResult();

            // 1. Biên dịch System Prompt & User Prompt
            var compiled = _promptBuilder.BuildSystemPrompt(
                persona, outputMode, durationSeconds, topic, customInstructions, detectedScenes);
            result.CompiledPrompt = compiled;

            bool isJsonMode = (outputMode != "script_only");

            var request = new AIRequest
            {
                SystemInstruction = compiled.SystemInstruction,
                UserPrompt = compiled.UserPrompt,
                Model = model,
                ResponseFormat = isJsonMode ? "json" : "text",
                Temperature = 0.72,
                TopP = 0.95,
                MaxTokens = (outputMode == "full_video_package") ? 4096 : 2048
            };

            IAIProvider provider = providerType.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)
                ? _openAiProvider
                : _geminiProvider;

            _fileService.Log($"ScriptGenerator: Bắt đầu sinh script theo Persona '{persona.Name}', Mode '{outputMode}', Provider '{provider.ProviderName}'");

            // 2. Gọi AI Provider lần 1
            var aiResponse = await provider.GenerateAsync(request, apiKey, baseUrl, cancellationToken);
            if (!aiResponse.Success)
            {
                result.Success = false;
                result.ErrorMessage = aiResponse.ErrorMessage;
                return result;
            }

            result.TotalTokens = aiResponse.InputTokens + aiResponse.OutputTokens;
            result.DurationMs = aiResponse.DurationMs;

            // 3. Kiểm định chất lượng (Style Validation)
            var validation = _validator.Validate(aiResponse.Text, outputMode, persona, durationSeconds);
            result.ValidationResult = validation;

            // 4. Cơ chế sửa lỗi / Auto-Repair nếu có lỗi cấu trúc hoặc chất lượng dưới ngưỡng (Tối đa 1 lần retry)
            string finalText = validation.SanitizedOutput;
            if (!validation.IsValid && validation.QualityScore < 50)
            {
                _fileService.Log($"ScriptGenerator: Kết quả lần 1 chưa đạt chuẩn ({validation.QualityScore}/100). Đang thực hiện tinh chỉnh 1 lần...", "WARN");
                
                string repairPrompt = $"{compiled.UserPrompt}\n\nLƯU Ý ĐẶC BIỆT: Lần sinh trước gặp vấn đề: {string.Join("; ", validation.Issues)}. Hãy sửa các lỗi trên và tuân thủ nghiêm ngặt định dạng {outputMode}.";
                var retryReq = new AIRequest
                {
                    SystemInstruction = compiled.SystemInstruction,
                    UserPrompt = repairPrompt,
                    Model = model,
                    ResponseFormat = isJsonMode ? "json" : "text",
                    Temperature = 0.65,
                    MaxTokens = request.MaxTokens
                };

                var retryRes = await provider.GenerateAsync(retryReq, apiKey, baseUrl, cancellationToken);
                if (retryRes.Success && !string.IsNullOrWhiteSpace(retryRes.Text))
                {
                    var retryVal = _validator.Validate(retryRes.Text, outputMode, persona, durationSeconds);
                    if (retryVal.QualityScore > validation.QualityScore)
                    {
                        finalText = retryVal.SanitizedOutput;
                        result.ValidationResult = retryVal;
                        result.TotalTokens += (retryRes.InputTokens + retryRes.OutputTokens);
                    }
                }
            }

            // 5. Định dạng đầu ra theo outputMode
            result.OutputText = _formatter.FormatOutput(finalText, outputMode);
            if (outputMode == "structured_script")
            {
                result.StructuredScript = _formatter.ParseStructuredScript(finalText);
            }
            else if (outputMode == "video_generation_prompt")
            {
                result.VideoPrompt = _formatter.ParseVideoPrompt(finalText);
            }
            else if (outputMode == "full_video_package")
            {
                result.EditingPlan = _formatter.ParseEditingPlan(finalText);
            }

            result.Success = true;
            _fileService.Log($"ScriptGenerator: Hoàn tất sinh kịch bản thành công ({result.ValidationResult.QualityScore}/100, {result.TotalTokens} tokens)");
            return result;
        }
    }
}

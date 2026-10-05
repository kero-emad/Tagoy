using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace church.AIServices.AI;

public sealed class GeminiAIProvider : AIProviderBase, IAIProvider
{
    public GeminiAIProvider(IHttpClientFactory factory, AIProviderOptions options, ILogger<GeminiAIProvider> logger) : base(factory, options, logger) { }
    public string Name => "gemini";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Options.GeminiApiKey);

    public Task<AIProviderResult> ExecuteAsync(AIProviderRequest input, CancellationToken cancellationToken)
    {
        if (!IsConfigured) return Task.FromResult(Failure(Name, AIProviderFailureCategory.NotConfigured, null, 0, "GEMINI_API_KEY is not configured"));
        var declarations = input.Tools.Select(t => new { name = t.GetProperty("function").GetProperty("name").GetString(), description = t.GetProperty("function").GetProperty("description").GetString(), parameters = t.GetProperty("function").GetProperty("parameters") }).ToArray();
        var payload = new {
            system_instruction = new { parts = new[] { new { text = input.SystemPrompt } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = input.UserMessage } } } },
            tools = new[] { new { function_declarations = declarations } },
            tool_config = new { function_calling_config = new { mode = input.RequireTool ? "ANY" : "AUTO" } },
            generation_config = new { temperature = input.Temperature }
        };
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(Options.GeminiModel)}:generateContent?key={Uri.EscapeDataString(Options.GeminiApiKey!)}";
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };
        return SendAsync(Name, request, input, Parse, cancellationToken);
    }

    private static AIProviderResult Parse(JsonElement root)
    {
        var parts = root.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts");
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("functionCall", out var call))
                return new() { Success = true, ToolCall = new AIProviderToolCall { Name = call.GetProperty("name").GetString() ?? "", ArgumentsJson = call.GetProperty("args").GetRawText() } };
            if (part.TryGetProperty("text", out var text) && !string.IsNullOrWhiteSpace(text.GetString())) return new() { Success = true, Content = text.GetString() };
        }
        return new() { FailureCategory = AIProviderFailureCategory.InvalidResponse };
    }
}

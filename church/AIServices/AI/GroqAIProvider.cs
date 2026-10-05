using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace church.AIServices.AI;

public sealed class GroqAIProvider : AIProviderBase, IAIProvider
{
    public GroqAIProvider(IHttpClientFactory factory, AIProviderOptions options, ILogger<GroqAIProvider> logger) : base(factory, options, logger) { }
    public string Name => "groq";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Options.GroqApiKey);

    public Task<AIProviderResult> ExecuteAsync(AIProviderRequest input, CancellationToken cancellationToken)
    {
        if (!IsConfigured) return Task.FromResult(Failure(Name, AIProviderFailureCategory.NotConfigured, null, 0, "GROQ_API_KEY is not configured"));
        var messages = new object[] {
            new { role = "system", content = input.SystemPrompt },
            new { role = "user", content = input.UserMessage }
        };
        var payload = new { model = Options.GroqModel, include_reasoning = false, temperature = input.Temperature, messages, tools = input.Tools, tool_choice = input.RequireTool ? "required" : "auto" };
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Options.GroqApiKey);
        return SendAsync(Name, request, input, Parse, cancellationToken);
    }

    private static AIProviderResult Parse(JsonElement root)
    {
        var message = root.GetProperty("choices")[0].GetProperty("message");
        var content = message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
        if (!message.TryGetProperty("tool_calls", out var calls) || calls.GetArrayLength() == 0)
            return string.IsNullOrWhiteSpace(content) ? new() { FailureCategory = AIProviderFailureCategory.InvalidResponse } : new() { Success = true, Content = content };
        var function = calls[0].GetProperty("function");
        var name = function.GetProperty("name").GetString();
        var args = function.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(args)) return new() { FailureCategory = AIProviderFailureCategory.InvalidToolCall };
        using var _ = JsonDocument.Parse(args);
        return new() { Success = true, ToolCall = new AIProviderToolCall { Name = name, ArgumentsJson = args } };
    }
}

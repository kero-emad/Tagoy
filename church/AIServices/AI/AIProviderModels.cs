using System.Text.Json;

namespace church.AIServices.AI;

public enum AIProviderFailureCategory
{
    None,
    NotConfigured,
    RateLimited,
    Timeout,
    TransientNetwork,
    ProviderUnavailable,
    InvalidResponse,
    InvalidToolCall,
    Cancelled,
    Unknown
}

public sealed class AIProviderRequest
{
    public string SystemPrompt { get; init; } = "";
    public string UserMessage { get; init; } = "";
    public IReadOnlyList<JsonElement> Tools { get; init; } = Array.Empty<JsonElement>();
    public bool RequireTool { get; init; }
    public double Temperature { get; init; } = 0;
}

public sealed class AIProviderToolCall
{
    public string Name { get; init; } = "";
    public string ArgumentsJson { get; init; } = "{}";
}

public sealed class AIProviderResult
{
    public bool Success { get; init; }
    public string Provider { get; init; } = "";
    public string? Content { get; init; }
    public AIProviderToolCall? ToolCall { get; init; }
    public AIProviderFailureCategory FailureCategory { get; init; }
    public int? StatusCode { get; init; }
    public long? LatencyMs { get; init; }
    public string? ErrorMessage { get; init; }
    public bool IsRateLimited => FailureCategory == AIProviderFailureCategory.RateLimited;
}

public sealed class AIProviderOptions
{
    public string PrimaryProvider { get; init; } = "groq";
    public string FallbackProvider { get; init; } = "gemini";
    public bool VerificationEnabled { get; init; }
    public TimeSpan ProviderTimeout { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromSeconds(30);
    public string? GroqApiKey { get; init; }
    public string GroqModel { get; init; } = "openai/gpt-oss-120b";
    public string? GeminiApiKey { get; init; }
    public string GeminiModel { get; init; } = "gemini-2.0-flash";
}

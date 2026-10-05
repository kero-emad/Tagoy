using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace church.AIServices.AI;

public sealed class AIOrchestrator : IAIOrchestrator
{
    private readonly IReadOnlyDictionary<string, IAIProvider> _providers;
    private readonly AIProviderOptions _options;
    private readonly ILogger<AIOrchestrator> _logger;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _cooldownUntil = new(StringComparer.OrdinalIgnoreCase);

    public AIOrchestrator(IEnumerable<IAIProvider> providers, AIProviderOptions options, ILogger<AIOrchestrator> logger)
    { _providers = providers.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase); _options = options; _logger = logger; }

    public async Task<AIProviderResult> ExecuteAsync(AIProviderRequest request, CancellationToken cancellationToken)
    {
        var primary = Select(_options.PrimaryProvider);
        var fallback = Select(_options.FallbackProvider);
        var first = await TryProvider(primary, request, cancellationToken);
        if (first.Success || !ShouldFallback(first) || fallback == null || ReferenceEquals(primary, fallback) || cancellationToken.IsCancellationRequested) return first;
        var second = await TryProvider(fallback, request, cancellationToken);
        if (second.Success) return second;
        return new AIProviderResult
        {
            Success = second.Success,
            Provider = second.Provider,
            Content = second.Content,
            ToolCall = second.ToolCall,
            FailureCategory = second.FailureCategory,
            StatusCode = second.StatusCode,
            LatencyMs = second.LatencyMs,
            ErrorMessage = $"Primary provider failed ({first.FailureCategory}); fallback provider failed ({second.FailureCategory})."
        };
    }

    private IAIProvider? Select(string? name) => string.IsNullOrWhiteSpace(name) || name.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : _providers.GetValueOrDefault(name);
    private async Task<AIProviderResult> TryProvider(IAIProvider? provider, AIProviderRequest request, CancellationToken token)
    {
        if (provider == null) return new() { FailureCategory = AIProviderFailureCategory.NotConfigured, ErrorMessage = "AI provider is not configured" };
        if (!provider.IsConfigured) return new() { Provider = provider.Name, FailureCategory = AIProviderFailureCategory.NotConfigured, ErrorMessage = "AI provider key is not configured" };
        if (_cooldownUntil.TryGetValue(provider.Name, out var until) && until > DateTimeOffset.UtcNow)
            return new() { Provider = provider.Name, FailureCategory = AIProviderFailureCategory.RateLimited, ErrorMessage = "AI provider is cooling down" };
        var result = await provider.ExecuteAsync(request, token);
        if (result.Success && request.RequireTool && result.ToolCall == null)
            result = new AIProviderResult
            {
                Provider = provider.Name,
                FailureCategory = AIProviderFailureCategory.InvalidResponse,
                StatusCode = result.StatusCode,
                LatencyMs = result.LatencyMs,
                ErrorMessage = "A tool call was required but the provider returned no tool."
            };
        if (result.Success && result.ToolCall != null && (!request.Tools.Any(t => t.GetProperty("function").GetProperty("name").GetString() == result.ToolCall.Name) || !IsObject(result.ToolCall.ArgumentsJson)))
            result = new() { Provider = result.Provider, FailureCategory = AIProviderFailureCategory.InvalidToolCall, StatusCode = result.StatusCode, LatencyMs = result.LatencyMs, ErrorMessage = "Unknown or malformed tool call" };
        if (result.FailureCategory is AIProviderFailureCategory.RateLimited or AIProviderFailureCategory.ProviderUnavailable or AIProviderFailureCategory.Timeout or AIProviderFailureCategory.TransientNetwork)
            _cooldownUntil[provider.Name] = DateTimeOffset.UtcNow.Add(_options.Cooldown);
        _logger.LogInformation("AI provider {Provider} result: success={Success}, failureCategory={FailureCategory}, statusCode={StatusCode}, latencyMs={LatencyMs}, toolName={ToolName}", provider.Name, result.Success, result.FailureCategory, result.StatusCode, result.LatencyMs, result.ToolCall?.Name);
        return result;
    }
    private static bool IsObject(string json) { try { using var d = JsonDocument.Parse(json); return d.RootElement.ValueKind == JsonValueKind.Object; } catch { return false; } }
    private static bool ShouldFallback(AIProviderResult r) => r.FailureCategory is AIProviderFailureCategory.NotConfigured or AIProviderFailureCategory.RateLimited or AIProviderFailureCategory.Timeout or AIProviderFailureCategory.TransientNetwork or AIProviderFailureCategory.ProviderUnavailable or AIProviderFailureCategory.InvalidResponse or AIProviderFailureCategory.InvalidToolCall;
}

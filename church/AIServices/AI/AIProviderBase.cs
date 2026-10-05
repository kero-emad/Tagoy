using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace church.AIServices.AI;

public abstract class AIProviderBase
{
    protected readonly IHttpClientFactory HttpClientFactory;
    protected readonly AIProviderOptions Options;
    protected readonly ILogger Logger;

    protected AIProviderBase(IHttpClientFactory httpClientFactory, AIProviderOptions options, ILogger logger)
    {
        HttpClientFactory = httpClientFactory;
        Options = options;
        Logger = logger;
    }

    protected async Task<AIProviderResult> SendAsync(
        string provider,
        HttpRequestMessage request,
        AIProviderRequest input,
        Func<JsonElement, AIProviderResult> parse,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Options.ProviderTimeout);
            using var response = await HttpClientFactory.CreateClient().SendAsync(request, timeout.Token);
            var raw = await response.Content.ReadAsStringAsync(timeout.Token);
            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var category = response.StatusCode == HttpStatusCode.TooManyRequests
                    ? AIProviderFailureCategory.RateLimited
                    : (int)response.StatusCode >= 500
                        ? AIProviderFailureCategory.ProviderUnavailable
                        : AIProviderFailureCategory.InvalidResponse;
                return Failure(provider, category, (int)response.StatusCode, stopwatch.ElapsedMilliseconds, raw);
            }

            using var document = JsonDocument.Parse(raw);
            return parse(document.RootElement);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(provider, AIProviderFailureCategory.Timeout, null, stopwatch.ElapsedMilliseconds, "provider timeout");
        }
        catch (OperationCanceledException)
        {
            return Failure(provider, AIProviderFailureCategory.Cancelled, null, stopwatch.ElapsedMilliseconds, "request cancelled");
        }
        catch (HttpRequestException ex)
        {
            return Failure(provider, AIProviderFailureCategory.TransientNetwork, null, stopwatch.ElapsedMilliseconds, ex.Message);
        }
        catch (JsonException ex)
        {
            return Failure(provider, AIProviderFailureCategory.InvalidResponse, null, stopwatch.ElapsedMilliseconds, ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "AI provider {Provider} failed unexpectedly", provider);
            return Failure(provider, AIProviderFailureCategory.Unknown, null, stopwatch.ElapsedMilliseconds, ex.Message);
        }
    }

    protected static AIProviderResult Failure(string provider, AIProviderFailureCategory category, int? status, long latency, string? error) => new()
    {
        Provider = provider, FailureCategory = category, StatusCode = status, LatencyMs = latency, ErrorMessage = error
    };
}

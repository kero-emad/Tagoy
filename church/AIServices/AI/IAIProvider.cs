namespace church.AIServices.AI;

public interface IAIProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<AIProviderResult> ExecuteAsync(AIProviderRequest request, CancellationToken cancellationToken);
}

public interface IAIOrchestrator
{
    Task<AIProviderResult> ExecuteAsync(AIProviderRequest request, CancellationToken cancellationToken);
}

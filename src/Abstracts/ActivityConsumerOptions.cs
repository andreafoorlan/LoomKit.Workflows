namespace LoomKit.Workflows.Abstracts;

public class ActivityConsumerOptions
{
    public required string ActivityConsumerName { get; init; }
    public required Type ActivityConsumerType { get; init; }

    public required string ActivityQueueName { get; init; }

    public bool UseScopedServiceProvider { get; init; }

    // Open generic middleware types (e.g. typeof(ActivityRetryMiddleware<,>))
    public required IReadOnlyList<Type> ActivityMiddlewares { get; init; }
}

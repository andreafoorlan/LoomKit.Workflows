namespace LoomKit.Workflows.Abstracts;

public class ActivityQueueOptions
{
    public required string ActivityQueueName { get; init; }

    public required Type ActivityQueueType { get; init; }

    public int MaxActivityRetries { get; init; } = 5;

    public int ActivityAwaitCheckInterval { get; init; } = 1000;

    public int ActivityRetryInterval { get; init; } = 5000;
}

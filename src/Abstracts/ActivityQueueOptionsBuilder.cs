namespace LoomKit.Workflows.Abstracts;

public class ActivityQueueOptionsBuilder
{
    public required string ActivityQueueName { get; init; }
    public required Type ActivityQueueType { get; init; }
    public int MaxActivityRetries { get; set; } = 5;
    public int ActivityAwaitCheckInterval { get; set; } = 1000;
    public int ActivityRetryInterval { get; set; } = 5000;

    public ActivityQueueOptions Build()
    {
        return new ActivityQueueOptions()
        {
            ActivityQueueName = ActivityQueueName,
            ActivityQueueType = ActivityQueueType,
            MaxActivityRetries = MaxActivityRetries,
            ActivityAwaitCheckInterval = ActivityAwaitCheckInterval,
            ActivityRetryInterval = ActivityRetryInterval
        };
    }
}

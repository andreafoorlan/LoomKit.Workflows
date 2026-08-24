using LoomKit.Workflows.Contracts;

namespace LoomKit.Workflows.Abstracts;

public abstract class WorkflowManagerOptionsBuilder<TWorkflowManagerOptions>
    where TWorkflowManagerOptions : WorkflowManagerOptions, new()
{
    private readonly Dictionary<string, ActivityQueueOptions> _activityQueueOptions;
    private readonly Dictionary<string, ActivityConsumerOptions> _activityConsumerOptions;
    private readonly LinkedList<Type> _workflowManagerSeeders;

    public WorkflowManagerOptionsBuilder()
    {
        _activityQueueOptions = new();
        _activityConsumerOptions = new();
        _workflowManagerSeeders = [];
    }

    public WorkflowManagerOptionsBuilder<TWorkflowManagerOptions> UseQueue<TActivityQueue>(string queueName, Action<ActivityQueueOptionsBuilder> activityQueueOptionsBuilderAction)
        where TActivityQueue : IActivityQueue
    {
        if (string.IsNullOrWhiteSpace(queueName))
            throw new ArgumentException("Queue name must not be null or empty", nameof(queueName));

        if (_activityQueueOptions.ContainsKey(queueName))
            throw new InvalidOperationException($"Queue name {queueName} already taken");

        var activityQueueOptionsBuilder = new ActivityQueueOptionsBuilder()
        {
            ActivityQueueName = queueName,
            ActivityQueueType = typeof(TActivityQueue)
        };

        activityQueueOptionsBuilderAction.Invoke(activityQueueOptionsBuilder);

        _activityQueueOptions.Add(queueName, activityQueueOptionsBuilder.Build());

        return this;
    }

    public WorkflowManagerOptionsBuilder<TWorkflowManagerOptions> UseConsumer<TActivityConsumer>(string consumerName, string queueName, Action<ActivityConsumerOptionsBuilder> activityConsumerOptionsBuilderAction)
        where TActivityConsumer : IActivityConsumer
    {
        if (string.IsNullOrWhiteSpace(consumerName))
            throw new ArgumentException("Consumer name must not be null or empty", nameof(consumerName));

        if (string.IsNullOrWhiteSpace(queueName))
            throw new ArgumentException("Queue name must not be null or empty", nameof(queueName));

        if (_activityConsumerOptions.ContainsKey(consumerName))
            throw new InvalidOperationException($"Consumer name {consumerName} already taken");

        var activityConsumerOptionsBuilder = new ActivityConsumerOptionsBuilder()
        {
            ActivityConsumerName = consumerName,
            ActivityConsumerType = typeof(TActivityConsumer),
            ActivityQueueName = queueName
        };

        activityConsumerOptionsBuilderAction.Invoke(activityConsumerOptionsBuilder);

        _activityConsumerOptions.Add(consumerName, activityConsumerOptionsBuilder.Build());

        return this;
    }

    public WorkflowManagerOptionsBuilder<TWorkflowManagerOptions> UseWorkflowManagerSeeder<TWorkflowManagerSeeder>()
        where TWorkflowManagerSeeder : IWorkflowManagerSeeder
    {
        _workflowManagerSeeders.AddLast(typeof(TWorkflowManagerSeeder));

        return this;
    }

    public virtual TWorkflowManagerOptions Build()
    {
        return new TWorkflowManagerOptions()
        {
            ActivityQueueOptions = new Dictionary<string, ActivityQueueOptions>(_activityQueueOptions),
            ActivityConsumerOptions = new Dictionary<string, ActivityConsumerOptions>(_activityConsumerOptions),
            WorkflowManagerSeeders = _workflowManagerSeeders.ToList()
        };
    }
}

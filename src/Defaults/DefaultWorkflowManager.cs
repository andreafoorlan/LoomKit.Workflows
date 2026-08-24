using LoomKit.Workflows.Abstracts;
using LoomKit.Workflows.Contracts;
using LoomKit.Workflows.Events;
using LoomKit.Workflows.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LoomKit.Workflows.Defaults;

public class DefaultWorkflowManager : WorkflowManager<DefaultWorkflowManagerOptions>
{
    public override event EventHandler<ActivityScheduledEventArgs>? ActivityScheduled;
    public override event EventHandler<ActivityStartedEventArgs>? ActivityStarted;
    public override event EventHandler<ActivityEndedEventArgs>? ActivityEnded;
    public override event EventHandler<ActivityExceptionEventArgs>? ActivityException;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DefaultWorkflowManager> _logger;
    private readonly Dictionary<string, IActivityQueue> _activityQueues;
    private readonly Dictionary<string, IActivityConsumer> _activityConsumers;

    public DefaultWorkflowManager(DefaultWorkflowManagerOptions workflowManagerOptions, IServiceProvider serviceProvider, ILogger<DefaultWorkflowManager> logger)
        : base(workflowManagerOptions)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        _activityQueues = new Dictionary<string, IActivityQueue>();
        _activityConsumers = new Dictionary<string, IActivityConsumer>();
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var (queueName, queueOptions) in _workflowManagerOptions.ActivityQueueOptions)
        {
            EnsureOrThrow(queueOptions.ActivityQueueType is not null, $"Queue type for queue {queueName} is null.");
            EnsureOrThrow(!_activityQueues.ContainsKey(queueName), $"Queue with name {queueName} already exists.");

            // create queue instance, injecting the options and resolving the rest from DI -
            // ActivatorUtilities picks the constructor matching the supplied arguments and DI
            // registrations, instead of hand-rolled (and fragile) constructor selection
            var queue = (IActivityQueue)ActivatorUtilities.CreateInstance(_serviceProvider, queueOptions.ActivityQueueType!, queueOptions);

            EnsureOrThrow(_activityQueues.TryAdd(queueName, queue), $"Queue with name {queueName} not added to activity queues.");

            await queue.StartAsync(cancellationToken);
        }

        foreach (var (consumerName, consumerOptions) in _workflowManagerOptions.ActivityConsumerOptions)
        {
            EnsureOrThrow(consumerOptions.ActivityConsumerType is not null, $"Consumer type for consumer {consumerName} is null.");
            EnsureOrThrow(!_activityConsumers.ContainsKey(consumerName), $"Consumer with name {consumerName} already exists.");
            EnsureOrThrow(_activityQueues.TryGetValue(consumerOptions.ActivityQueueName, out var activityQueue), $"Activity queue {consumerOptions.ActivityQueueName} for consumer with name {consumerName} does not exist.");

            // create consumer instance, injecting the options, its queue, and resolving the rest from DI
            var consumer = (IActivityConsumer)ActivatorUtilities.CreateInstance(_serviceProvider, consumerOptions.ActivityConsumerType!, consumerOptions, activityQueue!);

            EnsureOrThrow(_activityConsumers.TryAdd(consumerName, consumer), $"Consumer with name {consumerName} not added to activity consumers.");

            await consumer.StartAsync(cancellationToken);
        }

        foreach (var seederType in _workflowManagerOptions.WorkflowManagerSeeders)
        {
            var seeder = (IWorkflowManagerSeeder)_serviceProvider.GetRequiredService(seederType);
            await seeder.SeedActivities(this);
        }

        _logger.LogInformation("[{MethodName}] WorkflowManager started", nameof(StartAsync));
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // stop consumers first so no new activity is picked up while queues are still draining
        foreach (var (_, consumer) in _activityConsumers)
            await consumer.StopAsync(cancellationToken);

        // then stop the queues themselves
        foreach (var (_, queue) in _activityQueues)
            await queue.StopAsync(cancellationToken);

        _logger.LogInformation("[{MethodName}] WorkflowManager stopped", nameof(StopAsync));
    }

    public override Task<Dictionary<string, IActivityQueue>> ListQueuesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_activityQueues.ToDictionary());

    public override Task<Dictionary<string, IActivityConsumer>> ListConsumersAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_activityConsumers.ToDictionary());

    public override async Task EnqueueActivityScheduleAsync(string queueName, ActivitySchedule schedule, CancellationToken cancellationToken = default)
    {
        if (!_activityQueues.TryGetValue(queueName, out var queue))
            throw new InvalidOperationException($"Queue '{queueName}' does not exist.");

        await queue.EnqueueActivityScheduleAsync(schedule, cancellationToken);
    }

    public override Task<List<ActivitySchedule>> ListConsumingActivitySchedulesAsync(CancellationToken cancellationToken = default)
    {
        var result = _activityConsumers.Values
            .Where(c => c.CurrentActivitySchedule is not null)
            .Select(c => c.CurrentActivitySchedule!)
            .ToList();

        return Task.FromResult(result);
    }

    public override async Task<Dictionary<string, List<ActivitySchedule>>> ListQueuedActivitySchedulesAsync(
        Func<IQueryable<ActivitySchedule>, IQueryable<ActivitySchedule>> queryBuilder,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, List<ActivitySchedule>>();

        foreach (var (name, queue) in _activityQueues)
            result[name] = await queue.ListActivitySchedulesAsync(queryBuilder, cancellationToken);

        return result;
    }

    public override async Task<List<ActivitySchedule>> ListQueuedActivitySchedulesAsync(
        string queueName,
        Func<IQueryable<ActivitySchedule>, IQueryable<ActivitySchedule>> queryBuilder,
        CancellationToken cancellationToken = default)
    {
        if (!_activityQueues.TryGetValue(queueName, out var queue))
            throw new InvalidOperationException($"Queue '{queueName}' does not exist.");

        return await queue.ListActivitySchedulesAsync(queryBuilder, cancellationToken);
    }

    public override async Task<Dictionary<string, long>> RemoveQueuedActivitySchedulesAsync(
        Predicate<ActivitySchedule> predicate,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, long>();

        foreach (var (name, queue) in _activityQueues)
            result[name] = await queue.RemoveActivitySchedulesAsync(predicate, cancellationToken);

        return result;
    }

    public override async Task<long> RemoveQueuedActivitySchedulesAsync(
        string queueName,
        Predicate<ActivitySchedule> predicate,
        CancellationToken cancellationToken = default)
    {
        if (!_activityQueues.TryGetValue(queueName, out var queue))
            throw new InvalidOperationException($"Queue '{queueName}' does not exist.");

        return await queue.RemoveActivitySchedulesAsync(predicate, cancellationToken);
    }

    public override Task<ActivityQueueOptions> GetActivityQueueOptionsAsync(string queueName, CancellationToken cancellationToken = default)
    {
        if (!_activityQueues.TryGetValue(queueName, out var queue))
            throw new InvalidOperationException($"Queue '{queueName}' does not exist.");

        return Task.FromResult(queue.ActivityQueueOptions);
    }

    public override Task<ActivityConsumerOptions> GetActivityConsumerOptionsAsync(string consumerName, CancellationToken cancellationToken = default)
    {
        if (!_activityConsumers.TryGetValue(consumerName, out var consumer))
            throw new InvalidOperationException($"Consumer '{consumerName}' does not exist.");

        return Task.FromResult(consumer.ActivityConsumerOptions);
    }

    public override async Task TriggerResumeTokenAsync(string tokenValue, CancellationToken cancellationToken = default)
    {
        foreach (var (_, queue) in _activityQueues)
        {
            if (await queue.TriggerResumeTokenAsync(tokenValue, cancellationToken))
                return;
        }

        _logger.LogWarning("[{MethodName}] No paused activity found for resume token '{TokenValue}'",
            nameof(TriggerResumeTokenAsync), tokenValue);
    }

    public override Task NotifyActivityScheduled(string queueName, ActivitySchedule schedule)
    {
        ActivityScheduled?.Invoke(this, new ActivityScheduledEventArgs { QueueName = queueName, ActivitySchedule = schedule });
        return Task.CompletedTask;
    }

    public override Task NotifyActivityStarted(string queueName, string consumerName, ActivitySchedule schedule)
    {
        ActivityStarted?.Invoke(this, new ActivityStartedEventArgs { QueueName = queueName, ConsumerName = consumerName, ActivitySchedule = schedule });
        return Task.CompletedTask;
    }

    public override Task NotifyActivityEnded(string queueName, string consumerName, ActivitySchedule schedule)
    {
        ActivityEnded?.Invoke(this, new ActivityEndedEventArgs { QueueName = queueName, ConsumerName = consumerName, ActivitySchedule = schedule });
        return Task.CompletedTask;
    }

    public override Task NotifyActivityException(string queueName, string consumerName, ActivitySchedule schedule, Exception exception)
    {
        ActivityException?.Invoke(this, new ActivityExceptionEventArgs { QueueName = queueName, ConsumerName = consumerName, ActivitySchedule = schedule, Exception = exception });
        return Task.CompletedTask;
    }

    private void EnsureOrThrow(bool condition, string message)
    {
        if (condition)
            return;

        _logger.LogError("{Message}", message);
        throw new InvalidOperationException(message);
    }
}

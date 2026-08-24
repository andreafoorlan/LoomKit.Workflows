using LoomKit.Workflows.Contracts;
using LoomKit.Workflows.Events;
using LoomKit.Workflows.Models;

namespace LoomKit.Workflows.Abstracts;

public abstract class WorkflowManager<TWorkflowManagerOptions> : IWorkflowManager
    where TWorkflowManagerOptions : WorkflowManagerOptions
{
    protected readonly TWorkflowManagerOptions _workflowManagerOptions;

    public WorkflowManager(TWorkflowManagerOptions workflowManagerOptions)
    {
        ArgumentNullException.ThrowIfNull(workflowManagerOptions);
        _workflowManagerOptions = workflowManagerOptions;
    }

    public abstract Task StartAsync(CancellationToken cancellationToken);
    public abstract Task StopAsync(CancellationToken cancellationToken);

    public abstract Task<Dictionary<string, IActivityQueue>> ListQueuesAsync(CancellationToken cancellationToken = default);
    public abstract Task<Dictionary<string, IActivityConsumer>> ListConsumersAsync(CancellationToken cancellationToken = default);

    public abstract Task EnqueueActivityScheduleAsync(string queueName, ActivitySchedule schedule, CancellationToken cancellationToken = default);
    public abstract Task<List<ActivitySchedule>> ListConsumingActivitySchedulesAsync(CancellationToken cancellationToken = default);
    public abstract Task<Dictionary<string, List<ActivitySchedule>>> ListQueuedActivitySchedulesAsync(Func<IQueryable<ActivitySchedule>, IQueryable<ActivitySchedule>> queryBuilder, CancellationToken cancellationToken = default);
    public abstract Task<List<ActivitySchedule>> ListQueuedActivitySchedulesAsync(string queueName, Func<IQueryable<ActivitySchedule>, IQueryable<ActivitySchedule>> queryBuilder, CancellationToken cancellationToken = default);
    public abstract Task<Dictionary<string, long>> RemoveQueuedActivitySchedulesAsync(Predicate<ActivitySchedule> predicate, CancellationToken cancellationToken = default);
    public abstract Task<long> RemoveQueuedActivitySchedulesAsync(string queueName, Predicate<ActivitySchedule> predicate, CancellationToken cancellationToken = default);

    public abstract Task<ActivityQueueOptions> GetActivityQueueOptionsAsync(string queueName, CancellationToken cancellationToken = default);
    public abstract Task<ActivityConsumerOptions> GetActivityConsumerOptionsAsync(string consumerName, CancellationToken cancellationToken = default);

    public abstract Task TriggerResumeTokenAsync(string tokenValue, CancellationToken cancellationToken = default);

    public abstract Task NotifyActivityScheduled(string queueName, ActivitySchedule schedule);
    public abstract Task NotifyActivityStarted(string queueName, string consumerName, ActivitySchedule schedule);
    public abstract Task NotifyActivityEnded(string queueName, string consumerName, ActivitySchedule schedule);
    public abstract Task NotifyActivityException(string queueName, string consumerName, ActivitySchedule schedule, Exception exception);

    public abstract event EventHandler<ActivityScheduledEventArgs>? ActivityScheduled;
    public abstract event EventHandler<ActivityStartedEventArgs>? ActivityStarted;
    public abstract event EventHandler<ActivityEndedEventArgs>? ActivityEnded;
    public abstract event EventHandler<ActivityExceptionEventArgs>? ActivityException;
}

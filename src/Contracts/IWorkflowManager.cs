using LoomKit.Workflows.Abstracts;
using LoomKit.Workflows.Events;
using LoomKit.Workflows.Models;
using Microsoft.Extensions.Hosting;

namespace LoomKit.Workflows.Contracts;

public interface IWorkflowManager : IHostedService
{
    event EventHandler<ActivityScheduledEventArgs>? ActivityScheduled;
    event EventHandler<ActivityStartedEventArgs>? ActivityStarted;
    event EventHandler<ActivityEndedEventArgs>? ActivityEnded;
    event EventHandler<ActivityExceptionEventArgs>? ActivityException;

    Task<Dictionary<string, IActivityQueue>> ListQueuesAsync(CancellationToken cancellationToken = default);
    Task<Dictionary<string, IActivityConsumer>> ListConsumersAsync(CancellationToken cancellationToken = default);

    Task EnqueueActivityScheduleAsync(string queueName, ActivitySchedule schedule, CancellationToken cancellationToken = default);
    Task<List<ActivitySchedule>> ListConsumingActivitySchedulesAsync(CancellationToken cancellationToken = default);
    Task<Dictionary<string, List<ActivitySchedule>>> ListQueuedActivitySchedulesAsync(Func<IQueryable<ActivitySchedule>, IQueryable<ActivitySchedule>> queryBuilder, CancellationToken cancellationToken = default);
    Task<List<ActivitySchedule>> ListQueuedActivitySchedulesAsync(string queueName, Func<IQueryable<ActivitySchedule>, IQueryable<ActivitySchedule>> queryBuilder, CancellationToken cancellationToken = default);
    Task<Dictionary<string, long>> RemoveQueuedActivitySchedulesAsync(Predicate<ActivitySchedule> predicate, CancellationToken cancellationToken = default);
    Task<long> RemoveQueuedActivitySchedulesAsync(string queueName, Predicate<ActivitySchedule> predicate, CancellationToken cancellationToken = default);

    Task<ActivityQueueOptions> GetActivityQueueOptionsAsync(string queueName, CancellationToken cancellationToken = default);
    Task<ActivityConsumerOptions> GetActivityConsumerOptionsAsync(string consumerName, CancellationToken cancellationToken = default);

    // Resumes the paused activity whose ResumeTokens list contains a token with Value == tokenValue.
    // Searches all queues; no-ops with a warning log if the token is not found.
    Task TriggerResumeTokenAsync(string tokenValue, CancellationToken cancellationToken = default);

    Task NotifyActivityScheduled(string queueName, ActivitySchedule schedule);
    Task NotifyActivityStarted(string queueName, string consumerName, ActivitySchedule schedule);
    Task NotifyActivityEnded(string queueName, string consumerName, ActivitySchedule schedule);
    Task NotifyActivityException(string queueName, string consumerName, ActivitySchedule schedule, Exception exception);
}

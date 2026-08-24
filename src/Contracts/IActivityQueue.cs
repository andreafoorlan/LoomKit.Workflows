using LoomKit.Workflows.Abstracts;
using LoomKit.Workflows.Models;

namespace LoomKit.Workflows.Contracts;

public interface IActivityQueue
{
    string ActivityQueueName { get; }
    ActivityQueueOptions ActivityQueueOptions { get; }

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);

    Task EnqueueActivityScheduleAsync(ActivitySchedule schedule, CancellationToken cancellationToken = default);
    Task<List<ActivitySchedule>> ListActivitySchedulesAsync(Func<IQueryable<ActivitySchedule>, IQueryable<ActivitySchedule>> queryBuilder, CancellationToken cancellationToken = default);
    Task<long> RemoveActivitySchedulesAsync(Predicate<ActivitySchedule> predicate, CancellationToken cancellationToken = default);
    Task<ActivitySchedule?> AwaitForActivityScheduleAsync(string consumerName, CancellationToken cancellationToken = default);

    // Returns true if a paused schedule with a matching token Value was found and unpaused.
    Task<bool> TriggerResumeTokenAsync(string tokenValue, CancellationToken cancellationToken = default);

    // Appends one branch's contribution (success or permanent failure) to its ForkId's inbox. Pure
    // append - no explicit "wake" step: a paused join-point schedule for that ForkId becomes an
    // eligible dequeue candidate as soon as its inbox is non-empty (checked by the normal poll loop),
    // and the whole inbox is drained atomically at that point. See AwaitForActivityScheduleAsync.
    Task NotifyForkJoinArrivalAsync(string forkId, string joinId, IWorkflowContext? context, bool failed, Exception? exception, CancellationToken cancellationToken = default);

    Task NotifyActivityStarted(string consumerName, ActivitySchedule schedule);
    Task NotifyActivityEnded(string consumerName, ActivitySchedule schedule);
    Task NotifyActivityException(string consumerName, ActivitySchedule schedule, Exception exception);
}

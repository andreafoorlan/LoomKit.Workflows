using LoomKit.Workflows.Abstracts;
using LoomKit.Workflows.Contracts;
using LoomKit.Workflows.Models;
using Microsoft.Extensions.Logging;

namespace LoomKit.Workflows.Defaults;

public class InProcessActivityQueue : IActivityQueue
{
    public string ActivityQueueName => _activityQueueOptions.ActivityQueueName;
    public ActivityQueueOptions ActivityQueueOptions => _activityQueueOptions;

    private readonly IWorkflowManager _workflowManager;
    private readonly ActivityQueueOptions _activityQueueOptions;
    private readonly ILogger<InProcessActivityQueue> _logger;

    private readonly SemaphoreSlim _scheduleActivitySemaphore;
    private readonly SemaphoreSlim _awaitActivitySemaphore;
    private readonly List<ActivitySchedule> _activitySchedules;

    // Per-ForkId inbox of undelivered branch arrivals (success or permanent failure), fed by
    // NotifyForkJoinArrivalAsync and drained atomically whenever the matching join-point schedule is
    // picked up by AwaitForActivityScheduleAsync. Guarded by the same semaphore as _activitySchedules
    // so an append can never interleave with a select+drain. See ActivityConsumer/WorkflowFork for
    // how ForkId/JoinId get onto schedules in the first place.
    private readonly Dictionary<string, List<JoinArrival>> _forkJoinInboxes;

    private bool _queueStarted;

    public InProcessActivityQueue(
        IWorkflowManager workflowManager,
        ActivityQueueOptions activityQueueOptions,
        ILogger<InProcessActivityQueue> logger)
    {
        _workflowManager = workflowManager;
        _activityQueueOptions = activityQueueOptions;
        _logger = logger;

        _scheduleActivitySemaphore = new SemaphoreSlim(1, 1);
        _awaitActivitySemaphore = new SemaphoreSlim(1, 1);
        _activitySchedules = new List<ActivitySchedule>();
        _forkJoinInboxes = new Dictionary<string, List<JoinArrival>>();
        _queueStarted = false;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _queueStarted = true;
        _logger.LogInformation("[{MethodName}] Activity queue {QueueName} started", nameof(StartAsync), _activityQueueOptions.ActivityQueueName);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _queueStarted = false;
        _logger.LogInformation("[{MethodName}] Activity queue {QueueName} stopped", nameof(StopAsync), _activityQueueOptions.ActivityQueueName);
        return Task.CompletedTask;
    }

    public async Task EnqueueActivityScheduleAsync(ActivitySchedule schedule, CancellationToken cancellationToken = default)
    {
        await _scheduleActivitySemaphore.WaitAsync(cancellationToken);

        schedule.ActivityStatus.QueueName = _activityQueueOptions.ActivityQueueName;
        schedule.ActivityStatus.EnqueuedAt = DateTime.UtcNow;
        _activitySchedules.Add(schedule);

        _logger.LogInformation(
            "[{QueueName}] Scheduled activity {ActivityScheduleId} at {NextAt} (paused={IsPaused})",
            _activityQueueOptions.ActivityQueueName, schedule.ActivityScheduleId, schedule.NextAt, schedule.IsPaused);

        _scheduleActivitySemaphore.Release();

        await _workflowManager.NotifyActivityScheduled(_activityQueueOptions.ActivityQueueName, schedule);
    }

    public async Task<List<ActivitySchedule>> ListActivitySchedulesAsync(
        Func<IQueryable<ActivitySchedule>, IQueryable<ActivitySchedule>> queryBuilder,
        CancellationToken cancellationToken = default)
    {
        await _scheduleActivitySemaphore.WaitAsync(cancellationToken);

        var result = queryBuilder(_activitySchedules.AsQueryable()).ToList();

        _scheduleActivitySemaphore.Release();

        return result;
    }

    public async Task<long> RemoveActivitySchedulesAsync(Predicate<ActivitySchedule> predicate, CancellationToken cancellationToken = default)
    {
        await _scheduleActivitySemaphore.WaitAsync(cancellationToken);

        var removed = _activitySchedules.RemoveAll(predicate);

        _scheduleActivitySemaphore.Release();

        return removed;
    }

    public async Task<ActivitySchedule?> AwaitForActivityScheduleAsync(string consumerName, CancellationToken cancellationToken = default)
    {
        ActivitySchedule? candidate = null;

        await _awaitActivitySemaphore.WaitAsync(cancellationToken);

        do
        {
            if (_queueStarted)
            {
                await _scheduleActivitySemaphore.WaitAsync(cancellationToken);

                // Only pick up non-paused schedules whose time has come, or paused join-point
                // schedules whose ForkId inbox has something waiting (level-triggered, see
                // IsReadyForConsumption).
                candidate = _activitySchedules
                    .Where(IsReadyForConsumption)
                    .MinBy(s => s.NextAt);

                // Atomically drain the whole inbox for a join-point candidate under the same lock
                // that guards appends (NotifyForkJoinArrivalAsync), so nothing arriving concurrently
                // can be lost between selection and drain.
                if (candidate is not null
                    && candidate.ForkId is not null
                    && candidate.JoinId is null
                    && _forkJoinInboxes.TryGetValue(candidate.ForkId, out var arrivals)
                    && arrivals.Count > 0)
                {
                    _forkJoinInboxes[candidate.ForkId] = [];
                    candidate = candidate with { TriggeredJoinArrivals = arrivals };
                }

                _scheduleActivitySemaphore.Release();

                _logger.LogTrace(
                    "[{ConsumerName}] Waiting for activity, queued={Count}",
                    consumerName, _activitySchedules.Count);
            }

            if (candidate is null)
                await Task.Delay(_activityQueueOptions.ActivityAwaitCheckInterval, cancellationToken);

        } while (candidate is null && !cancellationToken.IsCancellationRequested);

        if (candidate is not null)
            await RemoveActivitySchedulesAsync(s => s.ActivityScheduleId == candidate.ActivityScheduleId, cancellationToken);

        _awaitActivitySemaphore.Release();

        return candidate;
    }

    private bool IsReadyForConsumption(ActivitySchedule schedule)
    {
        if (!schedule.IsPaused)
            return schedule.NextAt <= DateTime.UtcNow;

        // A join-point's own schedule is recognizable as ForkId set + JoinId null (branches always
        // carry both). Resume-token-paused schedules (ForkId null) are never picked up here - they
        // only become eligible once TriggerResumeTokenAsync flips IsPaused directly.
        return schedule.ForkId is not null
            && schedule.JoinId is null
            && _forkJoinInboxes.TryGetValue(schedule.ForkId, out var arrivals)
            && arrivals.Count > 0;
    }

    public async Task<bool> TriggerResumeTokenAsync(string tokenValue, CancellationToken cancellationToken = default)
    {
        await _scheduleActivitySemaphore.WaitAsync(cancellationToken);

        var idx = _activitySchedules.FindIndex(s =>
            s.IsPaused &&
            s.ResumeTokens?.Any(t => t.Value == tokenValue) == true);

        if (idx >= 0)
        {
            var triggeredToken = _activitySchedules[idx].ResumeTokens!.First(t => t.Value == tokenValue);

            _activitySchedules[idx] = _activitySchedules[idx] with
            {
                IsPaused = false,
                NextAt = DateTime.UtcNow,
                TriggeredResumeToken = triggeredToken
            };

            _logger.LogInformation(
                "[{QueueName}] Resume token (action='{Action}') triggered for workflow {WorkflowId}",
                _activityQueueOptions.ActivityQueueName, triggeredToken.Action,
                _activitySchedules[idx].WorkflowId);

            _scheduleActivitySemaphore.Release();

            return true;
        }

        _scheduleActivitySemaphore.Release();

        return false;
    }

    public async Task NotifyForkJoinArrivalAsync(string forkId, string joinId, IWorkflowContext? context, bool failed, Exception? exception, CancellationToken cancellationToken = default)
    {
        await _scheduleActivitySemaphore.WaitAsync(cancellationToken);

        if (!_forkJoinInboxes.TryGetValue(forkId, out var inbox))
        {
            inbox = [];
            _forkJoinInboxes[forkId] = inbox;
        }

        inbox.Add(new JoinArrival
        {
            JoinId = joinId,
            Context = context,
            Failed = failed,
            Exception = exception
        });

        _logger.LogTrace(
            "[{QueueName}] Join arrival recorded for fork {ForkId}, join {JoinId} (failed={Failed})",
            _activityQueueOptions.ActivityQueueName, forkId, joinId, failed);

        // Open point (documented, not implemented): if this ForkId's join point has already resolved
        // and moved on, nobody will ever drain this entry again - it's a benign leak for the lifetime
        // of the in-memory queue. No cleanup/closure signal exists yet for "this ForkId is done".

        _scheduleActivitySemaphore.Release();
    }

    public async Task NotifyActivityStarted(string consumerName, ActivitySchedule schedule)
    {
        await _workflowManager.NotifyActivityStarted(_activityQueueOptions.ActivityQueueName, consumerName, schedule);
    }

    public async Task NotifyActivityEnded(string consumerName, ActivitySchedule schedule)
    {
        await _workflowManager.NotifyActivityEnded(_activityQueueOptions.ActivityQueueName, consumerName, schedule);
    }

    public async Task NotifyActivityException(string consumerName, ActivitySchedule schedule, Exception exception)
    {
        await _workflowManager.NotifyActivityException(_activityQueueOptions.ActivityQueueName, consumerName, schedule, exception);
    }
}

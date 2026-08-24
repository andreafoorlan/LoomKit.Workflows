using LoomKit.Workflows.Abstracts;
using LoomKit.Workflows.Contracts;
using LoomKit.Workflows.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace LoomKit.Workflows.Middlewares;

public class ActivityRetryMiddleware<TActivity, TWorkflowContext>
    : ActivityMiddleware<TActivity, TWorkflowContext>
    where TActivity : IActivity<TWorkflowContext>
    where TWorkflowContext : IWorkflowContext
{
    private readonly IWorkflowManager _workflowManager;
    private readonly ILogger<ActivityRetryMiddleware<TActivity, TWorkflowContext>> _logger;

    public ActivityRetryMiddleware(
        IActivityHandler<TActivity, TWorkflowContext> nextHandler,
        IWorkflowManager workflowManager,
        ILogger<ActivityRetryMiddleware<TActivity, TWorkflowContext>> logger)
        : base(nextHandler)
    {
        _workflowManager = workflowManager;
        _logger = logger;
    }

    public override async Task<IWorkflowStep<IActivity<TWorkflowContext>, TWorkflowContext>> HandleAsync(
        TActivity activity,
        TWorkflowContext context,
        ActivitySchedule activitySchedule,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _nextHandler.HandleAsync(activity, context, activitySchedule, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                "Exception in activity schedule {ActivityScheduleId} of type {ExceptionType}: {ExceptionMessage}",
                activitySchedule.ActivityScheduleId, exception.GetType().Name, exception.Message);

            if (activitySchedule.RetriesLeft > 1
                && !string.IsNullOrWhiteSpace(activitySchedule.ActivityStatus.QueueName))
            {
                _logger.LogDebug(
                    "Retrying activity schedule {ActivityScheduleId}, {RetriesLeft} retries left",
                    activitySchedule.ActivityScheduleId, activitySchedule.RetriesLeft - 1);

                Activity.Current?.AddEvent(new ActivityEvent("activity.retried", tags: new ActivityTagsCollection
                {
                    ["retries_left"] = activitySchedule.RetriesLeft - 1
                }));

                // Retry attempts use the same TraceParent as the current attempt so they appear as siblings
                var retrySchedule = activitySchedule with
                {
                    RetriesLeft = activitySchedule.RetriesLeft - 1,
                    NextAt = DateTime.UtcNow,
                    ActivityStatus = new ActivityStatus(),
                    TraceParent = activitySchedule.TraceParent
                };

                await _workflowManager.EnqueueActivityScheduleAsync(
                    activitySchedule.ActivityStatus.QueueName,
                    retrySchedule,
                    cancellationToken);
            }
            else
            {
                _logger.LogDebug(
                    "No retries left for activity schedule {ActivityScheduleId}",
                    activitySchedule.ActivityScheduleId);

                Activity.Current?.AddEvent(new ActivityEvent("activity.retries_exhausted", tags: new ActivityTagsCollection
                {
                    ["exception.type"] = exception.GetType().FullName!,
                    ["exception.message"] = exception.Message
                }));

                throw;
            }
        }

        // unreachable — only reached when retry is enqueued and we swallow the exception
        return new WorkflowEnd<TWorkflowContext> { Context = context };
    }
}

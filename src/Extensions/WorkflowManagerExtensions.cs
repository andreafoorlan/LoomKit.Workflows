using LoomKit.Workflows.Contracts;
using LoomKit.Workflows.Models;
using System.Diagnostics;

namespace LoomKit.Workflows.Extensions;

public static class WorkflowManagerExtensions
{
    public static async Task<ActivitySchedule> StartWorkflowAsync<TActivity, TWorkflowContext>(
        this IWorkflowManager manager,
        string queueName,
        TActivity firstActivity,
        TWorkflowContext context,
        string? workflowName = null,
        CancellationToken cancellationToken = default)
        where TActivity : IActivity<TWorkflowContext>
        where TWorkflowContext : IWorkflowContext
    {
        var retriesLeft = await ResolveInitialRetriesLeftAsync(manager, queueName, cancellationToken);

        var schedule = new ActivitySchedule
        {
            WorkflowId = context.WorkflowId,
            WorkflowName = workflowName,
            Activity = firstActivity,
            WorkflowContext = context,
            NextAt = DateTime.UtcNow,
            RetriesLeft = retriesLeft,
            ActivityStatus = new ActivityStatus(),
            TraceParent = Activity.Current?.Id
        };

        await manager.EnqueueActivityScheduleAsync(queueName, schedule, cancellationToken);

        return schedule;
    }

    public static async Task<ActivitySchedule> StartWorkflowAtAsync<TActivity, TWorkflowContext>(
        this IWorkflowManager manager,
        string queueName,
        TActivity firstActivity,
        TWorkflowContext context,
        DateTime at,
        string? workflowName = null,
        CancellationToken cancellationToken = default)
        where TActivity : IActivity<TWorkflowContext>
        where TWorkflowContext : IWorkflowContext
    {
        var retriesLeft = await ResolveInitialRetriesLeftAsync(manager, queueName, cancellationToken);

        var schedule = new ActivitySchedule
        {
            WorkflowId = context.WorkflowId,
            WorkflowName = workflowName,
            Activity = firstActivity,
            WorkflowContext = context,
            NextAt = at,
            RetriesLeft = retriesLeft,
            ActivityStatus = new ActivityStatus(),
            TraceParent = Activity.Current?.Id
        };

        await manager.EnqueueActivityScheduleAsync(queueName, schedule, cancellationToken);

        return schedule;
    }

    // Shared plumbing: look up the target queue's configured retry budget so the very first activity
    // of a new workflow starts with the same RetriesLeft its later re-enqueues get, instead of the
    // zero-value default an uninitialized int field would otherwise leave it at.
    // GetActivityQueueOptionsAsync never returns without a value - it throws if the queue doesn't exist.
    private static async Task<int> ResolveInitialRetriesLeftAsync(IWorkflowManager manager, string queueName, CancellationToken cancellationToken)
    {
        var queueOptions = await manager.GetActivityQueueOptionsAsync(queueName, cancellationToken);
        return queueOptions.MaxActivityRetries;
    }
}

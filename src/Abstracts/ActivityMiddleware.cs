using LoomKit.Workflows.Contracts;
using LoomKit.Workflows.Models;

namespace LoomKit.Workflows.Abstracts;

public abstract class ActivityMiddleware<TActivity, TWorkflowContext>
    : IActivityHandler<TActivity, TWorkflowContext>
    where TActivity : IActivity<TWorkflowContext>
    where TWorkflowContext : IWorkflowContext
{
    protected readonly IActivityHandler<TActivity, TWorkflowContext> _nextHandler;

    protected ActivityMiddleware(IActivityHandler<TActivity, TWorkflowContext> nextHandler)
    {
        _nextHandler = nextHandler;
    }

    public abstract Task<IWorkflowStep<IActivity<TWorkflowContext>, TWorkflowContext>> HandleAsync(
        TActivity activity,
        TWorkflowContext context,
        ActivitySchedule activitySchedule,
        CancellationToken cancellationToken = default);
}

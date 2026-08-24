using LoomKit.Workflows.Models;

namespace LoomKit.Workflows.Contracts;

public interface IActivityHandler<in TActivity, TWorkflowContext>
    where TActivity : IActivity<TWorkflowContext>
    where TWorkflowContext : IWorkflowContext
{
    Task<IWorkflowStep<IActivity<TWorkflowContext>, TWorkflowContext>> HandleAsync(
        TActivity activity,
        TWorkflowContext context,
        ActivitySchedule activitySchedule,
        CancellationToken cancellationToken = default);
}

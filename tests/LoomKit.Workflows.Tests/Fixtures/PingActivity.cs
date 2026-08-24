using LoomKit.Workflows.Abstracts;
using LoomKit.Workflows.Contracts;
using LoomKit.Workflows.Models;

namespace LoomKit.Workflows.Tests.Fixtures;

public sealed record PingActivity : Activity<TestWorkflowContext>;

public sealed class PingActivityHandler : IActivityHandler<PingActivity, TestWorkflowContext>
{
    public Task<IWorkflowStep<IActivity<TestWorkflowContext>, TestWorkflowContext>> HandleAsync(
        PingActivity activity,
        TestWorkflowContext context,
        ActivitySchedule activitySchedule,
        CancellationToken cancellationToken = default)
    {
        context.Trace.Add("ping");

        return Task.FromResult<IWorkflowStep<IActivity<TestWorkflowContext>, TestWorkflowContext>>(
            new WorkflowEnd<TestWorkflowContext> { Context = context });
    }
}

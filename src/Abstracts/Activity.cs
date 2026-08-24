using LoomKit.Workflows.Contracts;

namespace LoomKit.Workflows.Abstracts;

public abstract record Activity<TWorkflowContext> : IActivity<TWorkflowContext>
    where TWorkflowContext : IWorkflowContext
{
    public string ActivityId { get; init; } = Guid.NewGuid().ToString();
    public string? PreviousActivityId { get; init; }
}

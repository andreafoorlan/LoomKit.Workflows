using LoomKit.Workflows.Contracts;

namespace LoomKit.Workflows.Abstracts;

// Optional convenience base, mirroring Activity<TCtx>'s role for IActivity<TCtx>: pre-fills the three
// boilerplate members every IWorkflowContext needs (WorkflowId/CreatedAt/UpdatedAt) with sensible
// defaults, so a concrete context only has to declare its own domain fields. Implementing
// IWorkflowContext directly instead of extending this is still fully supported - nothing in the
// library requires this base.
public abstract record WorkflowContext : IWorkflowContext
{
    public string WorkflowId { get; init; } = Guid.NewGuid().ToString();
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}

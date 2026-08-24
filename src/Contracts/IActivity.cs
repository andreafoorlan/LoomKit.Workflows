namespace LoomKit.Workflows.Contracts;

public interface IActivity
{
    string ActivityId { get; }
    string? PreviousActivityId { get; }
}

public interface IActivity<TWorkflowContext> : IActivity
    where TWorkflowContext : IWorkflowContext { }

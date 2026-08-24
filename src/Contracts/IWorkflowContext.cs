namespace LoomKit.Workflows.Contracts;

public interface IWorkflowContext
{
    string WorkflowId { get; }
    DateTime CreatedAt { get; }
    DateTime UpdatedAt { get; }
}

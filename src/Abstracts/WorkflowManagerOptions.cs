namespace LoomKit.Workflows.Abstracts;

// Not `required`: a type with required members can't satisfy the new() constraint that
// WorkflowManagerOptionsBuilder<T>.Build() relies on (see sibling JobSchedulerOptions in Jobs, which
// avoids `required` for the same reason).
public abstract class WorkflowManagerOptions
{
    public Dictionary<string, ActivityQueueOptions> ActivityQueueOptions { get; init; } = new();
    public Dictionary<string, ActivityConsumerOptions> ActivityConsumerOptions { get; init; } = new();
    public List<Type> WorkflowManagerSeeders { get; init; } = [];
}

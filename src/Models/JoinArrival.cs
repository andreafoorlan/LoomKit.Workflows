using LoomKit.Workflows.Contracts;

namespace LoomKit.Workflows.Models;

// One branch's contribution to a fork's join point - either a successful WorkflowJoin (Failed=false,
// Context carries whatever the branch produced) or a permanent failure (retries exhausted, the
// branch will never run again). Both are delivered to the join point's schedule through the same
// channel (IActivityQueue.NotifyForkJoinArrivalAsync) so the join-point handler never has to infer
// a branch's disappearance by polling for absence - see ActivityConsumer.
public record JoinArrival
{
    public required string JoinId { get; init; }
    public IWorkflowContext? Context { get; init; }
    public bool Failed { get; init; }
    public Exception? Exception { get; init; }
}

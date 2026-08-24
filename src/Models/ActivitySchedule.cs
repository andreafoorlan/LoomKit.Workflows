using LoomKit.Workflows.Contracts;

namespace LoomKit.Workflows.Models;

public record ActivitySchedule
{
    public string ActivityScheduleId { get; init; } = Guid.NewGuid().ToString();

    public required string WorkflowId { get; init; }

    // Optional, non-unique label for "what kind of workflow this instance is" - unlike WorkflowId,
    // several concurrent instances can legitimately share the same WorkflowName. Lives here (not on
    // IWorkflowContext) so it survives every re-enqueue the consumer performs itself, instead of
    // depending on user handler code carrying it through context `with` expressions. Enables
    // dedup/observability queries (filter ListQueuedActivitySchedulesAsync/ListConsumingActivitySchedulesAsync
    // by name) and is also emitted as the `workflow.name` tracing tag.
    public string? WorkflowName { get; init; }

    public required IActivity Activity { get; init; }

    public required IWorkflowContext WorkflowContext { get; init; }

    public required DateTime NextAt { get; init; }

    public bool IsPaused { get; init; }

    public IReadOnlyList<ResumeToken>? ResumeTokens { get; init; }

    // Populated by TriggerResumeTokenAsync with the specific token that triggered the resume.
    // Activity handlers read TriggeredResumeToken.Action to decide the next workflow step.
    public ResumeToken? TriggeredResumeToken { get; init; }

    // Fork/join correlation. ForkId is shared by every branch spawned by one WorkflowFork and by its
    // (optional) join-point schedule; JoinId identifies one specific branch's slot and is null on the
    // join-point's own schedule. See Models/WorkflowStep.cs (WorkflowFork/WorkflowJoin/WorkflowJoinWait).
    public string? ForkId { get; init; }
    public string? JoinId { get; init; }

    // Populated by the queue when it wakes a paused join-point schedule: the full batch of
    // JoinArrival items accumulated in that ForkId's inbox since the join point last ran, drained
    // atomically so concurrently-completing branches are never lost between two dispatches.
    public IReadOnlyList<JoinArrival>? TriggeredJoinArrivals { get; init; }

    public int RetriesLeft { get; init; }

    public required ActivityStatus ActivityStatus { get; init; }

    // W3C traceparent of the span that enqueued this activity, used to restore trace context across async boundaries
    public string? TraceParent { get; init; }
}

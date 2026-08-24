using LoomKit.Workflows.Abstracts;
using LoomKit.Workflows.Contracts;

namespace LoomKit.Workflows.Models;

public abstract record WorkflowStep<TNext, TWorkflowContext> : IWorkflowStep<TNext, TWorkflowContext>
    where TNext : IActivity<TWorkflowContext>
    where TWorkflowContext : IWorkflowContext
{
    public TWorkflowContext? Context { get; init; }
}

public record WorkflowContinue<TNext, TWorkflowContext> : WorkflowStep<TNext, TWorkflowContext>, IWorkflowNextStep
    where TNext : IActivity<TWorkflowContext>
    where TWorkflowContext : IWorkflowContext
{
    public required TNext Next { get; init; }

    // Null = immediate (today's default behavior). Set it to implement bounded polling/timeout
    // patterns ("recheck in 30s"); this is deliberately unrelated to, and not a substitute for,
    // unbounded cron-style recurrence - see the library's README for that distinction.
    public DateTime? NextAt { get; init; }

    IActivity IWorkflowNextStep.NextActivity => Next;
    IWorkflowContext? IWorkflowNextStep.NextContext => Context;
    bool IWorkflowNextStep.IsPaused => false;
    IReadOnlyList<ResumeToken>? IWorkflowNextStep.ResumeTokens => null;
}

public record WorkflowPause<TNext, TWorkflowContext> : WorkflowStep<TNext, TWorkflowContext>, IWorkflowNextStep
    where TNext : IActivity<TWorkflowContext>
    where TWorkflowContext : IWorkflowContext
{
    public required TNext Next { get; init; }
    // Each ResumeToken.Value is a GUID communicated externally; Action is the internal routing label.
    // Values must be unique within the list.
    public required IReadOnlyList<ResumeToken> ResumeTokens { get; init; }

    // Null = eligible for resume as soon as TriggerResumeTokenAsync fires (today's behavior). Kept
    // for symmetry with WorkflowContinue; rarely useful alongside ResumeTokens.
    public DateTime? NextAt { get; init; }

    IActivity IWorkflowNextStep.NextActivity => Next;
    IWorkflowContext? IWorkflowNextStep.NextContext => Context;
    bool IWorkflowNextStep.IsPaused => true;
    IReadOnlyList<ResumeToken>? IWorkflowNextStep.ResumeTokens => ResumeTokens;
}

// Sentinel type used as TNext in WorkflowEnd, WorkflowFork and WorkflowJoin to satisfy the generic constraint
public sealed record WorkflowTerminator<TWorkflowContext> : Activity<TWorkflowContext>
    where TWorkflowContext : IWorkflowContext;

public record WorkflowEnd<TWorkflowContext> : WorkflowStep<WorkflowTerminator<TWorkflowContext>, TWorkflowContext>
    where TWorkflowContext : IWorkflowContext;

// A deliberate, final failure outcome - distinct from an unhandled exception. Returned normally (not
// thrown), so it bypasses ActivityRetryMiddleware entirely: middleware only intercepts exceptions,
// never inspects a handler's return value. Use this when business logic has decided the workflow (or
// this branch) must end in failure and retrying would not help; throw instead for a transient error
// you want retried. When returned by a branch of a fork with a join point (ForkId/JoinId set on the
// branch's own ActivitySchedule), it is delivered to the join point the same way a permanently-failed
// branch is - see ActivityConsumer.
public record WorkflowFail<TWorkflowContext> : WorkflowStep<WorkflowTerminator<TWorkflowContext>, TWorkflowContext>
    where TWorkflowContext : IWorkflowContext
{
    public Exception? Exception { get; init; }
}

// A branch's terminal signal when its fork has a join point: instead of simply vanishing like a plain
// WorkflowEnd, the branch contributes Context to its ForkId's join point. ForkId/JoinId are not
// carried on this step - the consumer reads them off the branch's own ActivitySchedule, where they
// were stamped when the branch was enqueued (see WorkflowFork/ActivityConsumer).
public record WorkflowJoin<TWorkflowContext> : WorkflowStep<WorkflowTerminator<TWorkflowContext>, TWorkflowContext>
    where TWorkflowContext : IWorkflowContext;

// Returned by a join-point activity's handler when it decides it doesn't have enough arrivals yet:
// Next is a new instance of the join-point activity (typically built via `with {}`, folding in
// whatever this dispatch's ActivitySchedule.TriggeredJoinArrivals batch contributed), re-enqueued
// paused-for-join under the same ForkId. Distinct from WorkflowPause: this is woken by sibling branch
// arrivals through the queue's per-ForkId inbox, not by an external TriggerResumeTokenAsync.
public record WorkflowJoinWait<TNext, TWorkflowContext> : WorkflowStep<TNext, TWorkflowContext>, IWorkflowJoinWaitStep
    where TNext : IActivity<TWorkflowContext>
    where TWorkflowContext : IWorkflowContext
{
    public required TNext Next { get; init; }

    IActivity IWorkflowJoinWaitStep.NextActivity => Next;
    IWorkflowContext? IWorkflowJoinWaitStep.NextContext => Context;
}

// One branch of a fork, paired with the JoinId slot assigned to it by the forking handler (the fork
// decides JoinIds upfront as semantic labels, e.g. "payment"/"inventory" - they are not generated by
// the branch itself). JoinId can be left null for a branch that doesn't matter to the join point (or
// when the fork has no JoinPointActivity at all).
public record ForkBranch<TWorkflowContext> : IWorkflowForkBranch
    where TWorkflowContext : IWorkflowContext
{
    public string? JoinId { get; init; }
    public required IActivity<TWorkflowContext> Activity { get; init; }

    IActivity IWorkflowForkBranch.Activity => Activity;

    public static ForkBranch<TWorkflowContext> Of(IActivity<TWorkflowContext> activity, string? joinId = null)
        => new() { JoinId = joinId, Activity = activity };
}

// Spawns N independent branches that all share the same WorkflowId and continue in the same queue.
// With JoinPointActivity left null, behaves exactly as a fork always has: each branch runs to its own
// WorkflowEnd independently, no join/barrier mechanism. Setting JoinPointActivity additionally
// enqueues it, paused, as the fork's join point (see IWorkflowForkStep.JoinPointActivity) - branches
// that return WorkflowJoin then feed it via ForkId/JoinId instead of ending on their own.
public record WorkflowFork<TWorkflowContext> : WorkflowStep<WorkflowTerminator<TWorkflowContext>, TWorkflowContext>, IWorkflowForkStep
    where TWorkflowContext : IWorkflowContext
{
    public string ForkId { get; init; } = Guid.NewGuid().ToString();
    public required IReadOnlyList<ForkBranch<TWorkflowContext>> Branches { get; init; }
    public IActivity<TWorkflowContext>? JoinPointActivity { get; init; }

    IEnumerable<IWorkflowForkBranch> IWorkflowForkStep.Branches => Branches;
    IWorkflowContext? IWorkflowForkStep.NextContext => Context;
    IActivity? IWorkflowForkStep.JoinPointActivity => JoinPointActivity;
}

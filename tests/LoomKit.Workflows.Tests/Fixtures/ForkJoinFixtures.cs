using LoomKit.Workflows.Abstracts;
using LoomKit.Workflows.Contracts;
using LoomKit.Workflows.Models;

namespace LoomKit.Workflows.Tests.Fixtures;

public enum FirstBranchOutcome
{
    Succeed,
    ThrowPermanently,
    ExplicitFail
}

// ForkingActivity forks into one BranchActivity per JoinId (optionally replacing the first branch
// with one that throws or explicitly returns WorkflowFail), plus a CollectJoinPointActivity waiting
// for all of them.
public sealed record ForkingActivity : Activity<TestWorkflowContext>
{
    public required IReadOnlyList<string> JoinIds { get; init; }
    public FirstBranchOutcome FirstBranchOutcome { get; init; } = FirstBranchOutcome.Succeed;
}

public sealed class ForkingActivityHandler : IActivityHandler<ForkingActivity, TestWorkflowContext>
{
    public Task<IWorkflowStep<IActivity<TestWorkflowContext>, TestWorkflowContext>> HandleAsync(
        ForkingActivity activity,
        TestWorkflowContext context,
        ActivitySchedule activitySchedule,
        CancellationToken cancellationToken = default)
    {
        var branches = activity.JoinIds
            .Select((joinId, index) =>
            {
                IActivity<TestWorkflowContext> branchActivity = index == 0
                    ? activity.FirstBranchOutcome switch
                    {
                        FirstBranchOutcome.ThrowPermanently => new FailingBranchActivity(),
                        FirstBranchOutcome.ExplicitFail => new ExplicitlyFailingBranchActivity(),
                        _ => new BranchActivity { Label = joinId }
                    }
                    : new BranchActivity { Label = joinId };

                return ForkBranch<TestWorkflowContext>.Of(branchActivity, joinId);
            })
            .ToList();

        var fork = new WorkflowFork<TestWorkflowContext>
        {
            Branches = branches,
            JoinPointActivity = new CollectJoinPointActivity { ExpectedJoinIds = activity.JoinIds },
            Context = context
        };

        return Task.FromResult<IWorkflowStep<IActivity<TestWorkflowContext>, TestWorkflowContext>>(fork);
    }
}

public sealed record BranchActivity : Activity<TestWorkflowContext>
{
    public required string Label { get; init; }
}

public sealed class BranchActivityHandler : IActivityHandler<BranchActivity, TestWorkflowContext>
{
    public Task<IWorkflowStep<IActivity<TestWorkflowContext>, TestWorkflowContext>> HandleAsync(
        BranchActivity activity,
        TestWorkflowContext context,
        ActivitySchedule activitySchedule,
        CancellationToken cancellationToken = default)
    {
        context.Trace.Add($"branch:{activity.Label}");

        return Task.FromResult<IWorkflowStep<IActivity<TestWorkflowContext>, TestWorkflowContext>>(
            new WorkflowJoin<TestWorkflowContext> { Context = context });
    }
}

// Always throws - no retry middleware configured on the test consumer, so this fails permanently on
// the first attempt, letting ActivityConsumer deliver it to the join point as JoinArrival.Failed.
public sealed record FailingBranchActivity : Activity<TestWorkflowContext>;

public sealed class FailingBranchActivityHandler : IActivityHandler<FailingBranchActivity, TestWorkflowContext>
{
    public Task<IWorkflowStep<IActivity<TestWorkflowContext>, TestWorkflowContext>> HandleAsync(
        FailingBranchActivity activity,
        TestWorkflowContext context,
        ActivitySchedule activitySchedule,
        CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Simulated permanent branch failure.");
    }
}

// Fails on purpose, without throwing - exercises the WorkflowFail delivery path (as opposed to
// FailingBranchActivity's exhausted-retries exception path) into the same join-point inbox.
public sealed record ExplicitlyFailingBranchActivity : Activity<TestWorkflowContext>;

public sealed class ExplicitlyFailingBranchActivityHandler : IActivityHandler<ExplicitlyFailingBranchActivity, TestWorkflowContext>
{
    public Task<IWorkflowStep<IActivity<TestWorkflowContext>, TestWorkflowContext>> HandleAsync(
        ExplicitlyFailingBranchActivity activity,
        TestWorkflowContext context,
        ActivitySchedule activitySchedule,
        CancellationToken cancellationToken = default)
    {
        context.Trace.Add("branch:explicit-fail");

        return Task.FromResult<IWorkflowStep<IActivity<TestWorkflowContext>, TestWorkflowContext>>(
            new WorkflowFail<TestWorkflowContext> { Context = context, Exception = new InvalidOperationException("Declined by business rule.") });
    }
}

// A standalone (non-fork) activity that fails on purpose.
public sealed record FailOutrightActivity : Activity<TestWorkflowContext>;

public sealed class FailOutrightActivityHandler : IActivityHandler<FailOutrightActivity, TestWorkflowContext>
{
    public Task<IWorkflowStep<IActivity<TestWorkflowContext>, TestWorkflowContext>> HandleAsync(
        FailOutrightActivity activity,
        TestWorkflowContext context,
        ActivitySchedule activitySchedule,
        CancellationToken cancellationToken = default)
    {
        context.Trace.Add("fail-outright");

        return Task.FromResult<IWorkflowStep<IActivity<TestWorkflowContext>, TestWorkflowContext>>(
            new WorkflowFail<TestWorkflowContext> { Context = context, Exception = new InvalidOperationException("boom") });
    }
}

// Accumulates JoinArrivals across possibly multiple dispatches (folded into Received via `with {}`)
// until every ExpectedJoinIds slot has reported in, then ends.
public sealed record CollectJoinPointActivity : Activity<TestWorkflowContext>
{
    public required IReadOnlyList<string> ExpectedJoinIds { get; init; }
    public IReadOnlyDictionary<string, bool> Received { get; init; } = new Dictionary<string, bool>();
}

public sealed class CollectJoinPointActivityHandler : IActivityHandler<CollectJoinPointActivity, TestWorkflowContext>
{
    public Task<IWorkflowStep<IActivity<TestWorkflowContext>, TestWorkflowContext>> HandleAsync(
        CollectJoinPointActivity activity,
        TestWorkflowContext context,
        ActivitySchedule activitySchedule,
        CancellationToken cancellationToken = default)
    {
        var received = new Dictionary<string, bool>(activity.Received);

        foreach (var arrival in activitySchedule.TriggeredJoinArrivals ?? [])
            received[arrival.JoinId] = !arrival.Failed;

        context.Trace.Add($"join-point:{received.Count}/{activity.ExpectedJoinIds.Count}");

        if (received.Count < activity.ExpectedJoinIds.Count)
        {
            var next = activity with { Received = received };

            return Task.FromResult<IWorkflowStep<IActivity<TestWorkflowContext>, TestWorkflowContext>>(
                new WorkflowJoinWait<CollectJoinPointActivity, TestWorkflowContext> { Next = next, Context = context });
        }

        context.Trace.Add("join-point:done");

        return Task.FromResult<IWorkflowStep<IActivity<TestWorkflowContext>, TestWorkflowContext>>(
            new WorkflowEnd<TestWorkflowContext> { Context = context });
    }
}

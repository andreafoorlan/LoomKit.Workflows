using LoomKit.Workflows.Abstracts;
using LoomKit.Workflows.Contracts;
using LoomKit.Workflows.Models;

namespace LoomKit.Workflows.Tests.Fixtures;

// FirstActivity -> WorkflowContinue -> SecondActivity -> WorkflowEnd
public sealed record FirstActivity : Activity<TestWorkflowContext>;

public sealed class FirstActivityHandler : IActivityHandler<FirstActivity, TestWorkflowContext>
{
    public Task<IWorkflowStep<IActivity<TestWorkflowContext>, TestWorkflowContext>> HandleAsync(
        FirstActivity activity,
        TestWorkflowContext context,
        ActivitySchedule activitySchedule,
        CancellationToken cancellationToken = default)
    {
        context.Trace.Add("first");

        return Task.FromResult<IWorkflowStep<IActivity<TestWorkflowContext>, TestWorkflowContext>>(
            new WorkflowContinue<SecondActivity, TestWorkflowContext> { Next = new SecondActivity(), Context = context });
    }
}

public sealed record SecondActivity : Activity<TestWorkflowContext>;

public sealed class SecondActivityHandler : IActivityHandler<SecondActivity, TestWorkflowContext>
{
    public Task<IWorkflowStep<IActivity<TestWorkflowContext>, TestWorkflowContext>> HandleAsync(
        SecondActivity activity,
        TestWorkflowContext context,
        ActivitySchedule activitySchedule,
        CancellationToken cancellationToken = default)
    {
        context.Trace.Add("second");

        return Task.FromResult<IWorkflowStep<IActivity<TestWorkflowContext>, TestWorkflowContext>>(
            new WorkflowEnd<TestWorkflowContext> { Context = context });
    }
}

// PausingActivity -> WorkflowPause(token "go") -> AfterResumeActivity -> WorkflowEnd
public sealed record PausingActivity : Activity<TestWorkflowContext>;

public sealed class PausingActivityHandler : IActivityHandler<PausingActivity, TestWorkflowContext>
{
    public string? IssuedTokenValue { get; private set; }

    public Task<IWorkflowStep<IActivity<TestWorkflowContext>, TestWorkflowContext>> HandleAsync(
        PausingActivity activity,
        TestWorkflowContext context,
        ActivitySchedule activitySchedule,
        CancellationToken cancellationToken = default)
    {
        context.Trace.Add("pausing");

        var token = new ResumeToken { Action = "go" };
        IssuedTokenValue = token.Value;

        return Task.FromResult<IWorkflowStep<IActivity<TestWorkflowContext>, TestWorkflowContext>>(
            new WorkflowPause<AfterResumeActivity, TestWorkflowContext>
            {
                Next = new AfterResumeActivity(),
                ResumeTokens = [token],
                Context = context
            });
    }
}

public sealed record AfterResumeActivity : Activity<TestWorkflowContext>;

public sealed class AfterResumeActivityHandler : IActivityHandler<AfterResumeActivity, TestWorkflowContext>
{
    public Task<IWorkflowStep<IActivity<TestWorkflowContext>, TestWorkflowContext>> HandleAsync(
        AfterResumeActivity activity,
        TestWorkflowContext context,
        ActivitySchedule activitySchedule,
        CancellationToken cancellationToken = default)
    {
        context.Trace.Add($"resumed:{activitySchedule.TriggeredResumeToken?.Action}");

        return Task.FromResult<IWorkflowStep<IActivity<TestWorkflowContext>, TestWorkflowContext>>(
            new WorkflowEnd<TestWorkflowContext> { Context = context });
    }
}

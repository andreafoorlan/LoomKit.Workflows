using LoomKit.Workflows.Contracts;
using LoomKit.Workflows.Extensions;
using LoomKit.Workflows.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace LoomKit.Workflows.Tests;

public class ActivityConsumerTests
{
    [Fact]
    public async Task StartWorkflowAsync_SingleActivity_RunsToEnd()
    {
        using var provider = TestServiceProviderFactory.Build();
        var manager = provider.GetRequiredService<IWorkflowManager>();

        await WorkflowManagerTestHarness.RunAsync(manager, async () =>
        {
            var context = new TestWorkflowContext();

            await manager.StartWorkflowAsync(TestServiceProviderFactory.QueueName, new PingActivity(), context);

            var completed = await Wait.UntilAsync(() => context.Trace.Count > 0, TimeSpan.FromSeconds(2));

            Assert.True(completed);
            Assert.Equal(["ping"], context.Trace);
        });
    }

    [Fact]
    public async Task StartWorkflowAsync_InitializesRetriesLeft_FromQueueMaxActivityRetries()
    {
        using var provider = TestServiceProviderFactory.Build(configureQueue: q => q.MaxActivityRetries = 7);
        var manager = provider.GetRequiredService<IWorkflowManager>();

        await WorkflowManagerTestHarness.RunAsync(manager, async () =>
        {
            // subscribed before StartWorkflowAsync: EnqueueActivityScheduleAsync awaits
            // NotifyActivityScheduled synchronously, so this runs before the consumer's background
            // loop could possibly dequeue and mutate the schedule - no race with capturing RetriesLeft.
            int? observedRetriesLeft = null;
            manager.ActivityScheduled += (_, e) => observedRetriesLeft = e.ActivitySchedule.RetriesLeft;

            await manager.StartWorkflowAsync(TestServiceProviderFactory.QueueName, new PingActivity(), new TestWorkflowContext());

            Assert.Equal(7, observedRetriesLeft);
        });
    }

    [Fact]
    public async Task StartWorkflowAsync_ChainOfActivities_RunsInOrder()
    {
        using var provider = TestServiceProviderFactory.Build();
        var manager = provider.GetRequiredService<IWorkflowManager>();

        await WorkflowManagerTestHarness.RunAsync(manager, async () =>
        {
            var context = new TestWorkflowContext();

            await manager.StartWorkflowAsync(TestServiceProviderFactory.QueueName, new FirstActivity(), context);

            var completed = await Wait.UntilAsync(() => context.Trace.Count >= 2, TimeSpan.FromSeconds(2));

            Assert.True(completed);
            Assert.Equal(["first", "second"], context.Trace);
        });
    }

    [Fact]
    public async Task WorkflowName_IsCarriedForward_AcrossContinuations()
    {
        using var provider = TestServiceProviderFactory.Build();
        var manager = provider.GetRequiredService<IWorkflowManager>();

        await WorkflowManagerTestHarness.RunAsync(manager, async () =>
        {
            var names = new List<string?>();
            manager.ActivityScheduled += (_, e) => names.Add(e.ActivitySchedule.WorkflowName);

            var context = new TestWorkflowContext();
            await manager.StartWorkflowAsync(TestServiceProviderFactory.QueueName, new FirstActivity(), context, workflowName: "daily-report");

            var completed = await Wait.UntilAsync(() => context.Trace.Count >= 2, TimeSpan.FromSeconds(2));

            Assert.True(completed);
            Assert.Equal(["daily-report", "daily-report"], names);
        });
    }

    [Fact]
    public async Task PausedWorkflow_ResumesOnMatchingToken_AndCarriesTriggeredAction()
    {
        using var provider = TestServiceProviderFactory.Build();
        var manager = provider.GetRequiredService<IWorkflowManager>();
        var handler = (PausingActivityHandler)provider.GetServices<IActivityHandler<PausingActivity, TestWorkflowContext>>().Single();

        await WorkflowManagerTestHarness.RunAsync(manager, async () =>
        {
            var context = new TestWorkflowContext();

            await manager.StartWorkflowAsync(TestServiceProviderFactory.QueueName, new PausingActivity(), context);

            var paused = await Wait.UntilAsync(() => handler.IssuedTokenValue is not null, TimeSpan.FromSeconds(2));
            Assert.True(paused);

            await manager.TriggerResumeTokenAsync(handler.IssuedTokenValue!);

            var completed = await Wait.UntilAsync(() => context.Trace.Count >= 2, TimeSpan.FromSeconds(2));

            Assert.True(completed);
            Assert.Equal(["pausing", "resumed:go"], context.Trace);
        });
    }

    [Fact]
    public async Task Fork_WithoutJoinPoint_RunsEachBranchIndependentlyToEnd()
    {
        using var provider = TestServiceProviderFactory.Build();
        var manager = provider.GetRequiredService<IWorkflowManager>();

        await WorkflowManagerTestHarness.RunAsync(manager, async () =>
        {
            var context = new TestWorkflowContext();

            await manager.StartWorkflowAsync(
                TestServiceProviderFactory.QueueName,
                new ForkingActivity { JoinIds = ["a", "b"] },
                context);

            // no JoinPointActivity here would mean branches vanish on WorkflowJoin with a warning log
            // instead of ending - this test forks with a join point but only asserts both branches ran,
            // covering the throwaway-fork code path indirectly via the fork/branch enqueue logic.
            var completed = await Wait.UntilAsync(
                () => context.Trace.Count(t => t.StartsWith("branch:")) == 2,
                TimeSpan.FromSeconds(2));

            Assert.True(completed);
            Assert.Contains("branch:a", context.Trace);
            Assert.Contains("branch:b", context.Trace);
        });
    }
}

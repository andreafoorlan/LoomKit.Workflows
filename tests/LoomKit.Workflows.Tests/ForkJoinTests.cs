using LoomKit.Workflows.Contracts;
using LoomKit.Workflows.Extensions;
using LoomKit.Workflows.Models;
using LoomKit.Workflows.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace LoomKit.Workflows.Tests;

public class ForkJoinTests
{
    [Fact]
    public async Task ForkWithJoinPoint_AllBranchesSucceed_JoinPointCompletesOnceAllArrive()
    {
        using var provider = TestServiceProviderFactory.Build();
        var manager = provider.GetRequiredService<IWorkflowManager>();

        await WorkflowManagerTestHarness.RunAsync(manager, async () =>
        {
            var context = new TestWorkflowContext();

            await manager.StartWorkflowAsync(
                TestServiceProviderFactory.QueueName,
                new ForkingActivity { JoinIds = ["a", "b", "c"] },
                context);

            var completed = await Wait.UntilAsync(
                () => context.Trace.Contains("join-point:done"),
                TimeSpan.FromSeconds(3));

            Assert.True(completed);
            Assert.Contains("branch:a", context.Trace);
            Assert.Contains("branch:b", context.Trace);
            Assert.Contains("branch:c", context.Trace);
            Assert.Equal("join-point:done", context.Trace[^1]);
        });
    }

    [Fact]
    public async Task ForkWithJoinPoint_OneBranchFailsPermanently_DeliveredAsFailedArrival_JoinPointStillCompletes()
    {
        // no retry middleware configured on the test consumer, so FailingBranchActivity fails
        // permanently on its first (and only) attempt
        using var provider = TestServiceProviderFactory.Build();
        var manager = provider.GetRequiredService<IWorkflowManager>();

        await WorkflowManagerTestHarness.RunAsync(manager, async () =>
        {
            var context = new TestWorkflowContext();

            await manager.StartWorkflowAsync(
                TestServiceProviderFactory.QueueName,
                new ForkingActivity { JoinIds = ["a", "b"], FirstBranchOutcome = FirstBranchOutcome.ThrowPermanently },
                context);

            // without the failure-as-first-class-arrival fix, the join point would wait forever for
            // "a" and this would time out instead of completing
            var completed = await Wait.UntilAsync(
                () => context.Trace.Contains("join-point:done"),
                TimeSpan.FromSeconds(3));

            Assert.True(completed);
            Assert.Contains("branch:b", context.Trace);
            Assert.DoesNotContain("branch:a", context.Trace); // "a" was replaced by the failing activity
        });
    }

    [Fact]
    public async Task ForkWithJoinPoint_OneBranchExplicitlyFails_DeliveredAsFailedArrival_JoinPointStillCompletes()
    {
        using var provider = TestServiceProviderFactory.Build();
        var manager = provider.GetRequiredService<IWorkflowManager>();

        await WorkflowManagerTestHarness.RunAsync(manager, async () =>
        {
            var context = new TestWorkflowContext();

            await manager.StartWorkflowAsync(
                TestServiceProviderFactory.QueueName,
                new ForkingActivity { JoinIds = ["a", "b"], FirstBranchOutcome = FirstBranchOutcome.ExplicitFail },
                context);

            var completed = await Wait.UntilAsync(
                () => context.Trace.Contains("join-point:done"),
                TimeSpan.FromSeconds(3));

            Assert.True(completed);
            Assert.Contains("branch:explicit-fail", context.Trace);
            Assert.Contains("branch:b", context.Trace);
        });
    }

    [Fact]
    public async Task WorkflowFail_StandaloneActivity_EndsTheWorkflow_ButDoesNotRaiseActivityException()
    {
        using var provider = TestServiceProviderFactory.Build();
        var manager = provider.GetRequiredService<IWorkflowManager>();

        await WorkflowManagerTestHarness.RunAsync(manager, async () =>
        {
            var exceptionRaised = false;
            manager.ActivityException += (_, _) => exceptionRaised = true;

            var context = new TestWorkflowContext();
            await manager.StartWorkflowAsync(TestServiceProviderFactory.QueueName, new FailOutrightActivity(), context);

            var completed = await Wait.UntilAsync(() => context.Trace.Contains("fail-outright"), TimeSpan.FromSeconds(2));

            Assert.True(completed);
            // give the consumer loop a moment to also fire NotifyActivityEnded/NotifyActivityException
            // (both happen right after HandleActivityScheduleAsync returns, same as a normal WorkflowEnd)
            await Task.Delay(50);
            Assert.False(exceptionRaised);
        });
    }

    [Fact]
    public async Task InProcessActivityQueue_ConcurrentJoinArrivals_AreAllDeliveredInOneBatch_NoneLost()
    {
        using var provider = TestServiceProviderFactory.BuildQueueOnly();
        var manager = provider.GetRequiredService<IWorkflowManager>();

        await manager.StartAsync(CancellationToken.None);
        try
        {
            var queues = await manager.ListQueuesAsync();
            var queue = queues[TestServiceProviderFactory.QueueName];

            const string forkId = "race-fork";
            var joinIds = Enumerable.Range(0, 25).Select(i => $"branch-{i}").ToArray();

            var joinPointSchedule = new ActivitySchedule
            {
                WorkflowId = "race-workflow",
                Activity = new CollectJoinPointActivity { ExpectedJoinIds = joinIds },
                WorkflowContext = new TestWorkflowContext(),
                NextAt = DateTime.UtcNow,
                IsPaused = true,
                ForkId = forkId,
                ActivityStatus = new ActivityStatus()
            };

            await queue.EnqueueActivityScheduleAsync(joinPointSchedule);

            // fire every arrival concurrently, before anybody drains the inbox - this is the scenario
            // that would lose updates under a naive "single-slot inject" (TriggerResumeTokenAsync-style)
            // design instead of the per-ForkId inbox + atomic batch drain this queue implements
            await Task.WhenAll(joinIds.Select(joinId =>
                queue.NotifyForkJoinArrivalAsync(forkId, joinId, null, failed: false, exception: null)));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var picked = await queue.AwaitForActivityScheduleAsync("test-consumer", cts.Token);

            Assert.NotNull(picked);
            Assert.NotNull(picked!.TriggeredJoinArrivals);
            Assert.Equal(joinIds.Length, picked.TriggeredJoinArrivals!.Count);
            Assert.Equal(
                joinIds.OrderBy(x => x),
                picked.TriggeredJoinArrivals.Select(a => a.JoinId).OrderBy(x => x));
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task InProcessActivityQueue_SecondDrain_DoesNotRedeliverAlreadyDrainedArrivals()
    {
        using var provider = TestServiceProviderFactory.BuildQueueOnly();
        var manager = provider.GetRequiredService<IWorkflowManager>();

        await manager.StartAsync(CancellationToken.None);
        try
        {
            var queues = await manager.ListQueuesAsync();
            var queue = queues[TestServiceProviderFactory.QueueName];

            const string forkId = "two-batch-fork";

            var joinPointSchedule = new ActivitySchedule
            {
                WorkflowId = "two-batch-workflow",
                Activity = new CollectJoinPointActivity { ExpectedJoinIds = ["a", "b"] },
                WorkflowContext = new TestWorkflowContext(),
                NextAt = DateTime.UtcNow,
                IsPaused = true,
                ForkId = forkId,
                ActivityStatus = new ActivityStatus()
            };

            await queue.EnqueueActivityScheduleAsync(joinPointSchedule);
            await queue.NotifyForkJoinArrivalAsync(forkId, "a", null, failed: false, exception: null);

            using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var first = await queue.AwaitForActivityScheduleAsync("test-consumer", cts1.Token);

            Assert.NotNull(first);
            Assert.Equal(["a"], first!.TriggeredJoinArrivals!.Select(a => a.JoinId));

            // simulate the join-point handler deciding "not enough yet" and re-pausing itself
            var rePaused = first with { TriggeredJoinArrivals = null };
            await queue.EnqueueActivityScheduleAsync(rePaused);

            await queue.NotifyForkJoinArrivalAsync(forkId, "b", null, failed: false, exception: null);

            using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var second = await queue.AwaitForActivityScheduleAsync("test-consumer", cts2.Token);

            Assert.NotNull(second);
            Assert.Equal(["b"], second!.TriggeredJoinArrivals!.Select(a => a.JoinId));
        }
        finally
        {
            await manager.StopAsync(CancellationToken.None);
        }
    }
}

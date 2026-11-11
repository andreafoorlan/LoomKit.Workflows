using LoomKit.Workflows.Contracts;
using LoomKit.Workflows.Extensions;
using LoomKit.Workflows.Middlewares;
using LoomKit.Workflows.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LoomKit.Workflows.Tests;

public class ActivityConsumerLifecycleTests
{
    [Fact]
    public async Task RetryMiddleware_WaitsForActivityRetryInterval_BeforeRetrying()
    {
        using var provider = TestServiceProviderFactory.Build(
            configureConsumer: c => c.UseActivityMiddleware(typeof(ActivityRetryMiddleware<,>)),
            configureQueue: q =>
            {
                // 2 attempts in total: the middleware retries while RetriesLeft > 1
                q.MaxActivityRetries = 2;
                q.ActivityRetryInterval = 400;
            });
        var manager = provider.GetRequiredService<IWorkflowManager>();

        await WorkflowManagerTestHarness.RunAsync(manager, async () =>
        {
            var context = new TestWorkflowContext();

            await manager.StartWorkflowAsync(TestServiceProviderFactory.QueueName, new FlakyActivity(), context);

            var retried = await Wait.UntilAsync(() => context.Attempts.Count >= 2, TimeSpan.FromSeconds(5));

            Assert.True(retried);

            var attempts = context.Attempts.ToArray();
            var gap = attempts[1] - attempts[0];

            // without the delay the retry is picked up within a few polling intervals (~10 ms)
            Assert.True(gap >= TimeSpan.FromMilliseconds(350), $"Retry came after only {gap.TotalMilliseconds:F0} ms, expected about 400 ms.");
        });
    }

    [Fact]
    public async Task ScopedConsumer_DisposesScopeAsynchronously()
    {
        var disposalLog = new DisposalLog();

        using var provider = TestServiceProviderFactory.Build(
            configureConsumer: c => c.UseScopedServiceProvider = true,
            configureServices: services =>
            {
                services.AddSingleton(disposalLog);
                services.AddScoped<AsyncOnlyDisposable>();
            });
        var manager = provider.GetRequiredService<IWorkflowManager>();

        await WorkflowManagerTestHarness.RunAsync(manager, async () =>
        {
            var context = new TestWorkflowContext();

            await manager.StartWorkflowAsync(TestServiceProviderFactory.QueueName, new ScopedDependencyActivity(), context);

            var handled = await Wait.UntilAsync(() => context.Trace.Count > 0, TimeSpan.FromSeconds(2));
            var disposed = await Wait.UntilAsync(() => disposalLog.AsyncDisposals == 1, TimeSpan.FromSeconds(2));

            Assert.True(handled);
            Assert.True(disposed, "The scoped service that only implements IAsyncDisposable was never disposed.");
        });
    }

    [Fact]
    public async Task StopAsync_WhileWaitingForActivities_DoesNotLogErrors()
    {
        var logs = new CapturingLoggerProvider();

        using var provider = TestServiceProviderFactory.Build(
            // long polling interval: the consumer is parked inside Task.Delay(interval, token) when it
            // is stopped, which is where the cancellation surfaces as an OperationCanceledException
            configureQueue: q => q.ActivityAwaitCheckInterval = 5000,
            configureServices: services => services.AddLogging(builder => builder.AddProvider(logs)));
        var manager = provider.GetRequiredService<IWorkflowManager>();

        await manager.StartAsync(CancellationToken.None);

        // give the consumer's background loop time to reach the wait
        await Task.Delay(200);

        await manager.StopAsync(CancellationToken.None);

        var errors = logs.Entries.Where(e => e.Level >= LogLevel.Error).ToList();

        Assert.True(errors.Count == 0, "Stopping the consumer logged errors: " + string.Join(" | ", errors.Select(e => e.Message)));
    }
}

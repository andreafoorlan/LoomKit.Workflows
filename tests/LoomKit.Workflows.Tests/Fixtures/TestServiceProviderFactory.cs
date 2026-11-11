using LoomKit.Workflows.Abstracts;
using LoomKit.Workflows.Contracts;
using LoomKit.Workflows.Defaults;
using Microsoft.Extensions.DependencyInjection;

namespace LoomKit.Workflows.Tests.Fixtures;

internal static class TestServiceProviderFactory
{
    public const string QueueName = "test-queue";
    public const string ConsumerName = "test-consumer";

    public static ServiceProvider Build(
        Action<ActivityConsumerOptionsBuilder>? configureConsumer = null,
        Action<ActivityQueueOptionsBuilder>? configureQueue = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();

        // every Defaults/* type (DefaultWorkflowManager, ActivityConsumer, InProcessActivityQueue,
        // middlewares) takes an ILogger<T> - without this, DI resolution fails as soon as
        // ActivatorUtilities tries to construct any of them
        services.AddLogging();

        services.AddScoped<IActivityHandler<PingActivity, TestWorkflowContext>, PingActivityHandler>();
        services.AddScoped<IActivityHandler<FirstActivity, TestWorkflowContext>, FirstActivityHandler>();
        services.AddScoped<IActivityHandler<SecondActivity, TestWorkflowContext>, SecondActivityHandler>();
        services.AddScoped<IActivityHandler<PausingActivity, TestWorkflowContext>, PausingActivityHandler>();
        services.AddScoped<IActivityHandler<AfterResumeActivity, TestWorkflowContext>, AfterResumeActivityHandler>();
        services.AddScoped<IActivityHandler<ForkingActivity, TestWorkflowContext>, ForkingActivityHandler>();
        services.AddScoped<IActivityHandler<BranchActivity, TestWorkflowContext>, BranchActivityHandler>();
        services.AddScoped<IActivityHandler<FailingBranchActivity, TestWorkflowContext>, FailingBranchActivityHandler>();
        services.AddScoped<IActivityHandler<ExplicitlyFailingBranchActivity, TestWorkflowContext>, ExplicitlyFailingBranchActivityHandler>();
        services.AddScoped<IActivityHandler<FailOutrightActivity, TestWorkflowContext>, FailOutrightActivityHandler>();
        services.AddScoped<IActivityHandler<CollectJoinPointActivity, TestWorkflowContext>, CollectJoinPointActivityHandler>();
        services.AddScoped<IActivityHandler<FlakyActivity, TestWorkflowContext>, FlakyActivityHandler>();
        services.AddScoped<IActivityHandler<ScopedDependencyActivity, TestWorkflowContext>, ScopedDependencyActivityHandler>();

        configureServices?.Invoke(services);

        services.AddDefaultWorkflowManager(builder => builder
            .UseQueue<InProcessActivityQueue>(QueueName, q =>
            {
                // short polling interval so tests don't have to wait out the 1000ms default
                q.ActivityAwaitCheckInterval = 10;
                configureQueue?.Invoke(q);
            })
            .UseConsumer<ActivityConsumer>(ConsumerName, QueueName, c => configureConsumer?.Invoke(c)));

        return services.BuildServiceProvider();
    }

    // No consumer configured - for tests that drive an IActivityQueue directly (e.g. exercising
    // AwaitForActivityScheduleAsync/NotifyForkJoinArrivalAsync by hand). With a consumer running,
    // its background loop would race the test for the same schedules via the queue's single
    // _awaitActivitySemaphore, starving the test's own AwaitForActivityScheduleAsync calls.
    public static ServiceProvider BuildQueueOnly(Action<ActivityQueueOptionsBuilder>? configureQueue = null)
    {
        var services = new ServiceCollection();

        services.AddLogging();

        services.AddDefaultWorkflowManager(builder => builder
            .UseQueue<InProcessActivityQueue>(QueueName, q =>
            {
                q.ActivityAwaitCheckInterval = 10;
                configureQueue?.Invoke(q);
            }));

        return services.BuildServiceProvider();
    }
}

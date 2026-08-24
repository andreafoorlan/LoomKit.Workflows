using LoomKit.Workflows.Contracts;
using LoomKit.Workflows.Defaults;
using LoomKit.Workflows.Extensions;
using LoomKit.Workflows.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace LoomKit.Workflows.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public async Task AddActivityHandlersFromAssemblies_RegistersHandlers_ResolvableEndToEnd()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddActivityHandlersFromAssemblies(ServiceLifetime.Scoped, typeof(PingActivityHandler).Assembly);

        services.AddDefaultWorkflowManager(builder => builder
            .UseQueue<InProcessActivityQueue>(TestServiceProviderFactory.QueueName, q => q.ActivityAwaitCheckInterval = 10)
            .UseConsumer<ActivityConsumer>(TestServiceProviderFactory.ConsumerName, TestServiceProviderFactory.QueueName, _ => { }));

        using var provider = services.BuildServiceProvider();
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
    public void AddActivityHandlersFromAssemblies_DoesNotDuplicate_WhenCalledTwiceOrGivenOverlappingAssemblies()
    {
        var services = new ServiceCollection();
        var assembly = typeof(PingActivityHandler).Assembly;

        services.AddActivityHandlersFromAssemblies(ServiceLifetime.Scoped, assembly, assembly);
        services.AddActivityHandlersFromAssemblies(ServiceLifetime.Scoped, assembly);

        var pingRegistrations = services.Count(d =>
            d.ServiceType == typeof(IActivityHandler<PingActivity, TestWorkflowContext>) &&
            d.ImplementationType == typeof(PingActivityHandler));

        Assert.Equal(1, pingRegistrations);
    }

    [Fact]
    public void AddActivityHandlersFromAssemblies_DoesNotRegister_OpenGenericHandlerClasses()
    {
        var services = new ServiceCollection();
        services.AddActivityHandlersFromAssemblies(ServiceLifetime.Scoped, typeof(PingActivityHandler).Assembly);

        // ActivityMiddleware<,> also implements IActivityHandler<,>, but concrete middleware
        // subclasses are typically open generics themselves (e.g. ActivityRetryMiddleware<,>) and
        // must never be picked up by the scan and registered as if they were plain handlers.
        Assert.DoesNotContain(services, d => d.ImplementationType == typeof(LoomKit.Workflows.Middlewares.ActivityRetryMiddleware<,>));
    }

    [Fact]
    public async Task AddWorkflowManager_Generic_ResolvesConfiguredDefaultManager()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IActivityHandler<PingActivity, TestWorkflowContext>, PingActivityHandler>();

        services.AddWorkflowManager<DefaultWorkflowManager, DefaultWorkflowManagerOptionsBuilder, DefaultWorkflowManagerOptions>(builder => builder
            .UseQueue<InProcessActivityQueue>(TestServiceProviderFactory.QueueName, q => q.ActivityAwaitCheckInterval = 10)
            .UseConsumer<ActivityConsumer>(TestServiceProviderFactory.ConsumerName, TestServiceProviderFactory.QueueName, _ => { }));

        using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<IWorkflowManager>();

        Assert.IsType<DefaultWorkflowManager>(manager);

        await WorkflowManagerTestHarness.RunAsync(manager, async () =>
        {
            var context = new TestWorkflowContext();
            await manager.StartWorkflowAsync(TestServiceProviderFactory.QueueName, new PingActivity(), context);

            var completed = await Wait.UntilAsync(() => context.Trace.Count > 0, TimeSpan.FromSeconds(2));
            Assert.True(completed);
        });
    }
}

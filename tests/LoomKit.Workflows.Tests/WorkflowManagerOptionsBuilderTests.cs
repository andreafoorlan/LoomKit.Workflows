using LoomKit.Workflows.Contracts;
using LoomKit.Workflows.Defaults;
using LoomKit.Workflows.Tests.Fixtures;

namespace LoomKit.Workflows.Tests;

public class WorkflowManagerOptionsBuilderTests
{
    private sealed class Seeder : IWorkflowManagerSeeder
    {
        public Task SeedActivities(IWorkflowManager workflowManager) => Task.CompletedTask;
    }

    [Fact]
    public void UseQueue_AddsQueueOptions_WithConfiguredValues()
    {
        var options = new DefaultWorkflowManagerOptionsBuilder()
            .UseQueue<InProcessActivityQueue>("orders", q =>
            {
                q.MaxActivityRetries = 3;
                q.ActivityAwaitCheckInterval = 500;
            })
            .Build();

        var queueOptions = Assert.Single(options.ActivityQueueOptions).Value;
        Assert.Equal("orders", queueOptions.ActivityQueueName);
        Assert.Equal(typeof(InProcessActivityQueue), queueOptions.ActivityQueueType);
        Assert.Equal(3, queueOptions.MaxActivityRetries);
        Assert.Equal(500, queueOptions.ActivityAwaitCheckInterval);
    }

    [Fact]
    public void UseQueue_DuplicateName_Throws()
    {
        var builder = new DefaultWorkflowManagerOptionsBuilder()
            .UseQueue<InProcessActivityQueue>("orders", _ => { });

        Assert.Throws<InvalidOperationException>(() =>
            builder.UseQueue<InProcessActivityQueue>("orders", _ => { }));
    }

    [Fact]
    public void UseConsumer_AddsConsumerOptions_WithConfiguredValues()
    {
        var options = new DefaultWorkflowManagerOptionsBuilder()
            .UseQueue<InProcessActivityQueue>("orders", _ => { })
            .UseConsumer<ActivityConsumer>("orders-consumer", "orders", c => c.UseScopedServiceProvider = true)
            .Build();

        var consumerOptions = Assert.Single(options.ActivityConsumerOptions).Value;
        Assert.Equal("orders-consumer", consumerOptions.ActivityConsumerName);
        Assert.Equal(typeof(ActivityConsumer), consumerOptions.ActivityConsumerType);
        Assert.Equal("orders", consumerOptions.ActivityQueueName);
        Assert.True(consumerOptions.UseScopedServiceProvider);
    }

    [Fact]
    public void UseConsumer_DuplicateName_Throws()
    {
        var builder = new DefaultWorkflowManagerOptionsBuilder()
            .UseQueue<InProcessActivityQueue>("orders", _ => { })
            .UseConsumer<ActivityConsumer>("orders-consumer", "orders", _ => { });

        Assert.Throws<InvalidOperationException>(() =>
            builder.UseConsumer<ActivityConsumer>("orders-consumer", "orders", _ => { }));
    }

    [Fact]
    public void UseWorkflowManagerSeeder_AddsSeederType()
    {
        var options = new DefaultWorkflowManagerOptionsBuilder()
            .UseWorkflowManagerSeeder<Seeder>()
            .Build();

        Assert.Equal([typeof(Seeder)], options.WorkflowManagerSeeders);
    }
}

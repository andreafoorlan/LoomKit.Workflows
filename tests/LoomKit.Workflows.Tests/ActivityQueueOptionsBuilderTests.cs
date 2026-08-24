using LoomKit.Workflows.Abstracts;
using LoomKit.Workflows.Defaults;

namespace LoomKit.Workflows.Tests;

public class ActivityQueueOptionsBuilderTests
{
    [Fact]
    public void Build_UsesDefaults_WhenNotOverridden()
    {
        var options = new ActivityQueueOptionsBuilder
        {
            ActivityQueueName = "orders",
            ActivityQueueType = typeof(InProcessActivityQueue)
        }.Build();

        Assert.Equal("orders", options.ActivityQueueName);
        Assert.Equal(typeof(InProcessActivityQueue), options.ActivityQueueType);
        Assert.Equal(5, options.MaxActivityRetries);
        Assert.Equal(1000, options.ActivityAwaitCheckInterval);
        Assert.Equal(5000, options.ActivityRetryInterval);
    }

    [Fact]
    public void Build_HonorsOverriddenValues()
    {
        var options = new ActivityQueueOptionsBuilder
        {
            ActivityQueueName = "orders",
            ActivityQueueType = typeof(InProcessActivityQueue),
            MaxActivityRetries = 2,
            ActivityAwaitCheckInterval = 20,
            ActivityRetryInterval = 100
        }.Build();

        Assert.Equal(2, options.MaxActivityRetries);
        Assert.Equal(20, options.ActivityAwaitCheckInterval);
        Assert.Equal(100, options.ActivityRetryInterval);
    }
}

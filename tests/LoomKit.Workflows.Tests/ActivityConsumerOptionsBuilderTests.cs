using LoomKit.Workflows.Abstracts;
using LoomKit.Workflows.Contracts;
using LoomKit.Workflows.Defaults;
using LoomKit.Workflows.Middlewares;
using LoomKit.Workflows.Tests.Fixtures;

namespace LoomKit.Workflows.Tests;

public class ActivityConsumerOptionsBuilderTests
{
    private static ActivityConsumerOptionsBuilder NewBuilder() => new()
    {
        ActivityConsumerName = "consumer",
        ActivityConsumerType = typeof(ActivityConsumer),
        ActivityQueueName = "queue"
    };

    [Fact]
    public void UseActivityMiddleware_AddsMiddlewareType_InRegistrationOrder()
    {
        var options = NewBuilder()
            .UseActivityMiddleware(typeof(ActivityRetryMiddleware<,>))
            .Build();

        Assert.Equal([typeof(ActivityRetryMiddleware<,>)], options.ActivityMiddlewares);
    }

    [Fact]
    public void UseActivityMiddleware_NonOpenGenericType_Throws()
    {
        var builder = NewBuilder();

        Assert.Throws<ArgumentException>(() =>
            builder.UseActivityMiddleware(typeof(ActivityRetryMiddleware<PingActivity, TestWorkflowContext>)));
    }

    [Fact]
    public void UseActivityMiddleware_TypeNotDerivedFromActivityMiddleware_Throws()
    {
        var builder = NewBuilder();

        Assert.Throws<ArgumentException>(() =>
            builder.UseActivityMiddleware(typeof(List<>)));
    }

    [Fact]
    public void ClearActivityMiddlewares_RemovesPreviouslyAddedMiddlewares()
    {
        var options = NewBuilder()
            .UseActivityMiddleware(typeof(ActivityRetryMiddleware<,>))
            .ClearActivityMiddlewares()
            .Build();

        Assert.Empty(options.ActivityMiddlewares);
    }
}

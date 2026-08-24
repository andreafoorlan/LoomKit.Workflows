using System.Collections.Immutable;

namespace LoomKit.Workflows.Abstracts;

public class ActivityConsumerOptionsBuilder
{
    private readonly LinkedList<Type> _activityMiddlewares;

    public required string ActivityConsumerName { get; init; }
    public required Type ActivityConsumerType { get; init; }
    public required string ActivityQueueName { get; init; }
    public bool UseScopedServiceProvider { get; set; }

    public ActivityConsumerOptionsBuilder()
    {
        _activityMiddlewares = [];
    }

    public ActivityConsumerOptionsBuilder ClearActivityMiddlewares()
    {
        _activityMiddlewares.Clear();
        return this;
    }

    public ActivityConsumerOptionsBuilder UseActivityMiddleware(Type activityMiddlewareType)
    {
        if (activityMiddlewareType is null)
            throw new ArgumentNullException(nameof(activityMiddlewareType));

        if (!activityMiddlewareType.IsGenericTypeDefinition)
            throw new ArgumentException("Middleware type must be an open generic type", nameof(activityMiddlewareType));

        if (!DerivesFromOpenGeneric(activityMiddlewareType, typeof(ActivityMiddleware<,>)))
            throw new ArgumentException("Middleware type must derive from ActivityMiddleware<,>", nameof(activityMiddlewareType));

        _activityMiddlewares.AddLast(activityMiddlewareType);

        return this;
    }

    public ActivityConsumerOptions Build()
    {
        return new ActivityConsumerOptions()
        {
            ActivityConsumerName = ActivityConsumerName,
            ActivityConsumerType = ActivityConsumerType,
            ActivityQueueName = ActivityQueueName,
            UseScopedServiceProvider = UseScopedServiceProvider,
            ActivityMiddlewares = _activityMiddlewares.ToImmutableList(),
        };
    }

    // Walks the base-type chain (not GetInterfaces(), since ActivityMiddleware<,> is an abstract
    // class, not an interface) looking for a base type closing the given open generic definition.
    private static bool DerivesFromOpenGeneric(Type type, Type openGenericBaseType)
    {
        for (var currentBaseType = type.BaseType; currentBaseType is not null; currentBaseType = currentBaseType.BaseType)
        {
            if (currentBaseType.IsGenericType && currentBaseType.GetGenericTypeDefinition() == openGenericBaseType)
                return true;
        }

        return false;
    }
}

using System.Collections.Concurrent;
using LoomKit.Workflows.Contracts;

namespace LoomKit.Workflows.Internal;

// Shared reflection helper for resolving an activity's concrete TWorkflowContext from the single
// closed IActivity<TWorkflowContext> it implements. Used by the consumer's dispatch pipeline, which
// only knows the activity as `IActivity` at that point. Resolved once per activity type and cached -
// not recomputed per dispatch. Mirrors JobStatusTypeResolver in LoomKit.Jobs.
internal static class WorkflowContextTypeResolver
{
    private static readonly ConcurrentDictionary<Type, Type> _workflowContextTypeCache = new();

    public static Type ResolveWorkflowContextType(Type activityType)
    {
        return _workflowContextTypeCache.GetOrAdd(activityType, static type =>
        {
            var activityInterface = type
                .GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IActivity<>));

            if (activityInterface is null)
                throw new InvalidOperationException($"Type {type.FullName} does not implement IActivity<TWorkflowContext>.");

            return activityInterface.GetGenericArguments()[0];
        });
    }
}

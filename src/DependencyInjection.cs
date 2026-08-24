using System.Reflection;
using LoomKit.Workflows.Abstracts;
using LoomKit.Workflows.Contracts;
using LoomKit.Workflows.Defaults;
using Microsoft.Extensions.DependencyInjection;

namespace LoomKit.Workflows;

public static class DependencyInjection
{
    private static readonly Type _activityHandlerOpenGenericType = typeof(IActivityHandler<,>);

    public static IServiceCollection AddWorkflowManager<TWorkflowManager, TWorkflowManagerOptionsBuilder, TWorkflowManagerOptions>(this IServiceCollection services, Action<TWorkflowManagerOptionsBuilder> optionsBuilder)
        where TWorkflowManager : WorkflowManager<TWorkflowManagerOptions>
        where TWorkflowManagerOptionsBuilder : WorkflowManagerOptionsBuilder<TWorkflowManagerOptions>, new()
        where TWorkflowManagerOptions : WorkflowManagerOptions, new()
    {
        // create options builder
        var workflowManagerOptionsBuilder = new TWorkflowManagerOptionsBuilder();

        // invoke build action
        optionsBuilder.Invoke(workflowManagerOptionsBuilder);

        // build manager options from builder
        var workflowManagerOptions = workflowManagerOptionsBuilder.Build();

        // add manager to DI - always a singleton (not configurable, unlike RequestSender/
        // NotificationDispatcher's ServiceLifetime): the host only manages hosted services as
        // singletons, and IWorkflowManager is an IHostedService
        services.AddSingleton<IWorkflowManager>(serviceProvider =>
            (IWorkflowManager)ActivatorUtilities.CreateInstance(serviceProvider, typeof(TWorkflowManager), workflowManagerOptions));

        // manager as hosted service
        services.AddHostedService<IWorkflowManager>(sp => sp.GetRequiredService<IWorkflowManager>());

        return services;
    }

    public static IServiceCollection AddDefaultWorkflowManager(this IServiceCollection services, Action<DefaultWorkflowManagerOptionsBuilder> optionsBuilder)
    {
        return AddWorkflowManager<DefaultWorkflowManager, DefaultWorkflowManagerOptionsBuilder, DefaultWorkflowManagerOptions>(services, optionsBuilder);
    }

    // Scans the given assemblies for concrete, closed IActivityHandler<,> implementations and
    // registers each one against the interface it implements. Open-generic handlers (rare) are not
    // discovered - register those manually with services.AddScoped(typeof(IActivityHandler<,>), typeof(MyHandler<,>)).
    public static IServiceCollection AddActivityHandlersFromAssemblies(this IServiceCollection services, ServiceLifetime lifetime, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);

        foreach (var assembly in assemblies)
            ArgumentNullException.ThrowIfNull(assembly, nameof(assemblies));

        // track (interface, implementation) pairs already registered so overlapping assemblies,
        // or calling this method more than once against the same IServiceCollection, don't produce
        // duplicate registrations - seeded from what's already in `services` (including registrations
        // from an earlier call to this method, or a handler registered manually beforehand)
        var registered = new HashSet<(Type HandlerInterface, Type ImplementationType)>(
            services
                .Where(d => d.ImplementationType is not null)
                .Select(d => (d.ServiceType, d.ImplementationType!)));

        foreach (var assembly in assemblies)
        {
            foreach (var type in GetLoadableTypes(assembly))
            {
                // only concrete, closed classes can be instantiated as-is; open-generic handlers are out of scope here
                if (!type.IsClass || type.IsAbstract || type.IsGenericTypeDefinition)
                    continue;

                foreach (var handlerInterface in GetClosedActivityHandlerInterfaces(type))
                {
                    if (registered.Add((handlerInterface, type)))
                    {
                        services.Add(ServiceDescriptor.Describe(handlerInterface, type, lifetime));
                    }
                }
            }
        }

        return services;
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // some types in the assembly could not be loaded (e.g. missing dependency) - use the ones that could
            return ex.Types.Where(t => t is not null)!;
        }
    }

    private static IEnumerable<Type> GetClosedActivityHandlerInterfaces(Type type)
    {
        return type
            .GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == _activityHandlerOpenGenericType);
    }
}

#nullable enable
using System;
using System.Linq;
using System.Reflection;
using Dbones.Pipes.Dispatch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dbones.Pipes;

/// <summary>Registers dispatcher services and discovers request handlers using reflection.</summary>
public static class DispatchExtensions
{
    /// <summary>Registers public, concrete, closed handlers as scoped services.</summary>
    /// <param name="services">The collection to add handler registrations to.</param>
    /// <param name="assemblies">Assemblies containing exported handler implementations.</param>
    /// <returns>The service collection for further configuration.</returns>
    /// <remarks>Abstract and open generic types are excluded. All implemented interfaces of each
    /// handler are registered. Repeated scans do not duplicate an interface/implementation pair.</remarks>
    /// <exception cref="ArgumentNullException">The collection or assembly array is null.</exception>
    public static IServiceCollection AddHandlersFrom(this IServiceCollection services, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);
        foreach (var type in assemblies.Distinct().SelectMany(a => a.ExportedTypes)
                     .Where(t => t.IsClass && !t.IsAbstract && !t.ContainsGenericParameters))
        {
            var interfaces = type.GetInterfaces();
            if (!interfaces.Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IHandler<,>)))
                continue;
            // Preserve AsImplementedInterfaces behavior, including additional interfaces.
            foreach (var contract in interfaces)
                services.TryAddEnumerable(ServiceDescriptor.Scoped(contract, type));
        }

        return services;
    }

    /// <summary>Registers the scoped dispatcher, scoped actions and singleton pipeline definitions.</summary>
    /// <param name="services">The collection to add dispatcher registrations to.</param>
    /// <param name="configure">Configures open generic invocation actions in execution order.</param>
    /// <returns>The service collection for further configuration.</returns>
    /// <remarks>Handlers and called services must be registered separately. Invocations use the
    /// caller's scope; singleton pipelines retain action types rather than scoped instances.</remarks>
    public static IServiceCollection AddDispatcher(this IServiceCollection services, Action<PipelineOptions> configure)
    {
        var options = new PipelineOptions();
        configure(options);
        services.AddSingleton(options);
        services.AddSingleton(typeof(InvocationPipeline<,>));
        services.AddScoped(typeof(InvokeAction<,>));
        services.AddScoped<IDispatcher, Dispatcher>();
        foreach (var action in options.Actions) services.AddScoped(action);
        return services;
    }
}

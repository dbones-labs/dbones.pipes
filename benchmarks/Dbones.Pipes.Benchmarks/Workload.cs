using Dbones.Pipes;
using Dbones.Pipes.Dispatch;
using Benchmarks.SpaceAdapter;
using DispatchR.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Dbones.Pipes.Benchmarks;

/// <summary>Identifies a dispatcher implementation to benchmark.</summary>
public enum Library
{
    /// <summary>The Dbones.Pipes dispatcher.</summary>
    Pipes,
    /// <summary>The MediatR mediator.</summary>
    MediatR,
    /// <summary>The DispatchR mediator.</summary>
    DispatchR,
    /// <summary>The source-generated Space mediator.</summary>
    Space
}

/// <summary>Provides the same integer-result request to the interface-based dispatchers.</summary>
public sealed class Ping : Dbones.Pipes.Dispatch.IRequest<int>, MediatR.IRequest<int>,
    DispatchR.Abstractions.Send.IRequest<Ping, Task<int>> { }

/// <summary>Counts a request execution and returns the common benchmark result.</summary>
public sealed class Handler(Probe probe) : Dbones.Pipes.Dispatch.IHandler<Ping, int>,
    MediatR.IRequestHandler<Ping, int>, DispatchR.Abstractions.Send.IRequestHandler<Ping, Task<int>>
{
    /// <summary>Processes one request and returns its result.</summary>
    public Task<int> Handle(Ping request, CancellationToken ct)
    {
        probe.Handlers++;
        return Task.FromResult(42);
    }
}
/// <summary>Counts and forwards a Pipes invocation.</summary>
public sealed class PipesStage<TInput, TResult>(Probe probe) : IAction<Invocation<TInput, TResult>>
{
    /// <summary>Counts this action execution and awaits the next stage.</summary>
    public async Task Execute(Invocation<TInput, TResult> inv, Next<Invocation<TInput, TResult>> next)
    {
        probe.Stages++;
        await next(inv);
    }
}
/// <summary>Counts and forwards a MediatR invocation.</summary>
public sealed class MediatRStage(Probe probe) : MediatR.IPipelineBehavior<Ping, int>
{
    /// <summary>Processes one request and returns its result.</summary>
    public async Task<int> Handle(Ping request, MediatR.RequestHandlerDelegate<int> next, CancellationToken ct)
    {
        probe.Stages++;
        return await next(ct);
    }
}
/// <summary>Counts and forwards a DispatchR invocation.</summary>
public sealed class DispatchRStage(Probe probe) : DispatchR.Abstractions.Send.IPipelineBehavior<Ping, Task<int>>
{
    /// <summary>Gets or sets the next handler or behavior in the DispatchR chain.</summary>
    public required DispatchR.Abstractions.Send.IRequestHandler<Ping, Task<int>> NextPipeline { get; set; }
    /// <summary>Processes one request and returns its result.</summary>
    public async Task<int> Handle(Ping request, CancellationToken ct)
    {
        probe.Stages++;
        return await NextPipeline.Handle(request, ct);
    }
}

// Registration/build and scope creation are outside dispatch timing for every library.
/// <summary>Owns the container and caller scope for one dispatcher comparison.</summary>
public sealed class Workload : IDisposable
{
    readonly ServiceProvider provider;
    readonly IServiceScope scope;
    readonly Library library;
    readonly int depth;
    readonly Ping ping = new();
    readonly SpaceWorkload? space;
    /// <summary>Gets the counters used to verify handler and middleware execution.</summary>
    public Probe Probe => space?.Probe ?? scope.ServiceProvider.GetRequiredService<Probe>();

    /// <summary>Creates the resources for this Workload instance.</summary>
    public Workload(Library library, int depth)
    {
        this.library = library;
        this.depth = depth;
        var services = new ServiceCollection();
        services.AddSingleton<Probe>();
        switch (library)
        {
            case Library.Pipes:
                services.AddHandlersFrom(typeof(Ping).Assembly);
                services.AddDispatcher(o => { if (depth == 1) o.Actions.Add(typeof(PipesStage<,>)); });
                break;
            case Library.MediatR:
                services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
                services.AddMediatR(o =>
                {
                    o.RegisterServicesFromAssemblyContaining<Ping>();
                    o.Lifetime = ServiceLifetime.Scoped;
                    if (depth == 1) o.AddBehavior<MediatRStage>(ServiceLifetime.Scoped);
                });
                break;
            case Library.DispatchR:
                services.AddDispatchR(typeof(Ping).Assembly, withPipelines: depth == 1, withNotifications: false);
                break;
            case Library.Space:
                space = new SpaceWorkload(depth);
                break;
        }
        provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        scope = provider.CreateScope();
    }

    // Includes resolving the dispatcher from the existing caller scope.
    /// <summary>Dispatches one request and returns its result.</summary>
    public async Task<int> Send() => library switch
    {
        Library.Pipes => await scope.ServiceProvider.GetRequiredService<IDispatcher>().Send(ping),
        Library.MediatR => await scope.ServiceProvider.GetRequiredService<MediatR.IMediator>().Send(ping),
        Library.DispatchR => await scope.ServiceProvider.GetRequiredService<DispatchR.IMediator>().Send(ping, default),
        Library.Space => await space!.Send(),
        _ => throw new ArgumentOutOfRangeException()
    };

    /// <summary>Disposes the owned caller scope and service provider.</summary>
    public void Dispose() { space?.Dispose(); scope.Dispose(); provider.Dispose(); }
}

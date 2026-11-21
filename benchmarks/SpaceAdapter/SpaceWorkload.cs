using Microsoft.Extensions.DependencyInjection;
using Space.Abstraction;
using Space.Abstraction.Attributes;
using Space.Abstraction.Context;
using Space.Abstraction.Contracts;
using Space.DependencyInjection;

namespace Benchmarks.SpaceAdapter;
/// <summary>Counts middleware and handler executions for workload verification.</summary>
public sealed class Probe
{
    /// <summary>Counts middleware stage executions.</summary>
    public int Stages;
    /// <summary>Counts handler executions.</summary>
    public int Handlers;
}
/// <summary>Requests the Space benchmark result without middleware.</summary>
public sealed record SpacePing : Space.Abstraction.Contracts.IRequest<int>;
/// <summary>Requests the Space benchmark result through one middleware stage.</summary>
public sealed record SpacePipelinePing : Space.Abstraction.Contracts.IRequest<int>;
/// <summary>Implements the generated Space benchmark handlers and middleware.</summary>
public sealed class SpaceHandler(Probe probe)
{
    /// <summary>Processes one request and returns its result.</summary>
    [Handle]
    public ValueTask<int> Handle(HandlerContext<SpacePing> ctx)
    {
        probe.Handlers++;
        return ValueTask.FromResult(42);
    }
    /// <summary>Counts a Space handler execution and returns the benchmark result.</summary>
    [Handle]
    public ValueTask<int> HandlePipeline(HandlerContext<SpacePipelinePing> ctx)
    {
        probe.Handlers++;
        return ValueTask.FromResult(42);
    }
    /// <summary>Counts a middleware execution and awaits the next stage.</summary>
    [Pipeline(Order = 1)]
    public async ValueTask<int> Pipeline(PipelineContext<SpacePipelinePing> ctx,
        PipelineDelegate<SpacePipelinePing, int> next)
    {
        probe.Stages++;
        return await next(ctx);
    }
}


/// <summary>Owns the generated Space registrations and dispatch scope.</summary>
public sealed class SpaceWorkload : IDisposable
{
    readonly ServiceProvider provider;
    readonly IServiceScope scope;
    readonly int depth;
    readonly SpacePing ping = new();
    readonly SpacePipelinePing pipelinePing = new();
    /// <summary>Gets the counters used to verify handler and middleware execution.</summary>
    public Probe Probe => scope.ServiceProvider.GetRequiredService<Probe>();
    /// <summary>Creates the resources for this SpaceWorkload instance.</summary>
    public SpaceWorkload(int depth)
    {
        this.depth = depth;
        var services = new ServiceCollection();
        services.AddSingleton<Probe>();
        services.AddSpaceSourceGenerated();
        provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        scope = provider.CreateScope();
    }
    /// <summary>Dispatches one request and returns its result.</summary>
    public ValueTask<int> Send() => depth == 0
        ? scope.ServiceProvider.GetRequiredService<ISpace>().Send<SpacePing, int>(ping)
        : scope.ServiceProvider.GetRequiredService<ISpace>().Send<SpacePipelinePing, int>(pipelinePing);
    /// <summary>Disposes the owned caller scope and service provider.</summary>
    public void Dispose() { scope.Dispose(); provider.Dispose(); }
}

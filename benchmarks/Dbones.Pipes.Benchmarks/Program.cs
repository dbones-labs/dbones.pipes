using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Dbones.Pipes;
using Microsoft.Extensions.DependencyInjection;

namespace Dbones.Pipes.Benchmarks;

/// <summary>Runs benchmark workloads or verifies their execution counts.</summary>
public static class Program
{
    /// <summary>Runs selected benchmarks, or verifies handler results and middleware counts with --verify.</summary>
    public static async Task Main(string[] args)
    {
        if (args.Contains("--verify"))
        {
            foreach (var library in Enum.GetValues<Library>())
            foreach (var depth in new[] { 0, 1 })
            {
                using var workload = new Workload(library, depth);
                for (var i = 1; i <= 3; i++)
                {
                    if (await workload.Send() != 42 || workload.Probe.Handlers != i || workload.Probe.Stages != i * depth)
                        throw new InvalidOperationException($"Incorrect workload: {library}, depth {depth}, call {i}, handlers {workload.Probe.Handlers}, stages {workload.Probe.Stages}");
                }
                Console.WriteLine($"Verified {library}, depth {depth}");
            }
            return;
        }
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}

/// <summary>Shares dispatch parameters, execution and cleanup across benchmark suites.</summary>
public abstract class DispatchBenchmark
{
    /// <summary>Gets or sets the implementation measured by this benchmark case.</summary>
    [ParamsAllValues]
    public Library Library { get; set; }
    /// <summary>Gets or sets the number of middleware stages in this benchmark case.</summary>
    [Params(0, 1)]
    public int MiddlewareDepth { get; set; }
    /// <summary>The workload owned by the current benchmark case.</summary>
    protected Workload Workload = null!;
    /// <summary>Disposes the resources created for this benchmark case.</summary>
    [GlobalCleanup]
    public void Cleanup() => Workload.Dispose();
    /// <summary>Dispatches one request and returns its result.</summary>
    [Benchmark]
    public Task<int> Send() => Workload.Send();
}

/// <summary>Measures warmed dispatch throughput and managed allocations.</summary>
[MemoryDiagnoser]
public class ThroughputBenchmarks : DispatchBenchmark
{
    /// <summary>Creates the workload outside measurement; warmed suites also execute an initial invocation.</summary>
    [GlobalSetup]
    public async Task Setup()
    {
        Workload = new Workload(Library, MiddlewareDepth);
        await Workload.Send();
    }
}

/// <summary>Configures ten independent first-call samples without warm-up.</summary>
public sealed class FirstCallConfig : ManualConfig
{
    /// <summary>Configures a single invocation in each of ten fresh processes.</summary>
    public FirstCallConfig() => AddJob(Job.Default
        .WithId("FirstCall")
        .WithStrategy(RunStrategy.ColdStart)
        .WithLaunchCount(10)
        .WithWarmupCount(0)
        .WithIterationCount(1)
        .WithInvocationCount(1)
        .WithUnrollFactor(1));
}

/// <summary>Measures one cold dispatch per process after container setup.</summary>
[Config(typeof(FirstCallConfig))]
public class FirstCallBenchmarks : DispatchBenchmark
{
    /// <summary>Creates the container and caller scope without sending a request.</summary>
    [GlobalSetup]
    public void Setup() => Workload = new Workload(Library, MiddlewareDepth);
}

/// <summary>Measures raw middleware execution at different action-chain depths.</summary>
[MemoryDiagnoser]
public class MiddlewareBenchmarks
{
    /// <summary>Gets or sets the number of middleware stages in this benchmark case.</summary>
    [Params(0, 1, 5, 10)]
    public int MiddlewareDepth { get; set; }
    ServiceProvider provider = null!;
    IServiceScope scope = null!;
    Middleware<MiddlewareContext> middleware = null!;
    readonly MiddlewareContext context = new();

    /// <summary>Creates the workload outside measurement; warmed suites also execute an initial invocation.</summary>
    [GlobalSetup]
    public async Task Setup()
    {
        var services = new ServiceCollection();
        services.AddScoped<MiddlewareStage>();
        provider = services.BuildServiceProvider();
        scope = provider.CreateScope();
        middleware = new Middleware<MiddlewareContext>();
        for (var i = 0; i < MiddlewareDepth; i++) middleware.Add<MiddlewareStage>();
        await Execute();
    }
    /// <summary>Executes the configured middleware chain in the existing caller scope.</summary>
    [Benchmark]
    public Task Execute() => middleware.Execute(scope.ServiceProvider, context);
    /// <summary>Disposes the resources created for this benchmark case.</summary>
    [GlobalCleanup]
    public void Cleanup() { scope.Dispose(); provider.Dispose(); }
    /// <summary>Tracks action executions in the raw middleware benchmark.</summary>
    public sealed class MiddlewareContext
    {
        /// <summary>Counts middleware stage executions.</summary>
        public int Stages;
    }
    /// <summary>Counts an execution and awaits the next middleware stage.</summary>
    public sealed class MiddlewareStage : IAction<MiddlewareContext>
    {
        /// <summary>Counts this action execution and awaits the next stage.</summary>
        public async Task Execute(MiddlewareContext ctx, Next<MiddlewareContext> next)
        {
            ctx.Stages++;
            await next(ctx);
        }
    }
}

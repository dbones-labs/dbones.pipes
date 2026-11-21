# Dispatcher and middleware benchmarks

Run from the repository root with the .NET 8 SDK or newer. These projects are
separate from `src/Pipes.sln` so the solution build and published package
do not acquire benchmark dependencies.

```sh
# Verify actual handler results and middleware counts, without timing.
dotnet run -c Release --project benchmarks/Dbones.Pipes.Benchmarks -- --verify

# First dispatch: ten independent process launches per library/depth.
dotnet run -c Release --project benchmarks/Dbones.Pipes.Benchmarks -- --filter '*FirstCallBenchmarks*'

# Warm dispatch throughput and managed allocations.
dotnet run -c Release --project benchmarks/Dbones.Pipes.Benchmarks -- --filter '*ThroughputBenchmarks*'

# Raw Pipes middleware overhead at 0, 1, 5 and 10 stages.
dotnet run -c Release --project benchmarks/Dbones.Pipes.Benchmarks -- --filter '*MiddlewareBenchmarks*'

# Fast infrastructure check; Dry results are not performance conclusions.
dotnet run -c Release --project benchmarks/Dbones.Pipes.Benchmarks -- --filter '*ThroughputBenchmarks*' '*MiddlewareBenchmarks*' --job Dry
```

BenchmarkDotNet writes reports to `BenchmarkDotNet.Artifacts/results` (Markdown,
CSV and HTML), including environment details, timing and allocation statistics.
Run on an otherwise idle machine, outside a debugger, for meaningful results.

## Measurement boundaries

`FirstCallBenchmarks` uses ColdStart, no warm-up, one invocation in one iteration,
and ten independent launches. Global setup registers services, builds the provider,
and creates a caller scope, but does not resolve the dispatcher or send a request.
The measurement includes initial dispatcher resolution, first pipeline/handler
resolution, runtime caches and JIT work encountered during that first send.
It excludes process startup, service registration, container build and scope creation.
A fresh container inside a warm benchmark would not reset static caches; separate
processes are essential here. First-call samples are noisy; no allocation diagnoser
is attached to this suite to keep its configuration simple.

`ThroughputBenchmarks` explicitly sends once in setup and then uses normal
BenchmarkDotNet warm-up and measurement. Each library reuses the caller scope.
This represents multiple dispatches from one caller scope, not one new HTTP request
scope per operation. Space creates an internal dispatch scope; its native scope
behavior is included in its timings. Counters are singleton in every library so
verification observes executions across those internal scopes. Resolution of the
dispatcher is included in every operation.

Every library returns 42, increments a handler counter and, at depth one,
increments a middleware counter before awaiting the next stage. Request
instances are reused. A common async Task wrapper consumes every result, including
Space's native ValueTask result; native return-type differences still affect costs.
Pipes, MediatR and DispatchR use Task-returning handlers. Space uses its documented
ValueTask-based API. There is no I/O, logging, validation or artificial delay.
The verification command checks three successive sends per library/depth.

`MiddlewareBenchmarks` isolates `Middleware<T>` without dispatcher/handler overhead.
It resolves a scoped stage for every registered pipeline entry, reusing the same
scoped stage instance at each depth. It excludes child-scope creation/disposal.

## Dependencies

Pinned comparison versions: MediatR 15.0.0, DispatchR.Mediator 2.3.1,
Space.Abstraction/Space.DependencyInjection 2.3.1, BenchmarkDotNet 0.15.8.
Benchmark dependencies are confined to these projects. MediatR uses a null logger
factory so its initialization does not write logs during measurement. No license
key is supplied; its first-resolution initialization remains part of first-call timing.

Space is isolated in an adapter because its generator diagnoses unrelated interfaces
named `IHandler`. Only the adapter runs the Space analyzer; the executable references
the runtime packages with the analyzer excluded by an explicit MSBuild target. The adapter generates the root
Space registrations at compile time. The adapter calls its generated registration
method directly so BenchmarkDotNet's generated entry assembly cannot substitute
an empty registration root.

Upstream API references:
- https://benchmarkdotnet.org/articles/guides/choosing-run-strategy.html
- https://github.com/jbogard/MediatR
- https://github.com/hasanxdev/DispatchR
- https://github.com/salihcantekin/Space

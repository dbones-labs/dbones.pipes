#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Dbones.Pipes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dispatch;

public interface IRequest<TResult> { }

public interface IHandler<in TRequest, TResult> where TRequest : IRequest<TResult>
{
    Task<TResult> Handle(TRequest request, CancellationToken ct);
}

public sealed class Invocation<TInput, TResult>
{
    public Invocation(string kind, string target, string operation, TInput input,
        Func<IServiceProvider, TInput, CancellationToken, Task<TResult>> run, CancellationToken ct)
    {
        Kind = kind;
        Target = target;
        Operation = operation;
        Input = input;
        Run = run;
        Ct = ct;
    }

    public string Kind { get; }
    public string Target { get; }
    public string Operation { get; }
    public TInput Input { get; }
    public CancellationToken Ct { get; }
    public TResult? Result { get; set; }
    internal Func<IServiceProvider, TInput, CancellationToken, Task<TResult>> Run { get; }
}

public readonly record struct Unit;

public interface IDispatcher
{
    Task<TResult> Send<TResult>(IRequest<TResult> request, CancellationToken ct = default);
    Task<TResult> ServiceCall<TService, TInput, TResult>(TInput input, Func<TService, TInput, Task<TResult>> call,
        CancellationToken ct = default, [CallerArgumentExpression(nameof(call))] string? operation = null) where TService : notnull;
    Task<TResult> ServiceCall<TService, TInput, TResult>(TInput input, Func<TService, Func<TInput, Task<TResult>>> select,
        CancellationToken ct = default, [CallerArgumentExpression(nameof(select))] string? operation = null) where TService : notnull;
    Task ServiceCall<TService, TInput>(TInput input, Func<TService, TInput, Task> call,
        CancellationToken ct = default, [CallerArgumentExpression(nameof(call))] string? operation = null) where TService : notnull;
}

public sealed class InvokeAction<TInput, TResult> : IAction<Invocation<TInput, TResult>>
{
    private readonly IServiceProvider _scope;
    public InvokeAction(IServiceProvider scope) => _scope = scope;
    public async Task Execute(Invocation<TInput, TResult> inv, Next<Invocation<TInput, TResult>> next)
        => inv.Result = await inv.Run(_scope, inv.Input, inv.Ct);
}

public sealed class PipelineOptions
{
    public List<Type> Actions { get; } = new();
}

public sealed class InvocationPipeline<TInput, TResult>
{
    private readonly Middleware<Invocation<TInput, TResult>> _middleware = new();
    public InvocationPipeline(PipelineOptions options)
    {
        foreach (var open in options.Actions)
            _middleware.Add(open.MakeGenericType(typeof(TInput), typeof(TResult)));
        _middleware.Add<InvokeAction<TInput, TResult>>();
    }
    public async Task<TResult> Execute(IServiceProvider scope, Invocation<TInput, TResult> inv)
    {
        await _middleware.Execute(scope, inv);
        return inv.Result!;
    }
}

public sealed class Dispatcher : IDispatcher
{
    private readonly IServiceProvider _scope;
    // A request can implement more than one IRequest<TResult> interface.
    private static readonly ConcurrentDictionary<(Type Request, Type Result), object> Runners = new();
    public Dispatcher(IServiceProvider scope) => _scope = scope;
    private Task<TResult> Go<TInput, TResult>(Invocation<TInput, TResult> inv)
        => _scope.GetRequiredService<InvocationPipeline<TInput, TResult>>().Execute(_scope, inv);

    public Task<TResult> Send<TResult>(IRequest<TResult> request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var runner = (Runner<TResult>)Runners.GetOrAdd((request.GetType(), typeof(TResult)), key =>
            Activator.CreateInstance(typeof(Runner<,>).MakeGenericType(key.Request, key.Result))!);
        return runner.Run(this, request, ct);
    }

    public Task<TResult> ServiceCall<TService, TInput, TResult>(TInput input, Func<TService, TInput, Task<TResult>> call,
        CancellationToken ct = default, [CallerArgumentExpression(nameof(call))] string? operation = null) where TService : notnull
        => Go(new Invocation<TInput, TResult>("service", typeof(TService).Name, operation ?? "?", input,
            (sp, i, c) => call(sp.GetRequiredService<TService>(), i), ct));

    public Task<TResult> ServiceCall<TService, TInput, TResult>(TInput input, Func<TService, Func<TInput, Task<TResult>>> select,
        CancellationToken ct = default, [CallerArgumentExpression(nameof(select))] string? operation = null) where TService : notnull
        => Go(new Invocation<TInput, TResult>("service", typeof(TService).Name, operation ?? "?", input,
            (sp, i, c) => select(sp.GetRequiredService<TService>())(i), ct));

    public async Task ServiceCall<TService, TInput>(TInput input, Func<TService, TInput, Task> call,
        CancellationToken ct = default, [CallerArgumentExpression(nameof(call))] string? operation = null) where TService : notnull
        => await Go(new Invocation<TInput, Unit>("service", typeof(TService).Name, operation ?? "?", input,
            async (sp, i, c) => { await call(sp.GetRequiredService<TService>(), i); return default; }, ct));

    private abstract class Runner<TResult>
    {
        public abstract Task<TResult> Run(Dispatcher d, IRequest<TResult> r, CancellationToken ct);
    }
    private sealed class Runner<TRequest, TResult> : Runner<TResult> where TRequest : IRequest<TResult>
    {
        public override Task<TResult> Run(Dispatcher d, IRequest<TResult> r, CancellationToken ct)
            => d.Go(new Invocation<TRequest, TResult>("request", typeof(TRequest).Name, typeof(TRequest).Name, (TRequest)r,
                (sp, i, c) => sp.GetRequiredService<IHandler<TRequest, TResult>>().Handle(i, c), ct));
    }
}

public static class DispatchExtensions
{
    /// <summary>Registers public, concrete, closed handlers as scoped services.</summary>
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

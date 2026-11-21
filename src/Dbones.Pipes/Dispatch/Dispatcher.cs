#nullable enable
using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Dbones.Pipes.Dispatch;

/// <summary>Dispatches invocations using cached request runners and the caller's service scope.</summary>
public sealed class Dispatcher : IDispatcher
{
    private readonly IServiceProvider _scope;

    // A request can implement more than one IRequest<TResult> interface.
    private static readonly ConcurrentDictionary<(Type Request, Type Result), object> Runners = new();
    /// <summary>Creates a dispatcher bound to the caller's service scope.</summary>
    /// <param name="scope">The provider used to resolve pipelines, actions, handlers and services.</param>
    public Dispatcher(IServiceProvider scope) => _scope = scope;

    private Task<TResult> Go<TInput, TResult>(Invocation<TInput, TResult> inv)
        => _scope.GetRequiredService<InvocationPipeline<TInput, TResult>>().Execute(_scope, inv);

    /// <inheritdoc />
    public Task<TResult> Send<TResult>(IRequest<TResult> request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var runner = (Runner<TResult>)Runners.GetOrAdd((request.GetType(), typeof(TResult)), key =>
            Activator.CreateInstance(typeof(Runner<,>).MakeGenericType(key.Request, key.Result))!);
        return runner.Run(this, request, ct);
    }

    /// <inheritdoc />
    public Task<TResult> ServiceCall<TService, TInput, TResult>(TInput input,
        Func<TService, TInput, Task<TResult>> call,
        CancellationToken ct = default, [CallerArgumentExpression(nameof(call))] string? operation = null)
        where TService : notnull
        => Go(new Invocation<TInput, TResult>("service", typeof(TService).Name, operation ?? "?", input,
            (sp, i, c) => call(sp.GetRequiredService<TService>(), i), ct));

    /// <inheritdoc />
    public Task<TResult> ServiceCall<TService, TInput, TResult>(TInput input,
        Func<TService, Func<TInput, Task<TResult>>> select,
        CancellationToken ct = default, [CallerArgumentExpression(nameof(select))] string? operation = null)
        where TService : notnull
        => Go(new Invocation<TInput, TResult>("service", typeof(TService).Name, operation ?? "?", input,
            (sp, i, c) => select(sp.GetRequiredService<TService>())(i), ct));

    /// <inheritdoc />
    public async Task ServiceCall<TService, TInput>(TInput input, Func<TService, TInput, Task> call,
        CancellationToken ct = default, [CallerArgumentExpression(nameof(call))] string? operation = null)
        where TService : notnull
        => await Go(new Invocation<TInput, Unit>("service", typeof(TService).Name, operation ?? "?", input,
            async (sp, i, c) =>
            {
                await call(sp.GetRequiredService<TService>(), i);
                return default;
            }, ct));

    private abstract class Runner<TResult>
    {
        public abstract Task<TResult> Run(Dispatcher d, IRequest<TResult> r, CancellationToken ct);
    }

    private sealed class Runner<TRequest, TResult> : Runner<TResult> where TRequest : IRequest<TResult>
    {
        public override Task<TResult> Run(Dispatcher d, IRequest<TResult> r, CancellationToken ct)
            => d.Go(new Invocation<TRequest, TResult>("request", typeof(TRequest).Name, typeof(TRequest).Name,
                (TRequest)r,
                (sp, i, c) => sp.GetRequiredService<IHandler<TRequest, TResult>>().Handle(i, c), ct));
    }
}

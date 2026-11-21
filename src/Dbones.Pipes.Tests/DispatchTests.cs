using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dispatch;
using Machine.Specifications;
using Microsoft.Extensions.DependencyInjection;

namespace Dbones.Pipes.Tests;

public class DispatchTests
{
    It registers_concrete_handlers_once_with_scoped_lifetime = () =>
    {
        var services = new ServiceCollection();
        services.AddHandlersFrom(typeof(DispatchTests).Assembly, typeof(DispatchTests).Assembly);
        services.AddHandlersFrom(typeof(DispatchTests).Assembly);
        var handlers = services.Where(s => s.ServiceType == typeof(IHandler<Request, int>)).ToArray();
        Check(handlers.Length == 1 && handlers[0].ImplementationType == typeof(Handler));
        Check(handlers[0].Lifetime == ServiceLifetime.Scoped);
        Check(!services.Any(s => s.ImplementationType == typeof(AbstractHandler)));
        Check(!services.Any(s => s.ImplementationType == typeof(OpenHandler<>)));
    };

    It runs_requests_and_all_service_overloads_in_the_callers_scope = () => Run(async () =>
    {
        var services = new ServiceCollection();
        services.AddHandlersFrom(typeof(DispatchTests).Assembly);
        services.AddScoped<State>();
        services.AddScoped<Service>();
        services.AddDispatcher(o => o.Actions.Add(typeof(Observe<,>)));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var state = scope.ServiceProvider.GetRequiredService<State>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        using var cts = new CancellationTokenSource();
        Check(await dispatcher.Send(new Request(), cts.Token) == 42);
        Check(state.Token == cts.Token);
        Check(await dispatcher.ServiceCall<Service, int, int>(5, (s, i) => s.Double(i)) == 10);
        Check(await dispatcher.ServiceCall<Service, int, int>(6, s => s.Double) == 12);
        await dispatcher.ServiceCall<Service, int>(7, (s, i) => s.Store(i));
        Check(state.Value == 7 && state.Calls == 4 && state.Observations == 4);
        Check(state.Operation.Contains("Store"));
        using var other = provider.CreateScope();
        await other.ServiceProvider.GetRequiredService<IDispatcher>().Send(new Request());
        Check(state.Calls == 4);
    });

    It caches_runners_for_each_request_and_result_pair = () => Run(async () =>
    {
        var services = new ServiceCollection();
        services.AddHandlersFrom(typeof(DispatchTests).Assembly);
        services.AddDispatcher(_ => { });
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var request = new MultiRequest();
        Check(await dispatcher.Send((IRequest<int>)request) == 1);
        Check(await dispatcher.Send((IRequest<string>)request) == "one");
    });

    static void Run(Func<Task> test) => test().GetAwaiter().GetResult();
    internal static void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }

    public sealed class Request : IRequest<int> { }
    public sealed class State
    {
        public int Calls, Observations, Value;
        public CancellationToken Token;
        public string Operation;
    }
    public abstract class AbstractHandler : IHandler<Request, int>
    {
        public abstract Task<int> Handle(Request request, CancellationToken ct);
    }
    public class OpenHandler<T> : IHandler<Request, int>
    {
        public Task<int> Handle(Request request, CancellationToken ct) => Task.FromResult(0);
    }
    public sealed class Handler : IHandler<Request, int>
    {
        readonly State state;
        public Handler(State state) => this.state = state;
        public Task<int> Handle(Request request, CancellationToken ct)
        {
            Check(state.Observations == state.Calls + 1);
            state.Calls++;
            state.Token = ct;
            return Task.FromResult(42);
        }
    }
    public sealed class Service
    {
        readonly State state;
        public Service(State state) => this.state = state;
        public Task<int> Double(int input) { state.Calls++; return Task.FromResult(input * 2); }
        public Task Store(int input) { state.Calls++; state.Value = input; return Task.CompletedTask; }
    }
    public sealed class Observe<TInput, TResult> : IAction<Invocation<TInput, TResult>>
    {
        readonly State state;
        public Observe(State state) => this.state = state;
        public async Task Execute(Invocation<TInput, TResult> inv, Next<Invocation<TInput, TResult>> next)
        {
            state.Observations++;
            state.Operation = inv.Operation;
            await next(inv);
        }
    }
    public sealed class MultiRequest : IRequest<int>, IRequest<string> { }
    public sealed class MultiHandler : IHandler<MultiRequest, int>, IHandler<MultiRequest, string>
    {
        Task<int> IHandler<MultiRequest, int>.Handle(MultiRequest r, CancellationToken ct) => Task.FromResult(1);
        Task<string> IHandler<MultiRequest, string>.Handle(MultiRequest r, CancellationToken ct) => Task.FromResult("one");
    }
}

public class ScopedMiddlewareTests
{
    It keeps_the_child_scope_alive_until_async_execution_completes = () => Verify(false).GetAwaiter().GetResult();
    It disposes_the_child_scope_when_async_execution_fails = () => Verify(true).GetAwaiter().GetResult();

    static async Task Verify(bool fail)
    {
        var services = new ServiceCollection();
        services.AddScoped<Resource>();
        services.AddScoped<AsyncAction>();
        using var provider = services.BuildServiceProvider();
        var middleware = new ScopedMiddleware<Context>();
        middleware.Add<AsyncAction>();
        var context = new Context { Fail = fail };
        var execution = middleware.Execute(provider, context);
        await context.Started.Task;
        DispatchTests.Check(!context.Resource.Disposed);
        context.Resume.SetResult(true);
        try { await execution; DispatchTests.Check(!fail); }
        catch (InvalidOperationException) { DispatchTests.Check(fail); }
        DispatchTests.Check(context.Resource.Disposed);
    }
    public sealed class Context
    {
        public bool Fail;
        public Resource Resource;
        public TaskCompletionSource<bool> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    public sealed class Resource : IDisposable
    {
        public bool Disposed;
        public void Dispose() => Disposed = true;
    }
    public sealed class AsyncAction : IAction<Context>
    {
        readonly Resource resource;
        public AsyncAction(Resource resource) => this.resource = resource;
        public async Task Execute(Context context, Next<Context> next)
        {
            context.Resource = resource;
            context.Started.SetResult(true);
            await context.Resume.Task;
            DispatchTests.Check(!resource.Disposed);
            if (context.Fail) throw new InvalidOperationException();
            await next(context);
        }
    }
}

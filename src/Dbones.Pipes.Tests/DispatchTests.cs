using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dbones.Pipes.Dispatch;
using Machine.Specifications;
using Microsoft.Extensions.DependencyInjection;

namespace Dbones.Pipes.Tests;

/// <summary>Verifies reflection registration, dispatch results and caller-scope behavior.</summary>
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

    /// <summary>Provides an integer-result request for dispatch tests.</summary>
    public sealed class Request : IRequest<int> { }
    /// <summary>Records scope-local handler and action observations.</summary>
    public sealed class State
    {
        /// <summary>Counts handler and service calls.</summary>
        public int Calls;
        /// <summary>Counts invocation action executions.</summary>
        public int Observations;
        /// <summary>Stores the last service-call input.</summary>
        public int Value;
        /// <summary>Stores the cancellation token observed by the handler.</summary>
        public CancellationToken Token;
        /// <summary>Stores the most recently observed operation description.</summary>
        public string Operation;
    }
    /// <summary>Provides an abstract handler that assembly scanning must exclude.</summary>
    public abstract class AbstractHandler : IHandler<Request, int>
    {
        /// <summary>Processes one request and returns its result.</summary>
        public abstract Task<int> Handle(Request request, CancellationToken ct);
    }
    /// <summary>Provides an open generic handler that assembly scanning must exclude.</summary>
    public class OpenHandler<T> : IHandler<Request, int>
    {
        /// <summary>Processes one request and returns its result.</summary>
        public Task<int> Handle(Request request, CancellationToken ct) => Task.FromResult(0);
    }
    /// <summary>Counts a request execution and returns the common benchmark result.</summary>
    public sealed class Handler : IHandler<Request, int>
    {
        readonly State state;
        /// <summary>Creates the resources for this Handler instance.</summary>
        public Handler(State state) => this.state = state;
        /// <summary>Processes one request and returns its result.</summary>
        public Task<int> Handle(Request request, CancellationToken ct)
        {
            Check(state.Observations == state.Calls + 1);
            state.Calls++;
            state.Token = ct;
            return Task.FromResult(42);
        }
    }
    /// <summary>Provides result-returning and resultless service calls for dispatch tests.</summary>
    public sealed class Service
    {
        readonly State state;
        /// <summary>Creates the resources for this Service instance.</summary>
        public Service(State state) => this.state = state;
        /// <summary>Counts a service call and returns twice the input.</summary>
        public Task<int> Double(int input) { state.Calls++; return Task.FromResult(input * 2); }
        /// <summary>Counts a service call and records the input.</summary>
        public Task Store(int input) { state.Calls++; state.Value = input; return Task.CompletedTask; }
    }
    /// <summary>Records invocation metadata before forwarding to the next action.</summary>
    public sealed class Observe<TInput, TResult> : IAction<Invocation<TInput, TResult>>
    {
        readonly State state;
        /// <summary>Creates the resources for this Observe instance.</summary>
        public Observe(State state) => this.state = state;
        /// <summary>Counts this action execution and awaits the next stage.</summary>
        public async Task Execute(Invocation<TInput, TResult> inv, Next<Invocation<TInput, TResult>> next)
        {
            state.Observations++;
            state.Operation = inv.Operation;
            await next(inv);
        }
    }
    /// <summary>Provides a request with two result contracts to test runner caching.</summary>
    public sealed class MultiRequest : IRequest<int>, IRequest<string> { }
    /// <summary>Handles both result contracts of the same request type.</summary>
    public sealed class MultiHandler : IHandler<MultiRequest, int>, IHandler<MultiRequest, string>
    {
        Task<int> IHandler<MultiRequest, int>.Handle(MultiRequest r, CancellationToken ct) => Task.FromResult(1);
        Task<string> IHandler<MultiRequest, string>.Handle(MultiRequest r, CancellationToken ct) => Task.FromResult("one");
    }
}

/// <summary>Verifies child-scope lifetime across asynchronous completion and failure.</summary>
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
    /// <summary>Coordinates a suspended middleware action and its expected outcome.</summary>
    public sealed class Context
    {
        /// <summary>Indicates whether the action should fail after resuming.</summary>
        public bool Fail;
        /// <summary>Stores the dependency captured by the action.</summary>
        public Resource Resource;
        /// <summary>Executes the operation.</summary>
        public TaskCompletionSource<bool> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>Executes the operation.</summary>
        public TaskCompletionSource<bool> Resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    /// <summary>Records disposal of a scope-owned dependency.</summary>
    public sealed class Resource : IDisposable
    {
        /// <summary>Indicates whether the resource has been disposed.</summary>
        public bool Disposed;
        /// <summary>Releases owned resources and records disposal where applicable.</summary>
        public void Dispose() => Disposed = true;
    }
    /// <summary>Suspends execution to verify that its scoped dependency remains alive.</summary>
    public sealed class AsyncAction : IAction<Context>
    {
        readonly Resource resource;
        /// <summary>Creates the resources for this AsyncAction instance.</summary>
        public AsyncAction(Resource resource) => this.resource = resource;
        /// <summary>Counts this action execution and awaits the next stage.</summary>
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

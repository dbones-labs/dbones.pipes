---
layout: default
title: Dispatcher, commands, queries and services
parent: Quick Examples
nav_order: 2
---

# Dispatcher: commands, queries and services

The dispatcher runs request handlers and service calls through the same ordered action pipeline. A command changes state; a query reads state and returns a value. Both are represented by `IRequest<TResult>` and handled by `IHandler<TRequest, TResult>`. For an operation without a meaningful result, use `Unit`.

## Complete demo

Create a .NET 10 console app and add `Dbones.Pipes` version 2.0 or a compatible prerelease, plus `Microsoft.Extensions.DependencyInjection`. The selected package version must be available on your configured NuGet feed; see [GitHub Packages setup](nuget-github.md) for GitHub-hosted builds. Replace `Program.cs` with the following and run `dotnet run`:

```csharp
using Dbones.Pipes;
using Dbones.Pipes.Dispatch;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddSingleton<OrderStore>();
services.AddScoped<PricingService>();
services.AddHandlersFrom(typeof(PlaceOrderHandler).Assembly);
services.AddDispatcher(options => options.Actions.Add(typeof(TraceAction<,>)));

using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
using var scope = provider.CreateScope();
var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
var cancellationToken = CancellationToken.None;

// A command changes state; Unit represents a successful operation without a value.
await dispatcher.Send(new PlaceOrder("order-1", 25m), cancellationToken);

// A query returns a value from its handler.
var total = await dispatcher.Send(new GetOrderTotal("order-1"), cancellationToken);
Console.WriteLine($"Order total: {total:C}");

// Service methods use the same pipeline. Pass cancellation to the method explicitly.
var discounted = await dispatcher.ServiceCall<PricingService, decimal, decimal>(
    total, (service, amount) => service.ApplyDiscount(amount, cancellationToken), cancellationToken);
Console.WriteLine($"Discounted total: {discounted:C}");

// A method group can also be selected when its signature accepts the input alone.
var formatted = await dispatcher.ServiceCall<PricingService, decimal, string>(
    discounted, service => service.Format);
Console.WriteLine($"Formatted: {formatted}");

// Task-returning service methods without a result use the two-type-parameter overload.
await dispatcher.ServiceCall<PricingService, decimal>(
    discounted, (service, amount) => service.Notify(amount));

public sealed record PlaceOrder(string Id, decimal Total) : IRequest<Unit>;
public sealed record GetOrderTotal(string Id) : IRequest<decimal>;

public sealed class OrderStore
{
    private readonly Dictionary<string, decimal> orders = new();
    public void Put(string id, decimal total) => orders[id] = total;
    public decimal Get(string id) => orders[id];
}

public sealed class PlaceOrderHandler(OrderStore store) : IHandler<PlaceOrder, Unit>
{
    public Task<Unit> Handle(PlaceOrder request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        store.Put(request.Id, request.Total);
        return Task.FromResult(default(Unit));
    }
}

public sealed class GetOrderTotalHandler(OrderStore store) : IHandler<GetOrderTotal, decimal>
{
    public Task<decimal> Handle(GetOrderTotal request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(store.Get(request.Id));
    }
}

public sealed class PricingService
{
    public Task<decimal> ApplyDiscount(decimal total, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(total * 0.9m);
    }
    public Task<string> Format(decimal total) => Task.FromResult(total.ToString("C"));
    public Task Notify(decimal total)
    {
        Console.WriteLine($"Notified: {total:C}");
        return Task.CompletedTask;
    }
}

public sealed class TraceAction<TInput, TResult> : IAction<Invocation<TInput, TResult>>
{
    public async Task Execute(Invocation<TInput, TResult> invocation, Next<Invocation<TInput, TResult>> next)
    {
        Console.WriteLine($"{invocation.Kind}: {invocation.Target} / {invocation.Operation}");
        await next(invocation);
    }
}
```

The demo places an order, queries its total, and calls a service through the same tracing action. The in-memory store is only for demonstrating state; it is not designed for concurrent use.

## Register the dispatcher

```csharp
using Dbones.Pipes;
using Dbones.Pipes.Dispatch;
using Microsoft.Extensions.DependencyInjection;

services.AddSingleton<OrderStore>();
services.AddScoped<PricingService>();
services.AddHandlersFrom(typeof(PlaceOrderHandler).Assembly);
services.AddDispatcher(options => options.Actions.Add(typeof(TraceAction<,>)));
```

`AddHandlersFrom` scans the specified assemblies for public, concrete, closed handlers and registers their implemented interfaces as scoped services. Register services and handler dependencies separately. `Actions` contains open generic types with input and result type parameters, in execution order. To run without additional actions, use `services.AddDispatcher(_ => { });`.

## Send a command and query

```csharp
public sealed record PlaceOrder(string Id, decimal Total) : IRequest<Unit>;
public sealed record GetOrderTotal(string Id) : IRequest<decimal>;

// Resolve the dispatcher inside an existing request or explicit service scope.
using var scope = provider.CreateScope();
var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
await dispatcher.Send(new PlaceOrder("order-1", 25m), cancellationToken);
var total = await dispatcher.Send(new GetOrderTotal("order-1"), cancellationToken);
```

Each request needs a corresponding `IHandler<TRequest, TResult>`. The demo includes both handler implementations. The cancellation token is passed to the handler and exposed to actions; handlers are responsible for observing it.

## Call a service

```csharp
var discounted = await dispatcher.ServiceCall<PricingService, decimal, decimal>(
    total, (service, amount) => service.ApplyDiscount(amount, cancellationToken), cancellationToken);

var formatted = await dispatcher.ServiceCall<PricingService, decimal, string>(
    discounted, service => service.Format);

await dispatcher.ServiceCall<PricingService, decimal>(
    discounted, (service, amount) => service.Notify(amount));
```

The overloads accept a result-returning delegate, a selected method group, or a `Task`-returning delegate without a result. The token supplied to `ServiceCall` is available to actions as `Invocation.Ct`; pass it explicitly in the delegate when the service method needs it.

## Add an action

```csharp
public sealed class TraceAction<TInput, TResult> : IAction<Invocation<TInput, TResult>>
{
    public async Task Execute(Invocation<TInput, TResult> invocation, Next<Invocation<TInput, TResult>> next)
    {
        Console.WriteLine($"{invocation.Kind}: {invocation.Target} / {invocation.Operation}");
        await next(invocation);
    }
}
```

Actions see the input, cancellation token, kind, target and operation through `Invocation<TInput, TResult>`. They can inspect the result after `next`, or set `Result` and omit `next` to short-circuit. The terminal action invokes the handler or service.

The dispatcher, actions, handlers and services resolve from the **caller's scope**; the dispatcher does not create a child scope. Create a scope for each independent unit of work, and do not resolve scoped services from the root provider. Pipeline definitions are shared; scoped action instances are not.

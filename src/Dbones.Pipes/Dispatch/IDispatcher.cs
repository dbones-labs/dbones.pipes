#nullable enable
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Dbones.Pipes.Dispatch;

/// <summary>Runs request handlers and service calls through the configured invocation actions.</summary>
public interface IDispatcher
{
    /// <summary>Dispatches a request to its handler in the caller's scope.</summary>
    /// <typeparam name="TResult">The request result type.</typeparam>
    /// <param name="request">The request whose runtime type identifies the handler.</param>
    /// <param name="ct">The cancellation token passed to the handler and actions.</param>
    /// <returns>A task containing the pipeline result.</returns>
    /// <exception cref="ArgumentNullException">The request is null.</exception>
    Task<TResult> Send<TResult>(IRequest<TResult> request, CancellationToken ct = default);

    /// <summary>Invokes a result-returning service method through the pipeline.</summary>
    /// <typeparam name="TService">The service resolved from the caller's scope.</typeparam>
    /// <typeparam name="TInput">The service method input type.</typeparam>
    /// <typeparam name="TResult">The service method result type.</typeparam>
    /// <param name="input">The input exposed to actions and passed to the service method.</param>
    /// <param name="call">Invokes the resolved service with the input.</param>
    /// <param name="ct">The token exposed to actions; the delegate must forward cancellation to the service when needed.</param>
    /// <param name="operation">The operation description; defaults to the caller's delegate expression.</param>
    /// <returns>A task containing the pipeline result.</returns>
    Task<TResult> ServiceCall<TService, TInput, TResult>(TInput input, Func<TService, TInput, Task<TResult>> call,
        CancellationToken ct = default, [CallerArgumentExpression(nameof(call))] string? operation = null)
        where TService : notnull;

    /// <summary>Invokes a selected service method through the pipeline.</summary>
    /// <typeparam name="TService">The service resolved from the caller's scope.</typeparam>
    /// <typeparam name="TInput">The service method input type.</typeparam>
    /// <typeparam name="TResult">The service method result type.</typeparam>
    /// <param name="input">The input exposed to actions and passed to the service method.</param>
    /// <param name="select">Selects the method to invoke on the resolved service.</param>
    /// <param name="ct">The token exposed to actions; the delegate must forward cancellation to the service when needed.</param>
    /// <param name="operation">The operation description; defaults to the caller's delegate expression.</param>
    /// <returns>A task containing the pipeline result.</returns>
    Task<TResult> ServiceCall<TService, TInput, TResult>(TInput input,
        Func<TService, Func<TInput, Task<TResult>>> select,
        CancellationToken ct = default, [CallerArgumentExpression(nameof(select))] string? operation = null)
        where TService : notnull;

    /// <summary>Invokes a service method with no result through the pipeline.</summary>
    /// <typeparam name="TService">The service resolved from the caller's scope.</typeparam>
    /// <typeparam name="TInput">The service method input type.</typeparam>
    /// <param name="input">The input exposed to actions and passed to the service method.</param>
    /// <param name="call">Invokes the resolved service with the input.</param>
    /// <param name="ct">The token exposed to actions; the delegate must forward cancellation to the service when needed.</param>
    /// <param name="operation">The operation description; defaults to the caller's delegate expression.</param>
    /// <returns>A task representing pipeline completion.</returns>
    Task ServiceCall<TService, TInput>(TInput input, Func<TService, TInput, Task> call,
        CancellationToken ct = default, [CallerArgumentExpression(nameof(call))] string? operation = null)
        where TService : notnull;
}

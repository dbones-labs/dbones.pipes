#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Dbones.Pipes.Dispatch;

/// <summary>Provides the input, metadata and result shared by invocation actions.</summary>
/// <typeparam name="TInput">The request or service-call input type.</typeparam>
/// <typeparam name="TResult">The invocation result type.</typeparam>
public sealed class Invocation<TInput, TResult>
{
    /// <summary>Creates an invocation for a request handler or service call.</summary>
    /// <param name="kind">The invocation kind, typically "request" or "service".</param>
    /// <param name="target">The request or service type name used for diagnostics.</param>
    /// <param name="operation">The operation description exposed to actions.</param>
    /// <param name="input">The input exposed to actions and the terminal operation.</param>
    /// <param name="run">The terminal operation, resolved against the caller's service provider.</param>
    /// <param name="ct">The invocation cancellation token.</param>
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

    /// <summary>Gets the invocation kind.</summary>
    public string Kind { get; }
    /// <summary>Gets the request or service type name.</summary>
    public string Target { get; }
    /// <summary>Gets the operation description.</summary>
    public string Operation { get; }
    /// <summary>Gets the input exposed to every action.</summary>
    public TInput Input { get; }
    /// <summary>Gets the cancellation token exposed to actions and the terminal operation.</summary>
    public CancellationToken Ct { get; }
    /// <summary>Gets or sets the result, which an action may supply when short-circuiting the pipeline.</summary>
    public TResult? Result { get; set; }
    internal Func<IServiceProvider, TInput, CancellationToken, Task<TResult>> Run { get; }
}

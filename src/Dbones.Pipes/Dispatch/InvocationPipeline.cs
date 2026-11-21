#nullable enable
using System;
using System.Threading.Tasks;

namespace Dbones.Pipes.Dispatch;

/// <summary>Builds a reusable action chain that resolves actions from each execution's caller scope.</summary>
/// <typeparam name="TInput">The invocation input type.</typeparam>
/// <typeparam name="TResult">The invocation result type.</typeparam>
public sealed class InvocationPipeline<TInput, TResult>
{
    private readonly Middleware<Invocation<TInput, TResult>> _middleware = new();

    /// <summary>Closes the configured action types and appends the terminal action.</summary>
    /// <param name="options">The ordered invocation action configuration.</param>
    public InvocationPipeline(PipelineOptions options)
    {
        foreach (var open in options.Actions)
            _middleware.Add(open.MakeGenericType(typeof(TInput), typeof(TResult)));
        _middleware.Add<InvokeAction<TInput, TResult>>();
    }

    /// <summary>Executes the action chain without creating a child scope.</summary>
    /// <param name="scope">The caller's provider, used to resolve every action.</param>
    /// <param name="inv">The invocation shared by the actions.</param>
    /// <returns>The result supplied by the terminal operation or a short-circuiting action.</returns>
    public async Task<TResult> Execute(IServiceProvider scope, Invocation<TInput, TResult> inv)
    {
        await _middleware.Execute(scope, inv);
        return inv.Result!;
    }
}

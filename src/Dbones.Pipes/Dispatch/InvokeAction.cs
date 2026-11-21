#nullable enable
using System;
using System.Threading.Tasks;

namespace Dbones.Pipes.Dispatch;

/// <summary>Terminates the pipeline by executing the invocation in the injected scope.</summary>
/// <typeparam name="TInput">The invocation input type.</typeparam>
/// <typeparam name="TResult">The invocation result type.</typeparam>
public sealed class InvokeAction<TInput, TResult> : IAction<Invocation<TInput, TResult>>
{
    private readonly IServiceProvider _scope;
    /// <summary>Creates the terminal action for a service scope.</summary>
    /// <param name="scope">The provider used to resolve the handler or service.</param>
    public InvokeAction(IServiceProvider scope) => _scope = scope;

    /// <summary>Executes the terminal operation and stores its result without invoking the next action.</summary>
    /// <param name="inv">The invocation to execute.</param>
    /// <param name="next">The unused continuation; this action terminates the pipeline.</param>
    /// <returns>A task representing completion of the terminal operation.</returns>
    public async Task Execute(Invocation<TInput, TResult> inv, Next<Invocation<TInput, TResult>> next)
        => inv.Result = await inv.Run(_scope, inv.Input, inv.Ct);
}

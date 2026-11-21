#nullable enable
using System.Threading;
using System.Threading.Tasks;

namespace Dbones.Pipes.Dispatch;

/// <summary>Handles a request asynchronously within the dispatcher's scope.</summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResult">The result type.</typeparam>
public interface IHandler<in TRequest, TResult> where TRequest : IRequest<TResult>
{
    /// <summary>Processes the supplied request.</summary>
    /// <param name="request">The request to handle.</param>
    /// <param name="ct">The cancellation token for the operation.</param>
    /// <returns>A task containing the handler result.</returns>
    Task<TResult> Handle(TRequest request, CancellationToken ct);
}

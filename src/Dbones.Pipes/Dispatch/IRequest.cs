#nullable enable

namespace Dbones.Pipes.Dispatch;

/// <summary>Marks a request that produces a result of the specified type.</summary>
/// <typeparam name="TResult">The handler result type.</typeparam>
public interface IRequest<TResult>
{
}

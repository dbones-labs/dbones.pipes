#nullable enable
using System;
using System.Collections.Generic;

namespace Dbones.Pipes.Dispatch;

/// <summary>Configures the actions applied to every dispatcher invocation.</summary>
public sealed class PipelineOptions
{
    /// <summary>Gets action types in execution order, before the terminal action.</summary>
    /// <remarks>Each entry must be an open generic action type with input and result type parameters.</remarks>
    public List<Type> Actions { get; } = new();
}

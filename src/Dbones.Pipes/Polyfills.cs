#nullable enable
#if !NET6_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    /// <summary>Lets the compiler supply the source text of an argument (polyfill for targets before .NET 6).</summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    internal sealed class CallerArgumentExpressionAttribute : Attribute
    {
        /// <summary>Creates the attribute for the named parameter.</summary>
        /// <param name="parameterName">The parameter whose argument expression is captured.</param>
        public CallerArgumentExpressionAttribute(string parameterName) => ParameterName = parameterName;

        /// <summary>The name of the parameter whose argument expression is captured.</summary>
        public string ParameterName { get; }
    }
}
#endif

#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    /// <summary>Required by the compiler for <c>init</c> accessors and records (polyfill for targets before .NET 5).</summary>
    internal static class IsExternalInit { }
}
#endif

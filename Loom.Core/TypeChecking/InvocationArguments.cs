using Loom.Core.Parsing.AST;

namespace Loom.Core.TypeChecking;

/// <summary>
///     Answers which argument of a call lands on a declared parameter, once any of them is named.
/// </summary>
/// <remarks>
///     A named argument's position in the call means nothing, so reading <c>ArgumentList[i]</c> as the
///     argument for parameter <c>i</c> is only right for a purely positional call. Anything past the type
///     checker that asks "what was passed for parameter <c>i</c>" - narrowing through a type predicate, code
///     generation - has to ask here instead.
/// </remarks>
internal static class InvocationArguments
{
    /// <summary>
    ///     The declared parameters a name can address: every parameter but a trailing rest one, which
    ///     collects positional overflow and is never addressed by name.
    /// </summary>
    public static List<Parameter> FixedParameters(Parameters parameters) =>
        parameters.ParameterList is [.., { DotDot: not null }]
            ? parameters.ParameterList.GetRange(0, parameters.ParameterList.Count - 1)
            : parameters.ParameterList;

    /// <summary>
    ///     The expression passed for the declared parameter at <paramref name="parameterIndex" />, a named
    ///     argument reduced to its value, or null where nothing was passed or it cannot be told - a call
    ///     naming arguments whose callee has no statically known declaration.
    /// </summary>
    public static Expression? ForParameter(Arguments arguments, Parameters? parameters, int parameterIndex)
    {
        var argumentList = arguments.ArgumentList;
        if (!argumentList.Exists(argument => argument is NamedArgument))
            return argumentList.ElementAtOrDefault(parameterIndex);

        if (parameters == null)
            return null;

        var fixedParameters = FixedParameters(parameters);
        if (parameterIndex < 0 || parameterIndex >= fixedParameters.Count)
            return null;

        var name = fixedParameters[parameterIndex].Name.Text;
        var positionalCount = 0;
        foreach (var argument in argumentList)
        {
            if (argument is NamedArgument named)
            {
                if (named.Name.Text == name)
                    return named.Value;

                continue;
            }

            if (positionalCount == parameterIndex)
                return argument;

            positionalCount++;
        }

        return null;
    }
}

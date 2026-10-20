using System.Reflection;
using System.Runtime.CompilerServices;
using Scriban;
using Scriban.Runtime;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Templating;

/// <summary>
/// The type policy of host function delegates (<see cref="ITemplateFunctionProvider"/>). It is applied
/// twice: when a provider is registered (an unsupported signature fails the registration with an
/// <see cref="ArgumentException"/>, so the host learns about it at startup) and again on every result
/// (an unexpected value fails the render closed). Both directions exist because the sandbox must not
/// depend on a delegate signature being honest — a provider function may never hand a
/// <see cref="Type"/>, a delegate, a value of a forbidden namespace or one of the engine's escaping
/// markers to a template.
/// </summary>
internal static class TemplateFunctionResultPolicy
{
    /// <summary>
    /// Validates one provider function signature. Parameters and the return value must be a contract
    /// type, the delegate must be synchronous and must not use <c>ref</c>/<c>out</c> parameters.
    /// </summary>
    /// <exception cref="ArgumentException">The signature cannot be exposed to a template.</exception>
    public static void ValidateSignature(string @namespace, string name, Delegate function)
    {
        var qualifiedName = Qualify(@namespace, name);
        var method = function.Method;

        if (method.IsDefined(typeof(AsyncStateMachineAttribute), inherit: false))
        {
            throw new ArgumentException(
                $"Template function '{qualifiedName}' must be synchronous: an async delegate is not supported.",
                nameof(function));
        }

        if (method.ReturnType == typeof(void))
        {
            throw new ArgumentException(
                $"Template function '{qualifiedName}' must return a value (the contract types are " +
                "string, bool, numeric, decimal, DateTime, DateTimeOffset or an IEnumerable of those).",
                nameof(function));
        }

        if (IsAwaitable(method.ReturnType))
        {
            throw new ArgumentException(
                $"Template function '{qualifiedName}' must be synchronous: a delegate returning " +
                $"{method.ReturnType.Name} would leave the render waiting for host code.",
                nameof(function));
        }

        foreach (var parameter in method.GetParameters())
        {
            if (parameter.ParameterType.IsByRef)
            {
                throw new ArgumentException(
                    $"Template function '{qualifiedName}' must not use a ref/out parameter ('{parameter.Name}').",
                    nameof(function));
            }

            RequireContractType(qualifiedName, $"parameter '{parameter.Name}'", parameter.ParameterType, name);
        }

        RequireContractType(qualifiedName, "return type", method.ReturnType, name);
    }

    /// <summary>
    /// Refuses a result that must never reach a template: reflection handles, host code, values of a
    /// forbidden namespace, the engine's escaping markers and any type outside the contract.
    /// </summary>
    /// <returns>The value, so the caller can return it unchanged.</returns>
    /// <exception cref="TemplateSecurityViolationException">The result is not allowed (fail closed).</exception>
    public static object? EnsureAllowed(object? value, string qualifiedName)
    {
        if (value is null)
        {
            return null;
        }

        var valueType = value.GetType();

        if (value is Delegate)
        {
            throw Refuse(
                TemplateSecurityEventKind.BlockedMember,
                $"{qualifiedName} returned a delegate",
                SandboxMemberPolicy.DescribeForbiddenType(valueType));
        }

        if (value is Type || value is MemberInfo || value is Assembly || value is Module ||
            SandboxMemberPolicy.IsForbiddenNamespace(valueType.Namespace))
        {
            throw Refuse(
                TemplateSecurityEventKind.BlockedNamespace,
                $"{qualifiedName} returned a value of the forbidden namespace",
                SandboxMemberPolicy.DescribeForbiddenType(valueType));
        }

        // The escaping markers are the engine's internal contract with the html filters: a host function
        // must not be able to forge "this is already trusted/encoded" markup.
        if (value is TrustedHtml || value is EscapedHtml)
        {
            throw Refuse(
                TemplateSecurityEventKind.BlockedMember,
                $"{qualifiedName} returned an escaping marker",
                valueType.Name);
        }

        // A script object would expose callable members of the host object graph, and a template context
        // would hand out the sandbox itself.
        if (value is IScriptObject || value is TemplateContext)
        {
            throw Refuse(
                TemplateSecurityEventKind.BlockedMember,
                $"{qualifiedName} returned a script host object",
                valueType.Name);
        }

        if (!IsContractType(valueType))
        {
            throw Refuse(
                TemplateSecurityEventKind.BlockedMember,
                $"{qualifiedName} returned a value that is not a contract type",
                valueType.FullName ?? valueType.Name);
        }

        return value;
    }

    /// <summary>
    /// True when a value of the type may be handed to (or returned by) a host function: a contract
    /// scalar, a nullable contract scalar, or an <see cref="IEnumerable{T}"/> of those.
    /// </summary>
    public static bool IsContractType(Type type)
    {
        var type1 = Nullable.GetUnderlyingType(type) ?? type;
        switch (Type.GetTypeCode(type1))
        {
            case TypeCode.String:
            case TypeCode.Boolean:
            case TypeCode.SByte:
            case TypeCode.Byte:
            case TypeCode.Int16:
            case TypeCode.UInt16:
            case TypeCode.Int32:
            case TypeCode.UInt32:
            case TypeCode.Int64:
            case TypeCode.UInt64:
            case TypeCode.Single:
            case TypeCode.Double:
            case TypeCode.Decimal:
            case TypeCode.DateTime:
                return true;
        }

        if (type1 == typeof(DateTimeOffset))
        {
            return true;
        }

        if (type1.IsArray)
        {
            var element = type1.GetElementType();
            return element is not null && element != typeof(object) && IsContractType(element);
        }

        foreach (var candidate in Candidates(type1))
        {
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                var element = candidate.GetGenericArguments()[0];
                return element != typeof(object) && IsContractType(element);
            }
        }

        return false;
    }

    /// <summary>The type itself plus its interfaces, so a generic interface is recognised as one.</summary>
    private static IEnumerable<Type> Candidates(Type type)
    {
        yield return type;
        foreach (var @interface in type.GetInterfaces())
        {
            yield return @interface;
        }
    }

    private static void RequireContractType(string qualifiedName, string what, Type type, string parameterName)
    {
        if (IsContractType(type))
        {
            return;
        }

        throw new ArgumentException(
            $"Template function '{qualifiedName}' has an unsupported {what} ('{type.Name}'). Allowed are " +
            "string, bool, numeric, decimal, DateTime, DateTimeOffset and IEnumerable of those " +
            "(the engine never exposes reflection, delegates or arbitrary host types to a template).",
            parameterName);
    }

    private static bool IsAwaitable(Type type)
        => typeof(Task).IsAssignableFrom(type) ||
           type == typeof(ValueTask) ||
           (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>));

    private static string Qualify(string @namespace, string name) => $"{@namespace}.{name}";

    private static TemplateSecurityViolationException Refuse(
        TemplateSecurityEventKind kind,
        string reason,
        string detail)
        => new(kind, $"{reason} ({detail})");
}

using System.Collections;
using Scriban;
using Scriban.Runtime;
using Scriban.Syntax;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Templating;

/// <summary>
/// A provider function as the template sees it. Scriban only invokes a value that already implements
/// <see cref="IScriptCustomFunction"/>, so the host delegate is wrapped with
/// <see cref="DynamicCustomFunction"/>, and every result is materialized and re-checked before it can
/// reach the template:
/// <list type="bullet">
/// <item>reflection handles, host code, values of a forbidden namespace and the engine's escaping markers
/// fail the render closed and are audited;</item>
/// <item>an enumerable is turned into a <see cref="ScriptArray"/> of scalars, so a host type that happens
/// to be enumerable cannot be read through the template (<c>{{ (app.leaky).password }}</c>); the
/// enumeration is bounded by the context's loop limit;</item>
/// <item>an exception thrown by the delegate never leaks its message into the render error — the
/// template only learns the function name, the host gets the detail through the logger.</item>
/// </list>
/// </summary>
internal sealed class SandboxedTemplateFunction : IScriptCustomFunction
{
    private readonly IScriptCustomFunction _inner;
    private readonly string _qualifiedName;
    private readonly Action<string, Exception>? _onFailure;

    public SandboxedTemplateFunction(
        string @namespace,
        string name,
        Delegate function,
        Action<string, Exception>? onFailure = null)
    {
        _qualifiedName = $"{@namespace}.{name}";
        _inner = DynamicCustomFunction.Create(function);
        _onFailure = onFailure;
    }

    /// <inheritdoc />
    public object? Invoke(
        TemplateContext context,
        ScriptNode? callerContext,
        ScriptArray arguments,
        ScriptBlockStatement? blockStatement)
        => Materialize(
            context,
            Call(() => _inner.Invoke(context, callerContext, arguments, blockStatement), callerContext));

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(
        TemplateContext context,
        ScriptNode? callerContext,
        ScriptArray arguments,
        ScriptBlockStatement? blockStatement)
    {
        object? result = null;
        await CallAsync(
            async () => result = await _inner.InvokeAsync(context, callerContext, arguments, blockStatement)
                .ConfigureAwait(false),
            callerContext).ConfigureAwait(false);

        return Materialize(context, result);
    }

    /// <inheritdoc />
    public int RequiredParameterCount => _inner.RequiredParameterCount;

    /// <inheritdoc />
    public int ParameterCount => _inner.ParameterCount;

    /// <inheritdoc />
    public ScriptVarParamKind VarParamKind => _inner.VarParamKind;

    /// <inheritdoc />
    public Type ReturnType => _inner.ReturnType;

    /// <inheritdoc />
    public ScriptParameterInfo GetParameterInfo(int index) => _inner.GetParameterInfo(index);

    /// <summary>
    /// Runs the delegate. A sandbox refusal, a render cancellation and Scriban's own runtime errors pass
    /// through; anything else becomes a generic <see cref="ScriptRuntimeException"/> that names the
    /// function only — a host exception message could carry a connection string, a path or a value.
    /// </summary>
    private object? Call(Func<object?> call, ScriptNode? callerContext)
    {
        try
        {
            return call();
        }
        catch (Exception ex) when (ex is not TemplateSecurityViolationException
                                       and not OperationCanceledException
                                       and not ScriptRuntimeException)
        {
            _onFailure?.Invoke(_qualifiedName, ex);
            throw Failure(callerContext, ex);
        }
    }

    private async Task CallAsync(Func<Task> call, ScriptNode? callerContext)
    {
        try
        {
            await call().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not TemplateSecurityViolationException
                                       and not OperationCanceledException
                                       and not ScriptRuntimeException)
        {
            _onFailure?.Invoke(_qualifiedName, ex);
            throw Failure(callerContext, ex);
        }
    }

    private ScriptRuntimeException Failure(ScriptNode? callerContext, Exception ex)
        => new(callerContext?.Span ?? default, $"Template function '{_qualifiedName}' failed.", ex);

    /// <summary>
    /// Turns an enumerable result into a script array of scalars (recursively), so no host object graph
    /// survives into the template. The walk is bounded by the context's loop and recursion limits.
    /// </summary>
    private object? Materialize(TemplateContext context, object? value, int depth = 0)
    {
        if (depth > context.ObjectRecursionLimit)
        {
            throw new TemplateSecurityViolationException(
                TemplateSecurityEventKind.LimitExceeded,
                $"{_qualifiedName} result nesting");
        }

        value = TemplateFunctionResultPolicy.EnsureAllowed(value, _qualifiedName);
        if (value is null or string)
        {
            return value;
        }

        if (value is not IEnumerable enumerable)
        {
            return value;
        }

        var array = new ScriptArray();
        var count = 0;
        foreach (var item in enumerable)
        {
            if (++count > context.LoopLimit)
            {
                throw new TemplateSecurityViolationException(
                    TemplateSecurityEventKind.LimitExceeded,
                    $"LoopLimit ({_qualifiedName} enumeration)");
            }

            array.Add(Materialize(context, item, depth + 1));
        }

        return array;
    }
}

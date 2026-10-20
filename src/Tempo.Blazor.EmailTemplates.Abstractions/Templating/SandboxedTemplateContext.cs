using System.Reflection;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;
using Scriban.Syntax;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Templating;

/// <summary>
/// The <see cref="TemplateContext"/> every render runs in. It hardens Scriban with the rules that must
/// never be configurable by a host: an explicit standard member renamer, a member filter that refuses
/// reflection members and members declared on types from forbidden namespaces, no builtin that
/// evaluates a string as a template/script, no template loader, the limits from
/// <see cref="TemplateSecurityOptions"/>, a lookup probe that turns a refused access into a render
/// error instead of an empty string, and a cancellation token that aborts the render once the
/// wall-clock budget is spent — even when the template writes no output at all.
/// </summary>
internal sealed class SandboxedTemplateContext : TemplateContext, IDisposable
{
    /// <summary>Builtin functions removed from the sandbox, whatever else changes in Scriban.</summary>
    private static readonly string[] RemovedObjectBuiltins = ["eval", "eval_template"];

    /// <summary>Loader builtins removed from the sandbox: templates never include other templates.</summary>
    private static readonly string[] RemovedTopLevelBuiltins = ["include", "include_join"];

    /// <summary>
    /// Cancels the render when <see cref="TemplateSecurityOptions.Timeout"/> elapses. Scriban checks
    /// <see cref="TemplateContext.CancellationToken"/> on every loop step, so a loop that only assigns
    /// (and therefore never reaches the output-length/clock guard) is still bounded.
    /// </summary>
    private readonly CancellationTokenSource _cts;

    public SandboxedTemplateContext(TemplateSecurityOptions options)
        : base(StringComparer.Ordinal)
    {
        ArgumentNullException.ThrowIfNull(options);

        _cts = new CancellationTokenSource(options.Timeout);
        CancellationToken = _cts.Token;

        LoopLimit = options.LoopLimit;
        RecursiveLimit = options.RecursiveLimit;
        LimitToString = options.LimitToString;
        OnStringLimit = ScriptLimitBehavior.Throw;
        OutputLimit = options.MaxOutputLength;
        OnOutputLimit = ScriptLimitBehavior.Throw;
        ObjectRecursionLimit = options.ObjectRecursionLimit;
        // A regex can never outlive the whole render; the tighter of the two budgets wins.
        RegexTimeOut = options.RegexTimeOut < options.Timeout ? options.RegexTimeOut : options.Timeout;

        StrictVariables = options.StrictVariables;
        EnableRelaxedMemberAccess = !options.StrictVariables;
        // PostrionCore parity: a chain through a null/undefined target renders empty rather than
        // failing. A refused member never becomes null — the lookup probe throws.
        EnableRelaxedTargetAccess = !options.StrictVariables;

        // Explicit standard renamer (Name → name) and a member filter that is not host-configurable.
        MemberRenamer = StandardMemberRenamer.Default;
        MemberFilter = SandboxMemberPolicy.IsMemberAllowed;

        TryGetMember = GuardRefusedMember;

        RemoveUnsafeBuiltins();
        // No TemplateLoader is set on purpose: `include` fails instead of reading files.
    }

    /// <summary>
    /// Refuses to convert a value whose <c>ToString()</c>/text conversion would leak CLR metadata:
    /// reflection handles (<see cref="Type"/>, <see cref="MemberInfo"/>, <see cref="Assembly"/>,
    /// <see cref="Module"/>), host code (<see cref="Delegate"/>) and values of types from a forbidden
    /// namespace. Without this override <c>{{ t }}</c> with a <see cref="Type"/> model value prints
    /// <c>System.String</c>, a <see cref="FileInfo"/> prints its path and a task prints its type name.
    /// </summary>
    public override string? ObjectToString(object? value, bool nested = false)
    {
        if (value is not null)
        {
            var valueType = value.GetType();
            if (value is Delegate)
            {
                throw new TemplateSecurityViolationException(
                    TemplateSecurityEventKind.BlockedMember,
                    SandboxMemberPolicy.DescribeForbiddenType(valueType));
            }

            if (value is Type || value is MemberInfo || value is Assembly || value is Module ||
                SandboxMemberPolicy.IsForbiddenNamespace(valueType.Namespace))
            {
                throw new TemplateSecurityViolationException(
                    TemplateSecurityEventKind.BlockedNamespace,
                    SandboxMemberPolicy.DescribeForbiddenType(valueType));
            }
        }

        return base.ObjectToString(value, nested);
    }

    /// <summary>
    /// Wraps the accessor of every live CLR object so a delegate read off it is refused instead of
    /// invoked. Script objects are left untouched — the host function registry puts callable values
    /// there on purpose.
    /// </summary>
    protected override IObjectAccessor? GetMemberAccessorImpl(object target)
    {
        var accessor = base.GetMemberAccessorImpl(target);
        if (accessor is null || target is IScriptObject)
        {
            return accessor;
        }

        return new SandboxedObjectAccessor(accessor);
    }

    public void Dispose() => _cts.Dispose();

    private bool GuardRefusedMember(
        TemplateContext context,
        SourceSpan span,
        object target,
        string member,
        out object? value)
    {
        value = null;
        if (target is null)
        {
            return false;
        }

        var refusal = SandboxMemberPolicy.ClassifyFailedLookup(target.GetType(), member);
        if (refusal is null)
        {
            return false;
        }

        var kind = refusal.Value.Verdict == SandboxMemberVerdict.BlockedNamespace
            ? TemplateSecurityEventKind.BlockedNamespace
            : TemplateSecurityEventKind.BlockedMember;
        throw new TemplateSecurityViolationException(kind, refusal.Value.Detail);
    }

    private void RemoveUnsafeBuiltins()
    {
        if (BuiltinObject.TryGetValue("object", out var objectNamespace) && objectNamespace is ScriptObject functions)
        {
            foreach (var name in RemovedObjectBuiltins)
            {
                functions.Remove(name);
            }
        }

        foreach (var name in RemovedTopLevelBuiltins)
        {
            BuiltinObject.Remove(name);
        }
    }
}

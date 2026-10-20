using System.Globalization;
using System.Reflection;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;
using Scriban.Syntax;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Templating;

/// <summary>
/// The <see cref="TemplateContext"/> every render runs in. It hardens Scriban with the rules that must
/// never be configurable by a host: an explicit standard member renamer, a member filter that refuses
/// reflection members and members declared on types from forbidden namespaces, no builtin that
/// evaluates a string as a template/script, no template loader, the limits from
/// <see cref="TemplateSecurityOptions"/>, a lookup probe that turns a refused access into a render
/// error instead of an empty string, and a cancellation token that aborts the render once the
/// wall-clock budget is spent — even when the template writes no output at all.
/// It also owns HTML escaping (§3.7): in <see cref="TemplateEscapeMode.Html"/> the *output of every
/// expression* is encoded — literal template text, filters and the model are left untouched, so
/// <c>string.truncate</c> still measures the raw value and <c>html.escape</c> is not encoded twice.
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

    private readonly TemplateEscapeMode _escapeMode;

    public SandboxedTemplateContext(
        TemplateSecurityOptions options,
        TemplateEscapeMode escapeMode = TemplateEscapeMode.Html,
        TemplateFunctionRegistry? functions = null,
        CultureInfo? culture = null)
        : base(StringComparer.Ordinal)
    {
        ArgumentNullException.ThrowIfNull(options);

        _escapeMode = escapeMode;
        _cts = new CancellationTokenSource(options.Timeout);
        CancellationToken = _cts.Token;

        // Formatters must be deterministic: the engine always pushes an explicit culture (invariant
        // when the render did not ask for one), so the server's thread culture never leaks into output.
        if (culture is not null)
        {
            PushCulture(culture);
        }

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
        RegisterHtmlFunctions();
        RegisterFunctionNamespaces(functions);
        // No TemplateLoader is set on purpose: `include` fails instead of reading files.
    }

    /// <summary>
    /// Encodes the output of an expression. Scriban calls this overload — not
    /// <see cref="TemplateContext.Write(string)"/> — for expression results only, which is exactly the
    /// value boundary the contract draws: literal template text and the model itself stay untouched.
    /// </summary>
    public override TemplateContext Write(SourceSpan span, object? textAsObject)
    {
        switch (textAsObject)
        {
            // `{{ value | html.trusted }}`: the author asked for markup, so it is written without
            // encoding — the sanitizer decides which tags and attributes actually survive.
            case TrustedHtml trusted:
                return base.Write(span, HtmlContentSanitizer.Sanitize(trusted.Value));

            // `{{ value | html.escape }}`: already encoded, encoding it again would double-escape.
            case EscapedHtml escaped:
                return base.Write(span, escaped.Value);

            case null:
                return base.Write(span, textAsObject);

            default:
                if (_escapeMode == TemplateEscapeMode.None)
                {
                    return base.Write(span, textAsObject);
                }

                // Convert exactly as Scriban would (so number formatting keeps working) and encode the
                // resulting text — never the model value itself.
                return base.Write(span, HtmlTextEscaper.Encode(ObjectToString(textAsObject) ?? string.Empty));
        }
    }

    /// <summary>
    /// The asynchronous output path of <see cref="Write(SourceSpan, object?)"/>. Scriban's
    /// <c>RenderAsync</c> writes expression results through this overload, so an async host render is
    /// encoded exactly like the synchronous one — one implementation, no drift.
    /// </summary>
    public override ValueTask<TemplateContext> WriteAsync(SourceSpan span, object? textAsObject)
        => new(Write(span, textAsObject));

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

    /// <summary>
    /// Adds <c>html.trusted</c> and makes <c>html.escape</c> return already-encoded markup. The other
    /// <c>html</c> builtins (<c>strip</c>, <c>url_encode</c>, …) stay as Scriban ships them. The
    /// namespace is created when a future Scriban no longer ships one, so the opt-out filter can never
    /// silently disappear.
    /// </summary>
    private void RegisterHtmlFunctions()
    {
        if (BuiltinObject.TryGetValue(HtmlTemplateFunctions.Namespace, out var html) && html is ScriptObject functions)
        {
            HtmlTemplateFunctions.Register(functions);
            return;
        }

        functions = new ScriptObject();
        HtmlTemplateFunctions.Register(functions);
        BuiltinObject.SetValue(HtmlTemplateFunctions.Namespace, functions, readOnly: true);
    }

    /// <summary>
    /// Adds the host function namespaces (<see cref="ITemplateFunctionProvider"/>). The registry was
    /// validated when the engine was constructed, so a namespace can neither shadow a builtin nor
    /// another provider; the guard here is the last line of defence (fail closed rather than override).
    /// </summary>
    private void RegisterFunctionNamespaces(TemplateFunctionRegistry? functions)
    {
        if (functions is null || functions.IsEmpty)
        {
            return;
        }

        foreach (var @namespace in functions.NamespaceNames)
        {
            if (BuiltinObject.ContainsKey(@namespace))
            {
                throw new ArgumentException(
                    $"Template function namespace '{@namespace}' collides with a builtin namespace of the sandbox.",
                    nameof(functions));
            }

            BuiltinObject.SetValue(@namespace, functions.CreateScope(@namespace), readOnly: true);
        }
    }
}

using Tempo.Blazor.EmailTemplates.Abstractions.Templating;

namespace Tempo.Blazor.EmailTemplates;

/// <summary>
/// Options of the editor-only registration (<c>AddTempoEmailTemplateEditor</c>, §3.5).
/// </summary>
public sealed class EmailTemplateEditorOptions
{
    /// <summary>
    /// The defaults the editor started from. Kept so <see cref="ApplyTo"/> can hand over ONLY the values the
    /// host actually changed — a registration must never reset a limit another registration configured.
    /// </summary>
    private readonly TemplateSecurityOptions _defaults = new();

    /// <summary>
    /// Gets the sandbox limits shared with validation and variable extraction.
    /// </summary>
    /// <remarks>
    /// These are the limits of the one <see cref="TemplateSecurityOptions"/> instance the model registration
    /// owns; an engine registered later reads the same instance, so the editor and a local render always
    /// agree on the limits. The function namespaces of the registered
    /// <see cref="ITemplateFunctionProvider"/>s are added to
    /// <see cref="TemplateSecurityOptions.FunctionNamespaces"/> automatically — a host never fills them in
    /// by hand.
    /// </remarks>
    public TemplateSecurityOptions Security { get; } = new();

    /// <summary>
    /// Copies the values the host changed onto the shared instance the model registration owns. Values left
    /// at their defaults are NOT copied, so calling <c>AddTempoEmailTemplateEditor()</c> after a configured
    /// <c>AddTempoEmailTemplateEngine(o =&gt; …)</c> cannot silently undo that configuration.
    /// </summary>
    internal void ApplyTo(TemplateSecurityOptions target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (Security.LoopLimit != _defaults.LoopLimit) target.LoopLimit = Security.LoopLimit;
        if (Security.RecursiveLimit != _defaults.RecursiveLimit) target.RecursiveLimit = Security.RecursiveLimit;
        if (Security.MaxOutputLength != _defaults.MaxOutputLength) target.MaxOutputLength = Security.MaxOutputLength;
        if (Security.LimitToString != _defaults.LimitToString) target.LimitToString = Security.LimitToString;
        if (Security.ObjectRecursionLimit != _defaults.ObjectRecursionLimit) target.ObjectRecursionLimit = Security.ObjectRecursionLimit;
        if (Security.Timeout != _defaults.Timeout) target.Timeout = Security.Timeout;
        if (Security.RegexTimeOut != _defaults.RegexTimeOut) target.RegexTimeOut = Security.RegexTimeOut;
        if (Security.StrictVariables != _defaults.StrictVariables) target.StrictVariables = Security.StrictVariables;

        foreach (var @namespace in Security.FunctionNamespaces)
        {
            target.FunctionNamespaces.Add(@namespace);
        }
    }
}

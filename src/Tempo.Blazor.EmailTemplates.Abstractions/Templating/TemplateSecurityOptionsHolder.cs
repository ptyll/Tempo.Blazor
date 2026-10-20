namespace Tempo.Blazor.EmailTemplates.Abstractions.Templating;

/// <summary>
/// Holds the ONE <see cref="TemplateSecurityOptions"/> instance of a service collection (§3.0). The model
/// registration creates it and reuses it on every further call, so
/// <c>AddTempoEmailTemplateModel()</c> stays idempotent and <c>configure</c> always lands on the same
/// instance, whatever the order of the registrations. <c>TemplateSecurityOptions</c> itself is resolved
/// through a factory that enriches it with the function namespaces of the registered
/// <see cref="ITemplateFunctionProvider"/>s — that factory needs a way back to the instance without
/// recursing into its own service type.
/// </summary>
internal sealed class TemplateSecurityOptionsHolder(TemplateSecurityOptions options)
{
    public TemplateSecurityOptions Options { get; } = options;
}

/// <summary>
/// The narrow view of the template-function registry an <em>optional</em> package can expose to the model
/// registration. The model must fill <see cref="TemplateSecurityOptions.FunctionNamespaces"/> when the
/// engine package is present, without taking a dependency on it (D11): the registry implements this
/// internal interface, and with no registry registered there is simply nothing to add.
/// </summary>
internal interface ITemplateFunctionNamespaceSource
{
    /// <summary>Gets the validated namespaces of the registered function providers.</summary>
    IReadOnlyCollection<string> NamespaceNames { get; }
}

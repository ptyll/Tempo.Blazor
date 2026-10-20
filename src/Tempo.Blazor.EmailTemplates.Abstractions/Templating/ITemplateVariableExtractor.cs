namespace Tempo.Blazor.EmailTemplates.Abstractions.Templating;

/// <summary>
/// Extracts the variable paths a template references (§3.0). The editor's variable picker and sample
/// data work with <see cref="RegexTemplateVariableExtractor"/> (BCL baseline, this package) even when
/// the Engine package is absent; <c>ScribanTemplateVariableExtractor</c> in
/// <c>Tempo.Blazor.EmailTemplates.Engine</c> is the authoritative implementation (it understands
/// filters and loop-local variables). The difference is only how many variables the picker detects —
/// never a security decision.
/// </summary>
public interface ITemplateVariableExtractor
{
    /// <summary>Returns the distinct variable paths referenced by the template.</summary>
    IReadOnlyList<string> Extract(string template);

    /// <summary>
    /// Returns the distinct variables referenced by the template, each marked as a scalar or a collection
    /// (when it is used as a <c>for</c> iterator).
    /// </summary>
    IReadOnlyList<TemplateVariableInfo> ExtractInfos(string template);
}

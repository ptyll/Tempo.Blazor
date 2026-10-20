namespace Tempo.Blazor.EmailTemplates.Abstractions.Templating;

/// <summary>
/// Validates template syntax without rendering (§3.0). It is the only engine capability the validation
/// panel needs, so the editor no longer depends on <see cref="ITemplateEngine"/>: with the Engine package
/// registered the DI container resolves <c>ScribanTemplateValidator</c>; without it a host may register
/// its own server-side validator (the syntax check then runs where the engine runs). When nothing is
/// registered the panel degrades to a localized explanation that names the missing registration — never a
/// silent no-op and never an empty panel.
/// </summary>
public interface ITemplateValidator
{
    /// <summary>Validates a template's syntax without rendering it.</summary>
    TemplateValidationResult Validate(string template);
}

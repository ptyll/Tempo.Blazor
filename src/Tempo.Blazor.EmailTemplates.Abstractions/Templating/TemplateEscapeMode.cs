namespace Tempo.Blazor.EmailTemplates.Abstractions.Templating;

/// <summary>
/// How the output of an expression is encoded while a template renders. Deliberately <c>internal</c>
/// (D14): the renderer picks the mode per pass — <see cref="Html"/> for the MJML/HTML body,
/// <see cref="None"/> for the subject, preheader and text version — so a host has no switch that could
/// turn escaping off. The only opt-out available to a template author is
/// <c>{{ value | html.trusted }}</c>, whose output always passes the HTML sanitizer.
/// </summary>
internal enum TemplateEscapeMode
{
    /// <summary>Expression output is emitted verbatim (subject, preheader, plain-text version).</summary>
    None = 0,

    /// <summary>Expression output is HTML/XML-encoded before it reaches the markup (MJML/HTML body).</summary>
    Html = 1,
}

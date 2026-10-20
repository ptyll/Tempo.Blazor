using System.Globalization;
using Scriban.Runtime;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Templating;

/// <summary>
/// The <c>html</c> namespace entries the sandbox registers on top of Scriban's builtins.
/// <c>trusted</c> is the single opt-out from automatic escaping (its output is sanitized before it is
/// written); <c>escape</c> replaces the builtin so an explicitly encoded value is not encoded a second
/// time when the engine escapes expression output.
/// </summary>
internal static class HtmlTemplateFunctions
{
    /// <summary>Name of the namespace holding the HTML filters.</summary>
    public const string Namespace = "html";

    /// <summary>Filter that emits markup without encoding it (after sanitizing it).</summary>
    public const string Trusted = "trusted";

    /// <summary>Filter that HTML-encodes a value explicitly.</summary>
    public const string Escape = "escape";

    /// <summary>Installs (or replaces) the namespace members on the per-context builtin object.</summary>
    public static void Register(ScriptObject htmlFunctions)
    {
        ArgumentNullException.ThrowIfNull(htmlFunctions);

        // Scriban only calls a value that already is an IScriptCustomFunction, so the delegates are
        // wrapped explicitly (a bare Delegate in a script object is "not a valid function").
        htmlFunctions.SetValue(
            Escape,
            DynamicCustomFunction.Create(
                new Func<object?, EscapedHtml>(value => new EscapedHtml(HtmlTextEscaper.Encode(ToText(value))))),
            readOnly: true);
        htmlFunctions.SetValue(
            Trusted,
            DynamicCustomFunction.Create(new Func<object?, TrustedHtml>(value => new TrustedHtml(ToText(value)))),
            readOnly: true);
    }

    private static string ToText(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        EscapedHtml escaped => escaped.Value,
        TrustedHtml trusted => trusted.Value,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}

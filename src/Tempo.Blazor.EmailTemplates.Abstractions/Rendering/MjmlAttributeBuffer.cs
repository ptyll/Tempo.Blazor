using System.Text;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// Accumulates the attributes of ONE generated MJML element, escaping values, omitting defaults, refusing the
/// values the message cannot carry, and finally rendering them as the attribute portion of the opening tag.
/// </summary>
/// <remarks>
/// Every path into this buffer — a model-driven attribute, a preserved extra attribute, a per-component default
/// of <c>mj-attributes</c> — is judged by <see cref="MjmlKnownAttributes"/>: the name has to be a known attribute
/// of the element's tag, and the value has to be of the kind the name promises (a URL through
/// <see cref="EmailUrlPolicy"/>, a colour through <see cref="CssColourValue"/>, a style-bound value through
/// <see cref="CssStyleValue"/>). A refused attribute is DROPPED (never "cleaned up") and reported to the render's
/// diagnostics, so the loss is visible instead of silent. A duplicate name is dropped too: two attributes of the
/// same name make the export unparsable XML and let the last one win in a lenient parser.
/// </remarks>
internal sealed class MjmlAttributeBuffer
{
    private readonly List<(string Name, string Value)> _items = [];
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);
    private readonly string? _tag;
    private readonly ICollection<RenderError>? _diagnostics;

    /// <summary>Initializes a buffer for the attributes of <paramref name="tag"/>.</summary>
    /// <param name="tag">The MJML element name the attributes belong to (for the attribute allow-list).</param>
    /// <param name="diagnostics">Optional collector for the values that had to be dropped.</param>
    public MjmlAttributeBuffer(string? tag = null, ICollection<RenderError>? diagnostics = null)
    {
        _tag = tag;
        _diagnostics = diagnostics;
    }

    /// <summary>Adds an optional attribute; skipped when the value is null or empty.</summary>
    public MjmlAttributeBuffer Optional(string name, string? value)
    {
        if (!string.IsNullOrEmpty(value)) Add(name, value);
        return this;
    }

    /// <summary>Adds an attribute only when it differs from its MJML default value.</summary>
    public MjmlAttributeBuffer Defaulted(string name, string value, string? defaultValue)
    {
        if (!string.Equals(value, defaultValue, StringComparison.Ordinal)) Add(name, value);
        return this;
    }

    /// <summary>Adds a verbatim attribute (the value still goes through the same rules and escaping).</summary>
    public MjmlAttributeBuffer Raw(string name, string value)
    {
        Add(name, value);
        return this;
    }

    /// <summary>Adds a boolean flag attribute (e.g. <c>full-width="full-width"</c>) when set.</summary>
    public MjmlAttributeBuffer Flag(string name, bool value)
    {
        if (value) Add(name, name);
        return this;
    }

    /// <summary>
    /// Adds a URL-bearing attribute through the pipeline's single allow-list (§3.7a, D15): a blocked scheme
    /// (for example <c>javascript:</c>) is emitted as <c>#</c>, never as the value the model carried. This is
    /// the MJML counterpart of the sanitizer — without it the exported MJML would still carry a live
    /// <c>javascript:</c> link even though the compiled HTML replaces it.
    /// </summary>
    public MjmlAttributeBuffer Url(string name, string? value)
    {
        if (!string.IsNullOrEmpty(value)) Add(name, SafeUrl(value));
        return this;
    }

    /// <summary>
    /// Adds a COLOUR attribute only when the value is a colour the compiler may copy into the message's
    /// <c>style</c> (<see cref="CssColourValue"/>). Anything else is dropped: a value carrying <c>;</c>, a quote
    /// or a <c>url(…)</c> would add a declaration of its own, and there is no message this pipeline renders
    /// that could carry it.
    /// </summary>
    public MjmlAttributeBuffer Colour(string name, string? value, string? defaultValue = null)
    {
        if (string.IsNullOrEmpty(value)) return this;
        if (defaultValue is not null && string.Equals(value, defaultValue, StringComparison.Ordinal)) return this;
        if (!CssColourValue.IsPlausible(value))
        {
            Report(name, "a colour");
            return this;
        }

        Add(name, value.Trim());
        return this;
    }

    /// <summary>Adds <c>css-class</c>, <c>mj-class</c> and the preserved extra attributes.</summary>
    /// <remarks>
    /// The extra attributes come from an IMPORT (an unknown MJML attribute of a foreign template) or from a
    /// hostile JSON payload, and none of them is a value the editor ever authored. Emitting them verbatim used
    /// to re-emit <c>onclick</c>, <c>style</c> and a <c>href="javascript:…"</c> into the export, so every entry
    /// is filtered here: the name has to be a known attribute of THIS tag (or an inert <c>data-*</c>), a
    /// URL-bearing name goes through the URL allow-list, a colour has to be a colour, a style-bound value has to
    /// survive <see cref="CssStyleValue"/>, and any other value may not open a CSS declaration. Anything filtered
    /// out is dropped — a lossy round-trip of an attribute no template can use.
    /// </remarks>
    public MjmlAttributeBuffer Common(string? cssClass, IList<string> mjClasses, IDictionary<string, string> extra)
    {
        Optional("css-class", MjmlKnownAttributes.SafeClassList(cssClass));
        var classes = MjmlKnownAttributes.SafeClassList(string.Join(' ', mjClasses));
        if (!string.IsNullOrEmpty(classes)) Raw("mj-class", classes);
        foreach (var (name, value) in extra)
        {
            if (!MjmlKnownAttributes.IsAllowed(_tag, name))
            {
                continue;
            }

            if (MjmlKnownAttributes.IsUrl(name))
            {
                Url(name, value);
                continue;
            }

            if (!MjmlKnownAttributes.IsSafeValue(name, value))
            {
                Report(name, MjmlKnownAttributes.IsColour(name) ? "a colour" : "a style value");
                continue;
            }

            Add(name, value);
        }

        return this;
    }

    /// <summary>Adds the padding shorthand/sides and container background shared by blocks.</summary>
    public MjmlAttributeBuffer BlockCommon(EmailBlockBase block, string? defaultPadding)
    {
        Defaulted("padding", block.Padding ?? string.Empty, defaultPadding ?? string.Empty);
        Optional("padding-top", block.PaddingTop);
        Optional("padding-right", block.PaddingRight);
        Optional("padding-bottom", block.PaddingBottom);
        Optional("padding-left", block.PaddingLeft);
        Colour("container-background-color", block.ContainerBackgroundColor);
        Common(block.CssClass, block.MjClasses, block.ExtraAttributes);
        return this;
    }

    /// <summary>The rendered attribute portion of the opening tag (each pair preceded by a space).</summary>
    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (var (name, value) in _items)
            sb.Append(' ').Append(name).Append("=\"").Append(value).Append('"');
        return sb.ToString();
    }

    /// <summary>
    /// The single gate every attribute passes: a known name of the tag, an emittable value, no duplicate, and a
    /// double-quoted, escape-safe rendering.
    /// </summary>
    private void Add(string name, string value)
    {
        if (!MjmlKnownAttributes.IsAllowed(_tag, name))
        {
            return;
        }

        if (MjmlKnownAttributes.IsUrl(name))
        {
            value = SafeUrl(value);
        }
        else if (MjmlKnownAttributes.IsColour(name))
        {
            if (!CssColourValue.IsPlausible(value))
            {
                Report(name, "a colour");
                return;
            }

            value = value.Trim();
        }
        else if (MjmlKnownAttributes.IsStyleBound(name))
        {
            if (!CssStyleValue.IsPlausible(value, name))
            {
                Report(name, "a style value");
                return;
            }
        }
        else if (!MjmlKnownAttributes.IsSafeToken(value))
        {
            Report(name, "an attribute value");
            return;
        }

        // A duplicate attribute would make the export invalid XML and let a lenient parser take the LAST value,
        // so the first value (the model's own) always wins.
        if (!_names.Add(name)) return;

        _items.Add((name, MjmlEscape.Attribute(value)));
    }

    /// <summary>Records a dropped value; only the NAME is reported (the value may be crafted markup).</summary>
    private void Report(string name, string kind)
    {
        _diagnostics?.Add(new RenderError(
            $"The attribute '{name}' was dropped because its value is not {kind} the message can carry.",
            Key: RenderKeys.InvalidAttributeValue,
            Severity: RenderErrorSeverity.Warning));
    }

    /// <summary>A blocked URL becomes <c>#</c>; an allowed one is emitted as it is.</summary>
    private static string SafeUrl(string url) => EmailUrlPolicy.IsAllowed(url) ? url : EmailUrlPolicy.BlockedUrlPlaceholder;

}

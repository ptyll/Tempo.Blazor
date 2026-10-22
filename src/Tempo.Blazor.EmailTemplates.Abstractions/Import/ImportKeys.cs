namespace Tempo.Blazor.EmailTemplates.Abstractions.Import;

/// <summary>Localization keys for import diagnostics.</summary>
public static class ImportKeys
{
    /// <summary>Input was empty.</summary>
    public const string Empty = "import.empty";

    /// <summary>The markup could not be parsed as XML.</summary>
    public const string ParseError = "import.parse_error";

    /// <summary>The root element was not <c>mjml</c>.</summary>
    public const string NotMjml = "import.not_mjml";

    /// <summary>An unknown element was preserved as a raw block.</summary>
    public const string UnknownElement = "import.unknown_element";

    /// <summary>A body-level wrapper's sections were hoisted to the top level.</summary>
    public const string WrapperFlattened = "import.wrapper_flattened";

    /// <summary>A body- or section-level element was wrapped in a column to fit the model.</summary>
    public const string ElementWrapped = "import.element_wrapped";

    /// <summary>An <c>mj-include</c> could not be resolved.</summary>
    public const string IncludeUnresolved = "import.include_unresolved";

    /// <summary>The input is larger than <see cref="EmailImportOptions.MaxInputBytes"/> (UTF-8 bytes).</summary>
    public const string InputTooLarge = "import.input_too_large";

    /// <summary>
    /// A table cell's link used a scheme outside the URL allow-list (§3.7a). The value is kept in the
    /// model so the editor can show and fix it; every emission path (generator, text version, renderer)
    /// fail-closes it.
    /// </summary>
    public const string BlockedTableLink = "import.table.blocked_link";

    /// <summary>
    /// A table cell's <c>bgcolor</c> was outside the colour allow-list
    /// (<see cref="Rendering.EmailColorPolicy"/>) and was dropped from the model, because no message this
    /// pipeline renders could carry it.
    /// </summary>
    public const string InvalidTableColor = "import.table.invalid_color";

    /// <summary>
    /// Any other block's link (button, image, navbar link, social element, carousel image, hero/wrapper/section
    /// background, <c>mj-font</c>) used a scheme outside the URL allow-list (§3.7a). Same contract as
    /// <see cref="BlockedTableLink"/>: the value survives for editing and every emission path fail-closes it to
    /// <c>#</c>. The message carries the refused SCHEME, never the URL.
    /// </summary>
    public const string BlockedLink = "import.blocked_link";

    /// <summary>
    /// A colour value on a block or container is not a CSS colour, so the message cannot carry it (it is
    /// written into a style). The message carries the PROPERTY name, never the value.
    /// </summary>
    public const string InvalidColor = "import.invalid_color";

    /// <summary>
    /// An imported attribute was dropped because the pipeline may never write it: an event handler
    /// (<c>on*</c>), <c>style</c>, <c>srcdoc</c>, an attribute this MJML component does not know, a value that
    /// would open a CSS declaration of its own (<c>x;background:url(…)</c>) or a URL with a refused scheme. The
    /// detail is the TAG and the ATTRIBUTE NAME, never the value (which may carry crafted markup).
    /// </summary>
    public const string DroppedAttribute = "import.dropped_attribute";
}

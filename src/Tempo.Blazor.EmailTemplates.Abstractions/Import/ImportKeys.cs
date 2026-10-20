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
}

namespace Tempo.Blazor.EmailTemplates.Abstractions.Templating;

/// <summary>
/// Marker for markup that is already HTML-encoded (the value the <c>html.escape</c> filter returns).
/// Writing it verbatim keeps the filter's contract single-encoded instead of encoding the entities
/// again.
/// </summary>
internal sealed class EscapedHtml(string value)
{
    /// <summary>Gets the already encoded text.</summary>
    public string Value { get; } = value;

    /// <inheritdoc />
    public override string ToString() => Value;
}

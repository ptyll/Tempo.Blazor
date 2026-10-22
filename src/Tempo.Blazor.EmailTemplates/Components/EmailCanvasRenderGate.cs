using System.Globalization;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;

namespace Tempo.Blazor.EmailTemplates.Components;

/// <summary>
/// The re-render gate of ONE block renderer (Fáze 9A performance requirement).
/// <para>
/// The editor mutates the document IN PLACE (the inspector writes a property on the live block and then
/// commits), so neither object identity nor a document-wide revision can answer "which block changed":
/// the reference stays the same and a revision changes for every block. This gate compares the block's
/// rendered-data signature (<see cref="EmailBlockSignature"/>) plus the few render inputs that are not part
/// of the block (image-loading mode, nested flag, UI culture) and answers whether the renderer may rebuild
/// its subtree.
/// </para>
/// <para>
/// It is a separate object on purpose: the decision is the measured contract of the phase, so it is
/// testable on its own (<c>TmEmailCanvasPerformanceTests</c>) without depending on how a test renderer
/// treats <c>ShouldRender</c>. The cost of one decision is O(the block's own data) — no DOM diff, no render
/// tree, no sanitizer work.
/// </para>
/// </summary>
internal sealed class EmailCanvasRenderGate
{
    private string? _signature;
    private CanvasImageLoading? _imageLoading;
    private bool? _nested;
    private string? _culture;

    /// <summary>How many times the gate allowed a render (the first decision always does).</summary>
    public long Renders { get; private set; }

    /// <summary>The last signature the gate accepted, for diagnostics and tests.</summary>
    public string? Signature => _signature;

    /// <summary>
    /// Answers whether the renderer must rebuild: <see langword="true"/> when the block's painted values or
    /// one of the render inputs changed since the last accepted render.
    /// </summary>
    public bool ShouldRender(EmailBlockBase block, CanvasImageLoading imageLoading, bool nested, CultureInfo culture)
    {
        var signature = EmailBlockSignature.Of(block);
        var cultureName = culture.Name;
        if (string.Equals(signature, _signature, StringComparison.Ordinal)
            && _imageLoading == imageLoading
            && _nested == nested
            && string.Equals(cultureName, _culture, StringComparison.Ordinal))
        {
            return false;
        }

        _signature = signature;
        _imageLoading = imageLoading;
        _nested = nested;
        _culture = cultureName;
        Renders++;
        return true;
    }
}

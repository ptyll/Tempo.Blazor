namespace Tempo.Blazor.Components.Diagram;

/// <summary>
/// DiagramEditor-specific icon names. Only names that resolve inside the core
/// built-in <c>TmIcon</c> set (no new core icons are introduced by this package):
/// <c>files</c> is the two-overlapping-pages glyph used for duplicate actions,
/// keeping the single-page <c>copy</c> glyph unambiguous for plain copy.
/// </summary>
internal static class DiagramEditorIconNames
{
    /// <summary>Two overlapping pages — duplicate (page or node) actions.</summary>
    public const string Duplicate = "files";
}

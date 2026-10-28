using Tempo.Blazor.Components.Icons;

namespace Tempo.Blazor.Components.Spreadsheet;

/// <summary>
/// The Spreadsheet package's own stroke icons. They live in the shared <see cref="IconRegistry"/>
/// (never as Unicode box glyphs in markup) and are reusable by the ribbon, which still paints its own
/// border glyphs until the phase-5 toolbar rebuild.
/// </summary>
internal static class SpreadsheetIcons
{
    private static bool _registered;
    private static readonly object Gate = new();

    /// <summary>Idempotent: the registry overwrites the same keys, so a second call is harmless.</summary>
    internal static void EnsureRegistered()
    {
        if (_registered)
        {
            return;
        }

        lock (Gate)
        {
            if (_registered)
            {
                return;
            }

            IconRegistry.Register("tm-ss-border-none",
                "<rect x='3' y='3' width='18' height='18' rx='2' stroke-dasharray='3 3' opacity='0.5'/>");
            IconRegistry.Register("tm-ss-border-outside",
                "<rect x='3' y='3' width='18' height='18' rx='2'/><rect x='8' y='8' width='8' height='8' opacity='0.35'/>");
            IconRegistry.Register("tm-ss-border-all",
                "<rect x='3' y='3' width='18' height='18' rx='2'/><line x1='3' y1='12' x2='21' y2='12'/><line x1='12' y1='3' x2='12' y2='21'/>");
            IconRegistry.Register("tm-ss-border-thick",
                "<rect x='3' y='3' width='18' height='18' rx='2' stroke-width='5'/>");
            IconRegistry.Register("tm-ss-border-top",
                "<rect x='3' y='3' width='18' height='18' rx='2' opacity='0.35'/><line x1='3' y1='5' x2='21' y2='5' stroke-width='4'/>");
            IconRegistry.Register("tm-ss-border-bottom",
                "<rect x='3' y='3' width='18' height='18' rx='2' opacity='0.35'/><line x1='3' y1='19' x2='21' y2='19' stroke-width='4'/>");
            IconRegistry.Register("tm-ss-border-left",
                "<rect x='3' y='3' width='18' height='18' rx='2' opacity='0.35'/><line x1='5' y1='3' x2='5' y2='21' stroke-width='4'/>");
            IconRegistry.Register("tm-ss-border-right",
                "<rect x='3' y='3' width='18' height='18' rx='2' opacity='0.35'/><line x1='19' y1='3' x2='19' y2='21' stroke-width='4'/>");
            _registered = true;
        }
    }
}

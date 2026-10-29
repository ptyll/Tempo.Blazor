using Tempo.Blazor.Components.Icons;

namespace Tempo.Blazor.Components.Spreadsheet;

/// <summary>
/// The Spreadsheet package's own stroke icons. They live in the shared <see cref="IconRegistry"/>
/// (never as Unicode box glyphs in markup) and are used by the ribbon: cell borders, vertical cell
/// alignment and the decimal steppers have no lucide equivalent, so the package registers them once
/// (idempotently) and paints every one of them through <c>TmIcon</c>.
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

            // ── Cell borders ──
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
            IconRegistry.Register("tm-ss-border-thick-bottom",
                "<rect x='3' y='3' width='18' height='18' rx='2' opacity='0.35'/><line x1='3' y1='18' x2='21' y2='18' stroke-width='5'/>");
            IconRegistry.Register("tm-ss-border-double-bottom",
                "<rect x='3' y='3' width='18' height='18' rx='2' opacity='0.35'/><line x1='3' y1='17' x2='21' y2='17' stroke-width='2'/><line x1='3' y1='20' x2='21' y2='20' stroke-width='2'/>");
            IconRegistry.Register("tm-ss-border-top-thick-bottom",
                "<rect x='3' y='3' width='18' height='18' rx='2' opacity='0.35'/><line x1='3' y1='4' x2='21' y2='4' stroke-width='2'/><line x1='3' y1='19' x2='21' y2='19' stroke-width='5'/>");

            // ── Vertical cell alignment (lucide ships no "content at the top of a cell" glyph) ──
            IconRegistry.Register("tm-ss-align-top",
                "<rect x='3' y='3' width='18' height='18' rx='2' opacity='0.35'/><line x1='6' y1='7' x2='18' y2='7'/><line x1='6' y1='10' x2='18' y2='10'/><line x1='6' y1='13' x2='14' y2='13'/>");
            IconRegistry.Register("tm-ss-align-middle",
                "<rect x='3' y='3' width='18' height='18' rx='2' opacity='0.35'/><line x1='6' y1='9' x2='18' y2='9'/><line x1='6' y1='12' x2='18' y2='12'/><line x1='6' y1='15' x2='14' y2='15'/>");
            IconRegistry.Register("tm-ss-align-bottom",
                "<rect x='3' y='3' width='18' height='18' rx='2' opacity='0.35'/><line x1='6' y1='12' x2='18' y2='12'/><line x1='6' y1='15' x2='18' y2='15'/><line x1='6' y1='18' x2='14' y2='18'/>");

            // ── Decimal places ──
            IconRegistry.Register("tm-ss-decimal-increase",
                "<line x1='9' y1='4' x2='21' y2='4'/><line x1='9' y1='10' x2='21' y2='10'/><line x1='9' y1='16' x2='21' y2='16'/><circle cx='4' cy='4' r='1'/><circle cx='4' cy='10' r='1'/><circle cx='4' cy='16' r='1'/><line x1='16' y1='19' x2='22' y2='19'/><line x1='19' y1='16' x2='19' y2='22'/>");
            IconRegistry.Register("tm-ss-decimal-decrease",
                "<line x1='9' y1='4' x2='21' y2='4'/><line x1='9' y1='10' x2='21' y2='10'/><line x1='9' y1='16' x2='21' y2='16'/><circle cx='4' cy='4' r='1'/><circle cx='4' cy='10' r='1'/><circle cx='4' cy='16' r='1'/><line x1='16' y1='19' x2='22' y2='19'/>");

            _registered = true;
        }
    }
}

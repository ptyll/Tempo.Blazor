using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Fáze 10d: a content-height MODAL bottom sheet (<c>tm-sheet--content</c>) is capped by
/// <c>--tm-sheet-max</c> (default 0.85 of the viewport). <c>max-block-size</c> and <c>max-height</c> are the SAME
/// property in a horizontal writing mode, so the drawer's own <c>.tm-drawer--bottom .tm-drawer__panel { max-block-size:
/// 100dvh }</c> (0,2,0) beat the shared <c>.tm-sheet__panel { max-height: calc(var(--tm-sheet-max) * …) }</c> (0,1,0):
/// a long form filled the whole viewport and the cap never applied. The drawer stylesheet has to restate the cap at a
/// specificity that wins over the 100dvh ceiling.
/// </summary>
public sealed class DrawerSheetMaxHeightCssTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static string Css()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
            dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "src", "Tempo.Blazor", "wwwroot", "css", "components", "_drawer.css"));
    }

    [Fact]
    public void ModalBottomSheetPanel_RestatesTheSheetMaxCap_AboveTheDvhCeiling()
    {
        var css = Css();

        // Selector: bottom drawer + .tm-sheet + panel + .tm-sheet__panel = 4 classes (0,4,0), above the 0,2,0 ceiling.
        Regex.IsMatch(css,
                @"\.tm-drawer--bottom\.tm-sheet\s+\.tm-drawer__panel\.tm-sheet__panel\s*\{[^}]*max-height:\s*calc\(var\(--tm-sheet-max,\s*0\.85\)\s*\*\s*var\(--tm-sheet-viewport,\s*100dvh\)\)",
                RegexOptions.None, RegexTimeout)
            .Should().BeTrue("the modal sheet's cap (--tm-sheet-max × the visible viewport) must out-specify `.tm-drawer--bottom .tm-drawer__panel { max-block-size: 100dvh }`");
    }

    [Fact]
    public void ModalCap_IsDeclaredBefore_TheInlineSheetCap_SoTheInlineSheetStillMeasuresItsHost()
    {
        var css = Css();

        var modal = css.IndexOf(".tm-drawer--bottom.tm-sheet .tm-drawer__panel.tm-sheet__panel", StringComparison.Ordinal);
        var inline = css.IndexOf(".tm-drawer--inline.tm-drawer--bottom .tm-drawer__panel.tm-sheet__panel", StringComparison.Ordinal);

        modal.Should().BeGreaterThan(0);
        inline.Should().BeGreaterThan(0);
        modal.Should().BeLessThan(inline,
            "both rules are 0,4,0: the inline one (a fraction of its HOST, `* 100%`) must come later and win for `Modal=false`");
    }
}

using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Spreadsheet;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.Spreadsheet;

/// <summary>
/// Redesign phase 7d — the CONTEXT MENU owns the focus while it is open.
/// <para>
/// Since phase 6 the grid's context menu is HOST-owned: the canvas raises
/// <see cref="TmSpreadsheetCanvasGrid.OnCanvasContextMenu"/>, the grid sets its active cell (which arms
/// <c>_shouldFocusAfterRender</c>) and <c>TmSpreadsheet</c> renders <c>TmSpreadsheetContextMenu</c> as a
/// SIBLING, whose <c>OnAfterRenderAsync</c> focuses the first entry. Both components then run their
/// <c>OnAfterRenderAsync</c> in the same batch, and the grid's restore issues an async
/// <c>isFocusOutside</c> JS call BEFORE the menu focuses — so its answer is stale by the time the
/// <c>FocusAsync</c> hop arrives and the caret lands back in the grid ~25 ms after the menu took it. The
/// keyboard walk (ArrowDown / Home / End / Escape) was dead in a real browser while every DOM assertion
/// still passed. The fix is explicit precedence: the host tells the grid to stand down while one of its
/// menus is open.
/// </para>
/// </summary>
[Collection("SpreadsheetClipboard")]
public class TmSpreadsheetContextMenuFocusTests : LocalizationTestBase
{
    /// <summary>"ElementReference.FocusAsync" surfaces in bUnit's Loose JSInterop under this identifier.</summary>
    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";

    /// <summary>
    /// The ATOMIC guard the grid uses instead of probe-then-focus (phase 7d): one JS call that focuses the
    /// root only when the focus has not moved into another control, so a surface that took the focus in
    /// between cannot lose it again.
    /// </summary>
    private const string GuardedFocusInvocation = "tmSpreadsheetCanvas.focusGridUnlessFocusMovedAway";

    private const string ContextMenuIdAttribute = "data-context-menu-id";

    [Fact]
    public async Task WhileTheContextMenuIsOpen_TheGridStandsDownFromRestoringTheFocus()
    {
        var cut = Render<TmSpreadsheet>();
        var grid = cut.FindComponent<TmSpreadsheetCanvasGrid>();

        // The right-click / long-press path: the grid selects the pressed cell and raises the request,
        // the host renders the menu as a sibling and the menu focuses its first entry.
        await cut.InvokeAsync(() => grid.Instance.OnCanvasContextMenu(40, 40, 120, 240));
        cut.FindAll(".tm-spreadsheet-context-menu").Should().ContainSingle("the host renders the menu");

        // Any LATER grid activation while the menu is open must not yank the caret back: in a real browser
        // the grid's focus hop lands ~25 ms after the menu focused its first entry, which killed the
        // ArrowDown / Home / End walk while every DOM assertion still passed.
        var before = JSInterop.Invocations.Count(invocation =>
            invocation.Identifier == FocusInvocation || invocation.Identifier == GuardedFocusInvocation);
        await cut.InvokeAsync(() => grid.Instance.OnCanvasPointer(40, 40, shiftKey: false, ctrlKey: false));

        JSInterop.Invocations
            .Count(invocation => invocation.Identifier == FocusInvocation || invocation.Identifier == GuardedFocusInvocation)
            .Should().Be(before,
                "the open menu owns the focus — the grid's restore must stand down (SuppressFocusRestore)");
    }

    [Fact]
    public async Task NoContextMenu_TheGridStillRestoresTheFocusAfterACanvasActivation()
    {
        var cut = Render<TmSpreadsheet>();
        var grid = cut.FindComponent<TmSpreadsheetCanvasGrid>();

        // A plain canvas activation (no menu): the guard must NOT turn the historical focus restore off.
        await cut.InvokeAsync(() => grid.Instance.OnCanvasPointer(40, 40, shiftKey: false, ctrlKey: false));

        JSInterop.Invocations
            .Count(invocation => invocation.Identifier == GuardedFocusInvocation)
            .Should().BeGreaterThan(0,
                "a canvas click still lands the focus in the grid — through the ONE atomic guard call, "
                + "never through a probe followed by a separate FocusAsync hop");
    }

    [Fact]
    public async Task ClosingTheMenu_ReArmsTheGridFocusRestore()
    {
        var cut = Render<TmSpreadsheet>();
        var grid = cut.FindComponent<TmSpreadsheetCanvasGrid>();

        await cut.InvokeAsync(() => grid.Instance.OnCanvasContextMenu(40, 40, 120, 240));
        cut.FindAll(".tm-spreadsheet-context-menu").Should().ContainSingle();

        // Escape on an entry closes the anchored menu and the host clears its request.
        await cut.InvokeAsync(() => cut.Find(".tm-spreadsheet-context-menu__item")
            .KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" }));
        cut.FindAll(".tm-spreadsheet-context-menu").Should().BeEmpty("Escape closes the menu");

        var before = JSInterop.Invocations.Count(invocation => invocation.Identifier == GuardedFocusInvocation);
        await cut.InvokeAsync(() => grid.Instance.OnCanvasPointer(40, 40, shiftKey: false, ctrlKey: false));

        JSInterop.Invocations
            .Count(invocation => invocation.Identifier == GuardedFocusInvocation)
            .Should().BeGreaterThan(before,
                "with the menu gone the grid owns the focus again — the suppression is scoped to the open menu");
    }

    [Fact]
    public void ContextMenu_FirstEntryIsAFocusableRovingTabStop()
    {
        // The keyboard contract the browser spec asserts: the entries are real buttons with the stable
        // data-context-menu-id the roving focus resolves, and they are not Tab stops (one roving stop).
        var cut = Render<TmSpreadsheet>();
        var grid = cut.FindComponent<TmSpreadsheetCanvasGrid>();
        cut.InvokeAsync(() => grid.Instance.OnCanvasContextMenu(40, 40, 120, 240)).GetAwaiter().GetResult();

        var items = cut.FindAll($".tm-spreadsheet-context-menu__item[{ContextMenuIdAttribute}]");
        items.Should().NotBeEmpty();
        items.Should().OnlyContain(item => item.GetAttribute("tabindex") == "-1",
            "the menu is one roving stop, never fifteen Tab stops");
        items[0].GetAttribute(ContextMenuIdAttribute).Should().Be("format-cells");
    }
}

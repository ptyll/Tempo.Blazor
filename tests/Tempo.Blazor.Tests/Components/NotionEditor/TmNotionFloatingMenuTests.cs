using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// Phase 3 (N5): the floating menu is a thin wrap of the core overlay. Mobile is a sheet with a
/// title and a Close action; desktop is an anchored popover without a title. Escape and an outside
/// click raise OnClose, and Escape closes only the innermost overlay.
/// </summary>
public sealed class TmNotionFloatingMenuTests : LocalizationTestBase
{
    [Fact]
    public void Mobile_IsSheet_WithTitleAndClose()
    {
        var cut = RenderMenu(TmLayoutMode.Mobile, title: "Insert");

        cut.Find(".tm-overlay-panel-sheet, .tm-drawer").Should().NotBeNull();
        cut.Markup.Should().Contain("Insert");
        cut.FindAll("button").Select(b => b.TextContent).Should().Contain(t => t.Contains("Close", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Zavřít", StringComparison.OrdinalIgnoreCase)
            || t.Contains("[Tm_Close]", StringComparison.Ordinal));
    }

    [Fact]
    public void Desktop_IsAnchoredPopover_WithoutTitle()
    {
        var cut = RenderMenu(TmLayoutMode.Desktop, title: "Insert");

        cut.FindAll(".tm-overlay-panel-sheet, .tm-drawer").Should().BeEmpty();
        cut.Find(".tm-notion-floating-menu").Should().NotBeNull();
        cut.FindAll(".tm-notion-floating-menu__title, .tm-overlay-panel-sheet__title").Should().BeEmpty();
    }

    [Fact]
    public void Auto_CascadedMobile_IsSheet_WithSheetTitle()
    {
        // LayoutMode=Auto + a mobile viewport cascade: the menu sheets even though no mode is
        // forced, and the sheet header carries the consumer title (SheetTitle is always passed).
        var cut = Render<TmNotionFloatingMenu>(p => p
            .AddCascadingValue(TmLayoutScopes.Viewport, new TmLayoutContext(TmLayoutMode.Auto, TmLayoutMode.Mobile))
            .Add(c => c.IsOpen, true)
            .Add(c => c.Title, "Insert block")
            .Add(c => c.ChildContent, (RenderFragment)(b => b.AddMarkupContent(0, "<button type=\"button\">Item</button>"))));

        cut.Find(".tm-overlay-panel-sheet").Should().NotBeNull();
        cut.Find(".tm-overlay-panel-sheet__title").TextContent.Trim().Should().Be("Insert block");
    }

    [Fact]
    public void Auto_CascadedDesktop_StaysPopover_WithoutSheetTitle()
    {
        var cut = Render<TmNotionFloatingMenu>(p => p
            .AddCascadingValue(TmLayoutScopes.Viewport, new TmLayoutContext(TmLayoutMode.Auto, TmLayoutMode.Desktop))
            .Add(c => c.IsOpen, true)
            .Add(c => c.Title, "Insert block")
            .Add(c => c.ChildContent, (RenderFragment)(b => b.AddMarkupContent(0, "<button type=\"button\">Item</button>"))));

        cut.FindAll(".tm-overlay-panel-sheet, .tm-drawer").Should().BeEmpty();
        cut.Find(".tm-overlay-panel").Should().NotBeNull();
    }

    [Fact]
    public async Task Escape_RaisesOnClose()
    {
        var closed = 0;
        var cut = RenderMenu(TmLayoutMode.Desktop, title: "Insert", onClose: () => closed++);

        await cut.Find(".tm-notion-floating-menu").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        closed.Should().Be(1);
    }

    [Fact]
    public void Escape_ClosesOnlyTheInnermostOverlay()
    {
        var outer = 0;
        var inner = 0;
        var cut = Render<TmNotionFloatingMenu>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.OnClose, EventCallback.Factory.Create(this, () => outer++))
            .Add(c => c.ChildContent, (RenderFragment)(b =>
            {
                b.OpenComponent<TmNotionFloatingMenu>(0);
                b.AddAttribute(1, "IsOpen", true);
                b.AddAttribute(2, "LayoutMode", TmLayoutMode.Desktop);
                b.AddAttribute(3, "OnClose", EventCallback.Factory.Create(this, () => inner++));
                b.AddAttribute(4, "ChildContent", (RenderFragment)(innerBuilder => innerBuilder.AddMarkupContent(0, "<span class=\"inner\">item</span>")));
                b.CloseComponent();
            })));

        cut.Find(".inner").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" }).GetAwaiter().GetResult();

        inner.Should().Be(1);
        outer.Should().Be(0);
    }

    private IRenderedComponent<TmNotionFloatingMenu> RenderMenu(TmLayoutMode mode, string title, Action? onClose = null)
        => Render<TmNotionFloatingMenu>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Title, title)
            .Add(c => c.LayoutMode, mode)
            .Add(c => c.OnClose, EventCallback.Factory.Create(this, () => onClose?.Invoke()))
            .Add(c => c.ChildContent, (RenderFragment)(b => b.AddMarkupContent(0, "<button type=\"button\">Item</button>"))));
}

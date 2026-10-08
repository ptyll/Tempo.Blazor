using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Dropdowns;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Dropdowns;

/// <summary>TDD tests for TmDropdown.</summary>
public class TmDropdownTests : LocalizationTestBase
{
    [Fact]
    public void TmDropdown_Renders_Trigger_Button()
    {
        var cut = Render<TmDropdown>(p => p
            .Add(c => c.Text, "Options"));

        cut.Find("button.tm-dropdown-trigger").Should().NotBeNull();
    }

    [Fact]
    public void TmDropdown_Trigger_Shows_Text()
    {
        var cut = Render<TmDropdown>(p => p
            .Add(c => c.Text, "Actions"));

        cut.Find("button.tm-dropdown-trigger").TextContent.Should().Contain("Actions");
    }

    [Fact]
    public void TmDropdown_Menu_Hidden_By_Default()
    {
        var cut = Render<TmDropdown>(p => p
            .Add(c => c.Text, "Options"));

        cut.FindAll(".tm-dropdown-menu").Should().BeEmpty();
    }

    [Fact]
    public void TmDropdown_Click_Opens_Menu()
    {
        var cut = Render<TmDropdown>(p => p
            .Add(c => c.Text, "Options")
            .AddChildContent("<div class='item'>Item 1</div>"));

        cut.Find("button.tm-dropdown-trigger").Click();

        cut.FindAll(".tm-dropdown-menu").Should().NotBeEmpty();
    }

    [Fact]
    public void TmDropdown_Click_Again_Closes_Menu()
    {
        var cut = Render<TmDropdown>(p => p
            .Add(c => c.Text, "Options")
            .AddChildContent("<div>Item</div>"));

        cut.Find("button.tm-dropdown-trigger").Click();
        cut.Find("button.tm-dropdown-trigger").Click();

        cut.FindAll(".tm-dropdown-menu").Should().BeEmpty();
    }

    [Fact]
    public async Task TmDropdown_Escape_Key_Closes_Menu()
    {
        var cut = Render<TmDropdown>(p => p
            .Add(c => c.Text, "Options")
            .AddChildContent("<div>Item</div>"));

        cut.Find("button.tm-dropdown-trigger").Click();

        // overlay.js consumes Escape in the window capture phase in a real browser — the
        // component's dismissal path is this JSInvokable callback, not a keydown on the
        // wrapper (dead-branch sweep, N169 follow-up).
        var overlay = cut.FindComponent<Tempo.Blazor.Components.Overlay.TmOverlayPanel>();
        await cut.InvokeAsync(() => overlay.Instance.NotifyDismissedAsync("escape"));

        cut.FindAll(".tm-dropdown-menu").Should().BeEmpty();
    }

    [Fact]
    public void TmDropdown_Trigger_Has_Aria_Expanded_False_When_Closed()
    {
        var cut = Render<TmDropdown>(p => p
            .Add(c => c.Text, "Options"));

        cut.Find("button.tm-dropdown-trigger").GetAttribute("aria-expanded").Should().Be("false");
    }

    [Fact]
    public void TmDropdown_Trigger_Has_Aria_Expanded_True_When_Open()
    {
        var cut = Render<TmDropdown>(p => p
            .Add(c => c.Text, "Options")
            .AddChildContent("<div>Item</div>"));

        cut.Find("button.tm-dropdown-trigger").Click();

        cut.Find("button.tm-dropdown-trigger").GetAttribute("aria-expanded").Should().Be("true");
    }

    /// <summary>
    /// N319: the menu must close BEFORE <c>OnSelect</c> runs — a handler that opens a dialog
    /// would otherwise render it under the still-open top-layer popover.
    /// </summary>
    [Fact]
    public void TmDropdown_SelectItem_ClosesMenuBeforeInvokingOnSelect()
    {
        var openPanelsDuringHandler = -1;
        IRenderedComponent<TmDropdown>? probe = null;
        var cut = Render<TmDropdown>(p => p
            .Add(c => c.Text, "Options")
            .Add(c => c.OnSelect, EventCallback.Factory.Create<string>(this, _ =>
            {
                openPanelsDuringHandler = probe!.FindAll(".tm-overlay-panel").Count;
            }))
            .AddChildContent<TmDropdownItem>(i => i
                .Add(x => x.Value, "a")
                .AddChildContent("A")));
        probe = cut;

        cut.Find("button.tm-dropdown-trigger").Click();
        cut.Find(".tm-dropdown-item").Click();

        cut.WaitForAssertion(() => openPanelsDuringHandler.Should().Be(0));
    }

    [Fact]
    public void TmDropdown_SelectItem_ReturnsFocusToTrigger()
    {
        // docs/overlays.md rule 4: focus returns to the trigger on every close path — Escape,
        // outside, Done AND selection. A selection close does not route through overlay.js, so
        // the dropdown restores focus itself (like TmFilterableDropdown/TmMultiSelect since 2.8.25).
        var cut = Render<TmDropdown>(p => p
            .Add(c => c.Text, "Options")
            .AddChildContent<TmDropdownItem>(i => i
                .Add(x => x.Value, "a")
                .AddChildContent("A")));

        cut.Find("button.tm-dropdown-trigger").Click();
        var focusCallsBefore = JSInterop.Invocations.Count(i => i.Identifier.Contains("focus"));
        cut.Find(".tm-dropdown-item").Click();

        cut.WaitForAssertion(() =>
            JSInterop.Invocations.Count(i => i.Identifier.Contains("focus"))
                .Should().BeGreaterThan(focusCallsBefore));
    }

    [Fact]
    public void MobilePresentationSheet_SelectItem_ClosesSheet_AndRestoresFocus()
    {
        var cut = Render<TmDropdown>(p => p
            .Add(c => c.Text, "Export")
            .Add(c => c.MobilePresentation, PanelPresentation.Sheet)
            .AddChildContent<TmDropdownItem>(i => i
                .Add(x => x.Value, "a")
                .AddChildContent("A")));

        cut.Find("button.tm-dropdown-trigger").Click();
        cut.FindAll(".tm-overlay-panel-sheet").Should().HaveCount(1);

        cut.Find(".tm-dropdown-item").Click();

        cut.WaitForAssertion(() => cut.FindAll(".tm-overlay-panel-sheet").Should().BeEmpty());
        cut.WaitForAssertion(() =>
            JSInterop.Invocations.Count(i => i.Identifier.Contains("focus"))
                .Should().BeGreaterThan(0));
    }

    // ── F3 mobile presentation: on a narrow viewport the menu is a bottom sheet ─────────────

    [Fact]
    public void MobilePresentationSheet_RendersInBottomDrawer_WithTitleAndDone()
    {
        var cut = Render<TmDropdown>(p => p
            .Add(c => c.Text, "Export")
            .Add(c => c.MobilePresentation, PanelPresentation.Sheet)
            .AddChildContent("<div class='menu-item'>CSV</div>"));

        cut.Find("button.tm-dropdown-trigger").Click();

        cut.Find(".tm-overlay-panel-sheet__title").TextContent.Should().Be("Export");
        cut.Find(".tm-overlay-panel-sheet__done").TextContent.Trim().Should().Be("Done");
        cut.Find(".menu-item").Should().NotBeNull();
        // No floating popover in sheet mode.
        cut.FindAll(".tm-dropdown-menu").Should().BeEmpty();
    }

    [Fact]
    public void MobilePresentationAuto_UsesPopoverOnDesktop()
    {
        var cut = Render<TmDropdown>(p => p
            .Add(c => c.Text, "Export")
            .AddChildContent("<div>Item</div>"));

        cut.Find("button.tm-dropdown-trigger").Click();

        cut.FindAll(".tm-dropdown-menu").Should().HaveCount(1);
        cut.FindAll(".tm-drawer").Should().BeEmpty();
    }

    [Fact]
    public void MobilePresentation_AriaExpanded_AndFocusReturn()
    {
        var cut = Render<TmDropdown>(p => p
            .Add(c => c.Text, "Export")
            .Add(c => c.MobilePresentation, PanelPresentation.Sheet)
            .AddChildContent("<div>Item</div>"));

        cut.Find("button.tm-dropdown-trigger").GetAttribute("aria-expanded").Should().Be("false");

        cut.Find("button.tm-dropdown-trigger").Click();
        cut.Find("button.tm-dropdown-trigger").GetAttribute("aria-expanded").Should().Be("true");

        // Focus return is asserted with a real browser in E2E; here the trigger element itself is
        // wired as the sheet's restore target, so closing lands focus back on it.
        var drawer = cut.FindComponent<Tempo.Blazor.Components.Layout.TmDrawer>();
        drawer.Instance.RestoreFocusTarget.HasValue.Should().BeTrue();

        // The Done action closes the sheet and the trigger reflects the closed state.
        cut.Find(".tm-overlay-panel-sheet__done").Click();
        cut.Find("button.tm-dropdown-trigger").GetAttribute("aria-expanded").Should().Be("false");
        cut.FindAll(".tm-drawer").Should().BeEmpty();
    }
}

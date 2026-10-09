using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Actions;

/// <summary>
/// TDD tests for <see cref="TmMobileActionBar"/> (phase F5). The bar shows up to
/// <c>MaxVisible</c> actions and moves the rest into a "More" menu that composes
/// <c>TmOverlayPanel</c> (popover on desktop, F3 bottom sheet on a mobile viewport).
/// </summary>
public class TmMobileActionBarTests : LocalizationTestBase
{
    private static TmActionItem Action(
        string label,
        int priority = 0,
        string? icon = null,
        bool disabled = false,
        bool keepMenuOpen = false,
        EventCallback? onClick = null)
        => new()
        {
            Id = label,
            Label = label,
            Icon = icon,
            Priority = priority,
            Disabled = disabled,
            KeepMenuOpen = keepMenuOpen,
            OnClick = onClick ?? default,
        };

    private static IReadOnlyList<TmActionItem> Actions(params string[] labels)
        => labels.Select(label => Action(label)).ToList();

    [Fact]
    public void MoreThanMaxVisible_MovesRestToMoreSheet()
    {
        var cut = Render<TmMobileActionBar>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .Add(x => x.Items, Actions("One", "Two", "Three", "Four", "Five")));

        // Three actions stay on the bar; the remaining two move behind "More".
        cut.FindAll(".tm-mobile-action-bar__action").Count.Should().Be(3);

        var more = cut.Find(".tm-mobile-action-bar__more");
        more.TextContent.Trim().Should().Be("More");

        cut.FindAll("[role='menuitem']").Should().BeEmpty();

        more.Click();

        var menu = cut.Find("[role='menu']");
        menu.Should().NotBeNull();
        var menuItems = cut.FindAll("[role='menuitem']");
        menuItems.Count.Should().Be(2);
        menuItems.Select(i => i.TextContent.Trim()).Should().Equal("Four", "Five");
    }

    [Fact]
    public void NoOverflow_RendersNoMoreButton()
    {
        var cut = Render<TmMobileActionBar>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .Add(x => x.Items, Actions("One", "Two", "Three")));

        cut.FindAll(".tm-mobile-action-bar__action").Count.Should().Be(3);
        cut.FindAll(".tm-mobile-action-bar__more").Should().BeEmpty();
    }

    [Fact]
    public void StickyContainer_DoesNotUsePositionFixed()
    {
        var cut = Render<TmMobileActionBar>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .Add(x => x.Items, Actions("One")));

        var bar = cut.Find(".tm-mobile-action-bar__bar");
        bar.ClassList.Should().Contain("tm-mobile-action-bar__bar--sticky");
        bar.ClassList.Should().NotContain("tm-mobile-action-bar__bar--fixed");

        // Source-level guard: position: fixed exists ONLY in the FixedViewport opt-in block
        // (comments stripped — the header documents the rule by name).
        var css = File.ReadAllText(RepoPath(
            "src", "Tempo.Blazor", "wwwroot", "css", "components", "_mobile-action-bar.css"));
        css = System.Text.RegularExpressions.Regex.Replace(css, @"/\*.*?\*/", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);
        css.Should().Contain("position: sticky");
        css.Split('}', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(block => block.Contains("position: fixed", StringComparison.Ordinal))
            .Should()
            .AllSatisfy(block => block.Should().Contain("tm-mobile-action-bar__bar--fixed"));
    }

    [Fact]
    public void HiddenAboveBreakpoint_WhenAuto()
    {
        // Auto resolves from the container; with no measurement the initial (desktop) mode stands.
        var cut = Render<TmMobileActionBar>(p => p
            .Add(x => x.Items, Actions("One", "Two", "Three")));

        cut.FindAll(".tm-mobile-action-bar__bar").Should().BeEmpty();
    }

    [Fact]
    public void HiddenOnTablet_EvenWhenForced()
    {
        var cut = Render<TmMobileActionBar>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Tablet)
            .Add(x => x.Items, Actions("One")));

        cut.FindAll(".tm-mobile-action-bar__bar").Should().BeEmpty();
    }

    [Fact]
    public void ItemsHaveAccessibleNames()
    {
        var cut = Render<TmMobileActionBar>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .Add(x => x.Items, new List<TmActionItem>
            {
                Action("Alpha", icon: "plus"),
                Action("Beta", icon: "save"),
            }));

        var buttons = cut.FindAll(".tm-mobile-action-bar__action");
        buttons.Count.Should().Be(2);
        buttons.Select(b => b.TextContent.Trim()).Should().Equal("Alpha", "Beta");
    }

    [Fact]
    public void ItemsOrder_IsTheRenderOrder()
    {
        // User decision (F5 round 1): Priority is ONLY an overflow rank — the bar renders in
        // Items order, whatever the priorities say.
        var cut = Render<TmMobileActionBar>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .Add(x => x.Items, new List<TmActionItem>
            {
                Action("Low", priority: 0),
                Action("High", priority: 10),
                Action("Mid", priority: 5),
            }));

        cut.FindAll(".tm-mobile-action-bar__action")
            .Select(b => b.TextContent.Trim())
            .Should().Equal("Low", "High", "Mid");
    }

    [Fact]
    public void Priority_OnlyDecidesOverflow_LowestFirst_TiesDropTheLastItem()
    {
        var cut = Render<TmMobileActionBar>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .Add(x => x.MaxVisible, 2)
            .Add(x => x.Items, new List<TmActionItem>
            {
                Action("A", priority: 0),
                Action("B", priority: 10),
                Action("C", priority: 5),
                Action("D", priority: 5),
            }));

        cut.FindAll(".tm-mobile-action-bar__action")
            .Select(b => b.TextContent.Trim())
            .Should().Equal("B", "C");
        cut.Find(".tm-mobile-action-bar__more").TextContent.Trim().Should().Be("More");

        cut.Find(".tm-mobile-action-bar__more").Click();

        // The overflow menu also renders in Items order: A (lowest priority), then D (ties with C,
        // but the LAST item drops first).
        cut.FindAll("[role='menuitem']")
            .Select(i => i.TextContent.Trim())
            .Should().Equal("A", "D");
    }

    [Fact]
    public void MoreButton_IsAlwaysLast()
    {
        var cut = Render<TmMobileActionBar>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .Add(x => x.Items, Actions("One", "Two", "Three", "Four")));

        var children = cut.FindAll(".tm-mobile-action-bar__bar > *");
        children.Count.Should().Be(4);
        children[^1].ClassList.Should().Contain("tm-mobile-action-bar__more");
    }

    [Fact]
    public void DisabledItem_RendersDisabledButton()
    {
        var cut = Render<TmMobileActionBar>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .Add(x => x.Items, new List<TmActionItem>
            {
                Action("Blocked", disabled: true),
                Action("Free"),
            }));

        var buttons = cut.FindAll(".tm-mobile-action-bar__action");
        buttons[0].HasAttribute("disabled").Should().BeTrue();
        buttons[1].HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void FixedViewport_UsesFixedModifierAndViewportDataLayout()
    {
        var cut = Render<TmMobileActionBar>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .Add(x => x.Placement, ActionBarPlacement.FixedViewport)
            .Add(x => x.Items, Actions("One")));

        var bar = cut.Find(".tm-mobile-action-bar__bar");
        bar.ClassList.Should().Contain("tm-mobile-action-bar__bar--fixed");
        bar.ClassList.Should().NotContain("tm-mobile-action-bar__bar--sticky");

        var root = cut.Find(".tm-mobile-action-bar");
        root.GetAttribute("data-layout").Should().Be("mobile");
    }

    [Fact]
    public void Inline_RendersStaticModifier()
    {
        var cut = Render<TmMobileActionBar>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .Add(x => x.Placement, ActionBarPlacement.Inline)
            .Add(x => x.Items, Actions("One")));

        var bar = cut.Find(".tm-mobile-action-bar__bar");
        bar.ClassList.Should().Contain("tm-mobile-action-bar__bar--inline");
        bar.ClassList.Should().NotContain("tm-mobile-action-bar__bar--sticky");
    }

    [Fact]
    public void ChildContent_RendersBodyRegionAboveTheBar()
    {
        var cut = Render<TmMobileActionBar>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .Add(x => x.Items, Actions("One"))
            .AddChildContent("<p class='body-marker'>Body content</p>"));

        var body = cut.Find(".tm-mobile-action-bar__body");
        body.TextContent.Should().Contain("Body content");

        // The bar sits after the body region in DOM order.
        cut.Find(".tm-mobile-action-bar").Children
            .Select((el, i) => (el, i))
            .Should().ContainSingle(pair => pair.el.ClassList.Contains("tm-mobile-action-bar__bar"));
    }

    [Fact]
    public void MenuItem_InvokesOnClickAndCloses()
    {
        var calls = 0;
        var cut = Render<TmMobileActionBar>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .Add(x => x.Items, new List<TmActionItem>
            {
                Action("One"),
                Action("Two"),
                Action("Three"),
                Action("Four", onClick: EventCallback.Factory.Create(this, () => { calls++; return Task.CompletedTask; })),
            }));

        cut.Find(".tm-mobile-action-bar__more").Click();
        cut.FindAll("[role='menuitem']")[0].Click();

        calls.Should().Be(1);
        cut.FindAll("[role='menu']").Should().BeEmpty();
    }

    [Fact]
    public void MenuItem_KeepMenuOpen_LeavesTheMenuOpen()
    {
        var calls = 0;
        var cut = Render<TmMobileActionBar>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .Add(x => x.Items, new List<TmActionItem>
            {
                Action("One"),
                Action("Two"),
                Action("Three"),
                Action("Four", keepMenuOpen: true,
                    onClick: EventCallback.Factory.Create(this, () => { calls++; return Task.CompletedTask; })),
            }));

        cut.Find(".tm-mobile-action-bar__more").Click();
        cut.FindAll("[role='menuitem']")[0].Click();

        calls.Should().Be(1);
        cut.FindAll("[role='menu']").Should().NotBeEmpty("a KeepMenuOpen item must not close the menu");
    }

    [Fact]
    public void MoreLabel_Override_WinsOverLocalization()
    {
        var cut = Render<TmMobileActionBar>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .Add(x => x.Items, Actions("One", "Two", "Three", "Four"))
            .Add(x => x.MoreLabel, "Další akce"));

        cut.Find(".tm-mobile-action-bar__more").TextContent.Trim().Should().Be("Další akce");
    }

    [Fact]
    public void MoreSheet_ComposesDrawerWithMenuInitialFocus()
    {
        // The More menu composes TmOverlayPanel: on a mobile layout it presents as the F3
        // bottom sheet (TmDrawer), never a hand-rolled copy.
        var cut = Render<TmMobileActionBar>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .Add(x => x.Items, Actions("One", "Two", "Three", "Four")));

        cut.Find(".tm-mobile-action-bar__more").Click();

        cut.Find(".tm-overlay-panel-sheet").Should().NotBeNull();
        cut.Find(".tm-drawer__panel").Should().NotBeNull();
        cut.Find("[role='menu']").Should().NotBeNull();
    }

    [Fact]
    public void Toolbar_HasAccessibleLabel()
    {
        var cut = Render<TmMobileActionBar>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .Add(x => x.Items, Actions("One")));

        cut.Find(".tm-mobile-action-bar__bar")
            .GetAttribute("aria-label")
            .Should().Be("Action bar");
    }

    [Theory]
    [InlineData(TmLayoutMode.Desktop)]
    [InlineData(TmLayoutMode.Tablet)]
    public void ChildContent_RendersAboveBreakpoint_WithoutBar(TmLayoutMode mode)
    {
        // X1 (BLOCKER): the ChildContent belongs to the host at every layout — only the role=group
        // bar is gated on the mobile breakpoint.
        var cut = Render<TmMobileActionBar>(p => p
            .Add(x => x.LayoutMode, mode)
            .Add(x => x.Items, Actions("One", "Two", "Three"))
            .AddChildContent("<p class='body-marker'>Body content</p>"));

        var body = cut.Find(".tm-mobile-action-bar__body");
        body.TextContent.Should().Contain("Body content");
        cut.FindAll(".tm-mobile-action-bar__bar").Should().BeEmpty();
        body.ClassList.Should().NotContain("tm-mobile-action-bar__body--with-bar",
            "no bar → no height reserve");
    }

    [Fact]
    public void ChildContent_AutoUnmeasured_RendersBodyWithoutBar()
    {
        // Auto without a DOM resolves to the initial (desktop) mode: the content survives, the
        // bar does not exist yet.
        var cut = Render<TmMobileActionBar>(p => p
            .Add(x => x.Items, Actions("One"))
            .AddChildContent("<p class='body-marker'>Body content</p>"));

        cut.Find(".tm-mobile-action-bar__body").TextContent.Should().Contain("Body content");
        cut.FindAll(".tm-mobile-action-bar__bar").Should().BeEmpty();
    }

    [Fact]
    public void ChildContent_WithBar_GetsTheReserveModifier()
    {
        var cut = Render<TmMobileActionBar>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .Add(x => x.Items, Actions("One"))
            .AddChildContent("<p>Body content</p>"));

        cut.Find(".tm-mobile-action-bar__body").ClassList
            .Should().Contain("tm-mobile-action-bar__body--with-bar");
    }

    [Fact]
    public void BareRoot_WithoutChildContent_IsTheStickyBox()
    {
        // X2 (MAJOR): with no ChildContent the bar's containing block would be its own root, so
        // sticky could never move — the ROOT becomes the sticky box and the bar is static inside.
        var cut = Render<TmMobileActionBar>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .Add(x => x.Items, Actions("One")));

        cut.Find(".tm-mobile-action-bar").ClassList
            .Should().Contain("tm-mobile-action-bar--bare");

        var css = BarCss();
        css.Should().Contain("tm-mobile-action-bar--bare");
        css.Should().Contain("position: sticky");
    }

    [Fact]
    public void WithChildContent_StickyStaysOnTheBar()
    {
        var cut = Render<TmMobileActionBar>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .Add(x => x.Items, Actions("One"))
            .AddChildContent("<p>Body content</p>"));

        cut.Find(".tm-mobile-action-bar").ClassList
            .Should().NotContain("tm-mobile-action-bar--bare");
        cut.Find(".tm-mobile-action-bar__bar").ClassList
            .Should().Contain("tm-mobile-action-bar__bar--sticky");
    }

    [Fact]
    public void BodyReserve_OnlyForFixedViewport()
    {
        // X2 (b): the body's bottom padding reserves space ONLY for the FixedViewport bar (fixed
        // positioning removes the bar from flow). Sticky and inline bars sit in flow at the end
        // of the content — padding there double-reserves an empty band at scroll end.
        var css = BarCss(stripComments: true);
        var bodyBlocks = css.Split('}', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(block => block.Contains("tm-mobile-action-bar__body", StringComparison.Ordinal)
                && block.Contains("padding", StringComparison.Ordinal))
            .ToList();

        bodyBlocks.Should().NotBeEmpty("the with-bar reserve rule must exist");
        bodyBlocks.Should().AllSatisfy(block => block.Should().Contain("--fixed",
            "the body's height reserve may only exist under the FixedViewport placement"));
    }

    [Fact]
    public void BarActions_AreTileButtons_NotTmBtn()
    {
        // X3 (MAJOR): bar actions are the bar's own tile buttons (icon over a two-line label),
        // never .tm-btn — the .tm-btn contract (fixed height, nowrap label) made labels
        // illegible at 320/390.
        var cut = Render<TmMobileActionBar>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .Add(x => x.Items, new List<TmActionItem>
            {
                Action("Alpha", icon: "plus"),
                Action("Beta", icon: "save"),
            }));

        var buttons = cut.FindAll(".tm-mobile-action-bar__action");
        buttons.Count.Should().Be(2);
        buttons.Should().AllSatisfy(button =>
        {
            button.TagName.Should().Be("BUTTON");
            button.ClassList.Should().NotContain("tm-btn");
            // The full label stays in the DOM (no title attribute duplicating the accessible name).
            button.HasAttribute("title").Should().BeFalse();
            button.GetAttribute("data-action-id").Should().Be(button.TextContent.Trim());
        });
    }

    [Fact]
    public void NullItems_RendersNoBar()
    {
        // X11: nothing to show → no bar at all (and no height reserve).
        var cut = Render<TmMobileActionBar>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .AddChildContent("<p>Body content</p>"));

        cut.FindAll(".tm-mobile-action-bar__bar").Should().BeEmpty();
        cut.Find(".tm-mobile-action-bar__body").ClassList
            .Should().NotContain("tm-mobile-action-bar__body--with-bar");
    }

    [Fact]
    public void EmptyItems_RendersNoBar()
    {
        var cut = Render<TmMobileActionBar>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .Add(x => x.Items, new List<TmActionItem>())
            .AddChildContent("<p>Body content</p>"));

        cut.FindAll(".tm-mobile-action-bar__bar").Should().BeEmpty();
        cut.Find(".tm-mobile-action-bar__body").ClassList
            .Should().NotContain("tm-mobile-action-bar__body--with-bar");
    }

    [Fact]
    public void AllOverflowItemsDisabled_DisablesTheMoreTrigger()
    {
        // X11: when every overflow item is disabled the menu would open onto nothing invocable —
        // the More trigger is disabled instead.
        var cut = Render<TmMobileActionBar>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .Add(x => x.Items, new List<TmActionItem>
            {
                Action("One", priority: 10),
                Action("Two", priority: 10),
                Action("Three", priority: 10),
                Action("Blocked", priority: 0, disabled: true),
            }));

        var more = cut.Find(".tm-mobile-action-bar__more");
        more.HasAttribute("disabled").Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void MaxVisible_BelowOne_ClampsToOne(int maxVisible)
    {
        var cut = Render<TmMobileActionBar>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .Add(x => x.MaxVisible, maxVisible)
            .Add(x => x.Items, Actions("One", "Two")));

        cut.FindAll(".tm-mobile-action-bar__action").Count.Should().Be(1);
        cut.Find(".tm-mobile-action-bar__more").TextContent.Trim().Should().Be("More");
    }

    [Fact]
    public void Actions_CarryDataActionId_InBarAndMenu()
    {
        // X12: the stable Id is a render key and a test hook — no drifting sequence numbers.
        var cut = Render<TmMobileActionBar>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .Add(x => x.Items, new List<TmActionItem>
            {
                Action("One"),
                Action("Two"),
                Action("Three"),
                Action("Four"),
            }));

        cut.FindAll(".tm-mobile-action-bar__action")
            .Select(b => b.GetAttribute("data-action-id"))
            .Should().Equal("One", "Two", "Three");

        cut.Find(".tm-mobile-action-bar__more").Click();
        cut.FindAll("[role='menuitem']")
            .Select(i => i.GetAttribute("data-action-id"))
            .Should().Equal("Four");
    }

    [Fact]
    public void MoreTrigger_HasMenuAriaAttributes()
    {
        // X10: haspopup/expanded/controls like every other popup trigger in the library. A
        // desktop viewport context makes the menu present as the anchored popover (the sheet
        // branch does not render the panel Class/Id).
        var cut = Render<TmMobileActionBar>(p => p
            .AddCascadingValue(TmLayoutScopes.Viewport, new TmLayoutContext(TmLayoutMode.Auto, TmLayoutMode.Desktop))
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .Add(x => x.Items, Actions("One", "Two", "Three", "Four")));

        var more = cut.Find(".tm-mobile-action-bar__more");
        more.GetAttribute("aria-haspopup").Should().Be("menu");
        more.GetAttribute("aria-expanded").Should().Be("false");
        var controls = more.GetAttribute("aria-controls");
        controls.Should().NotBeNullOrEmpty();

        more.Click();

        cut.Find(".tm-mobile-action-bar__more").GetAttribute("aria-expanded").Should().Be("true");
        cut.Find(".tm-mobile-action-bar__menu-panel").Id.Should().Be(controls);
    }

    [Fact]
    public async Task CloseMoreAsync_ClosesTheMenu()
    {
        // X13: a host closes the sheet after a confirmed KeepMenuOpen action.
        var cut = Render<TmMobileActionBar>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .Add(x => x.Items, Actions("One", "Two", "Three", "Four")));

        cut.Find(".tm-mobile-action-bar__more").Click();
        cut.FindAll("[role='menu']").Should().NotBeEmpty();

        await cut.Instance.CloseMoreAsync();

        cut.FindAll("[role='menu']").Should().BeEmpty();
    }

    [Fact]
    public void MoreMenu_PanelCarriesTheSurfaceClass()
    {
        // X4 (MAJOR): the popover-mode More menu must paint a surface — the panel carries the
        // bar's own surface class instead of accepting the transparent default. The desktop
        // viewport context forces the popover presentation (in sheet mode the drawer owns the
        // surface and the panel Class is not rendered).
        var cut = Render<TmMobileActionBar>(p => p
            .AddCascadingValue(TmLayoutScopes.Viewport, new TmLayoutContext(TmLayoutMode.Auto, TmLayoutMode.Desktop))
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .Add(x => x.Items, Actions("One", "Two", "Three", "Four")));

        cut.Find(".tm-mobile-action-bar__more").Click();

        cut.Find(".tm-mobile-action-bar__menu-panel").Should().NotBeNull();
    }

    [Fact]
    public void OverflowMenu_RendersInItemsOrder()
    {
        // X11/X12: the overflow menu renders in Items order with the stable Ids as hooks; the
        // initial-focus move to the first ENABLED item is covered by the E2E lane (focus needs a
        // real browser).
        var cut = Render<TmMobileActionBar>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .Add(x => x.Items, new List<TmActionItem>
            {
                Action("One", priority: 10),
                Action("Two", priority: 10),
                Action("Three", priority: 10),
                Action("Blocked", priority: 0, disabled: true),
                Action("Free", priority: 0),
            }));

        cut.Find(".tm-mobile-action-bar__more").Click();

        cut.FindAll("[role='menuitem']")
            .Select(i => i.GetAttribute("data-action-id"))
            .Should().Equal("Blocked", "Free");
    }

    private static string BarCss(bool stripComments = false)
    {
        var css = File.ReadAllText(RepoPath(
            "src", "Tempo.Blazor", "wwwroot", "css", "components", "_mobile-action-bar.css"));
        return stripComments
            ? System.Text.RegularExpressions.Regex.Replace(css, @"/\*.*?\*/", string.Empty,
                System.Text.RegularExpressions.RegexOptions.Singleline)
            : css;
    }

    private static string RepoPath(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
            throw new InvalidOperationException("Repository root was not found.");

        return Path.Combine([directory.FullName, .. parts]);
    }
}

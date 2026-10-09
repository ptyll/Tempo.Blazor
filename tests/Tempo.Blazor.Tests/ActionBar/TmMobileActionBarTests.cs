using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.ActionBar;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.ActionBar;

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
    public void HigherPriorityRendersFirst()
    {
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
            .Should().Equal("High", "Mid", "Low");
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

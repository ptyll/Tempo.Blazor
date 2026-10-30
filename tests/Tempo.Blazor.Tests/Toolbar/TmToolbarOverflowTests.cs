using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Buttons;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Components.Toolbar;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Toolbar;

/// <summary>
/// F4: <see cref="TmToolbar"/> <c>Overflow=Menu</c>. The buttons register with the toolbar; a button
/// that does not fit is STILL rendered on the bar (collapsed: invisible, out of flow, inert — so its
/// width stays measurable) AND offered in the "More" menu; <c>OverflowOnly</c> buttons are only in the
/// menu. How many fit comes from tm-toolbar.js (<c>OnFitChanged</c>); WHICH leave is the shared
/// <c>ActionOverflowLayout</c>. These tests drive the count directly — the real measurement is the E2E lane.
/// </summary>
public class TmToolbarOverflowTests : LocalizationTestBase
{
    private const string ToolbarModule = "./_content/Tempo.Blazor/js/tm-toolbar.js";

    private sealed record Spec(
        string Text,
        ToolbarButtonPriority Priority = ToolbarButtonPriority.Primary,
        bool Disabled = false,
        ButtonVariant Variant = ButtonVariant.Ghost,
        EventCallback OnClick = default,
        string? Icon = "plus",
        bool? Pressed = null,
        bool PressedMixed = false);

    private BunitJSModuleInterop SetupModule()
    {
        var module = JSInterop.SetupModule(ToolbarModule);
        module.SetupVoid("attach", _ => true).SetVoidResult();
        module.SetupVoid("detach", _ => true).SetVoidResult();
        return module;
    }

    private static RenderFragment Buttons(params Spec[] specs) => builder =>
    {
        for (var i = 0; i < specs.Length; i++)
        {
            var spec = specs[i];
            builder.OpenComponent<TmToolbarButton>(i);
            builder.AddComponentParameter(0, nameof(TmToolbarButton.Text), spec.Text);
            builder.AddComponentParameter(1, nameof(TmToolbarButton.Icon), spec.Icon);
            builder.AddComponentParameter(2, nameof(TmToolbarButton.Priority), spec.Priority);
            builder.AddComponentParameter(3, nameof(TmToolbarButton.Disabled), spec.Disabled);
            builder.AddComponentParameter(4, nameof(TmToolbarButton.Variant), spec.Variant);
            builder.AddComponentParameter(5, nameof(TmToolbarButton.OnClick), spec.OnClick);
            builder.AddComponentParameter(6, nameof(TmToolbarButton.Pressed), spec.Pressed);
            builder.AddComponentParameter(7, nameof(TmToolbarButton.PressedMixed), spec.PressedMixed);
            builder.SetKey(spec.Text);
            builder.CloseComponent();
        }
    };

    private IRenderedComponent<TmToolbar> RenderToolbar(
        RenderFragment buttons,
        ToolbarOverflow overflow = ToolbarOverflow.Menu,
        Action<ComponentParameterCollectionBuilder<TmToolbar>>? configure = null,
        TmLayoutMode viewport = TmLayoutMode.Desktop)
        => Render<TmToolbar>(p =>
        {
            p.Add(c => c.Overflow, overflow)
             .Add(c => c.ChildContent, buttons)
             .AddCascadingValue(TmLayoutScopes.Viewport, new TmLayoutContext(TmLayoutMode.Auto, viewport));
            configure?.Invoke(p);
        });

    private static IReadOnlyList<string> BarButtons(IRenderedComponent<TmToolbar> cut)
        => cut.FindAll("button.tm-toolbar-btn").Select(b => b.TextContent.Trim()).ToList();

    private static IReadOnlyList<string> MenuLabels(IRenderedComponent<TmToolbar> cut)
        => cut.FindAll("[role='menu'] [role='menuitem']").Select(i => i.TextContent.Trim()).ToList();

    private static void OpenMenu(IRenderedComponent<TmToolbar> cut) => cut.Find("button.tm-toolbar-more").Click();

    [Fact]
    public void OverflowOnlyItems_AlwaysInMoreMenu()
    {
        SetupModule();
        var cut = RenderToolbar(Buttons(
            new Spec("Alpha"), new Spec("Hidden", ToolbarButtonPriority.OverflowOnly), new Spec("Gamma")));

        cut.WaitForAssertion(() => cut.FindAll("button.tm-toolbar-more").Should().HaveCount(1));
        BarButtons(cut).Should().Equal("Alpha", "Gamma");
        OpenMenu(cut);
        MenuLabels(cut).Should().Equal("Hidden");
    }

    [Fact]
    public void OverflowOnly_NeverCountsAgainstTheMeasuredRoom()
    {
        SetupModule();
        var cut = RenderToolbar(Buttons(
            new Spec("Alpha"), new Spec("Hidden", ToolbarButtonPriority.OverflowOnly), new Spec("Gamma")));
        cut.WaitForAssertion(() => cut.FindAll("button.tm-toolbar-more").Should().HaveCount(1));

        cut.InvokeAsync(() => cut.Instance.OnFitChanged(2));

        cut.FindAll(".tm-toolbar-item--collapsed").Should().BeEmpty("two room, two Auto buttons");
        OpenMenu(cut);
        MenuLabels(cut).Should().Equal("Hidden");
    }

    [Fact]
    public void SecondaryItems_RenderedInBothPlaces_WithCssHooks()
    {
        SetupModule();
        var cut = RenderToolbar(Buttons(
            new Spec("Alpha"), new Spec("Beta", ToolbarButtonPriority.Secondary),
            new Spec("Gamma"), new Spec("Delta", ToolbarButtonPriority.Secondary)));
        cut.WaitForAssertion(() => cut.FindAll("[data-tm-toolbar-item]").Should().HaveCount(4));

        cut.InvokeAsync(() => cut.Instance.OnFitChanged(3));

        // On the bar: all four stay in the DOM (the collapsed one keeps its measurable width) ...
        BarButtons(cut).Should().Equal("Alpha", "Beta", "Gamma", "Delta");
        var collapsed = cut.FindAll(".tm-toolbar-item--collapsed");
        collapsed.Should().ContainSingle("the LATER of the two Secondary buttons leaves first");
        collapsed[0].TextContent.Trim().Should().Be("Delta");
        collapsed[0].HasAttribute("inert").Should().BeTrue("a collapsed copy must not be focusable or clickable");
        collapsed[0].GetAttribute("aria-hidden").Should().Be("true");
        collapsed[0].GetAttribute("data-rank").Should().Be("1");
        // ... and the same button is in the menu.
        OpenMenu(cut);
        MenuLabels(cut).Should().Equal("Delta");
    }

    [Fact]
    public void MenuKeepsTheWrittenOrder_NotThePriorityOrder()
    {
        SetupModule();
        var cut = RenderToolbar(Buttons(
            new Spec("A", ToolbarButtonPriority.Secondary), new Spec("B"), new Spec("C", ToolbarButtonPriority.Secondary),
            new Spec("D", ToolbarButtonPriority.OverflowOnly)));
        cut.WaitForAssertion(() => cut.FindAll("[data-tm-toolbar-item]").Should().HaveCount(4, "three bar buttons + the OverflowOnly position marker"));

        cut.InvokeAsync(() => cut.Instance.OnFitChanged(1));

        OpenMenu(cut);
        MenuLabels(cut).Should().Equal("A", "C", "D");
    }

    [Fact]
    public void KeyboardNavigation_SkipsHiddenItems()
    {
        // The arrow keys are JS (tm-toolbar.js, node-tested against these very hooks): it visits
        // buttons that are enabled and NOT inside .tm-toolbar-item--collapsed / the menu. The markup
        // contract that makes the skip possible: role, name, the collapsed hook and inert.
        SetupModule();
        var cut = RenderToolbar(Buttons(
            new Spec("Alpha"), new Spec("Beta", ToolbarButtonPriority.Secondary), new Spec("Gamma")),
            configure: p => p.Add(c => c.Title, "Orders"));
        cut.WaitForAssertion(() => cut.FindAll("[data-tm-toolbar-item]").Should().HaveCount(3));
        cut.InvokeAsync(() => cut.Instance.OnFitChanged(2));

        var root = cut.Find(".tm-toolbar");
        root.GetAttribute("role").Should().Be("toolbar");
        root.GetAttribute("aria-label").Should().Be("Orders");
        var collapsed = cut.Find(".tm-toolbar-item--collapsed");
        collapsed.TextContent.Trim().Should().Be("Beta");
        collapsed.HasAttribute("inert").Should().BeTrue();
        cut.FindAll("button.tm-toolbar-btn:not(.tm-toolbar-item--collapsed)").Should().HaveCount(2);
    }

    [Fact]
    public void ToolbarName_FallsBackToTheLocalizedDefault_AndExplicitAriaLabelWins()
    {
        SetupModule();
        var unnamed = RenderToolbar(Buttons(new Spec("Alpha")));
        unnamed.Find(".tm-toolbar").GetAttribute("aria-label").Should().Be("Toolbar");

        var named = RenderToolbar(Buttons(new Spec("Alpha")), configure: p => p
            .Add(c => c.Title, "Orders").Add(c => c.AriaLabel, "Order tools"));
        named.Find(".tm-toolbar").GetAttribute("aria-label").Should().Be("Order tools");
    }

    [Fact]
    public void WiderRoom_ExpandsTheCollapsedButtons_AndDropsTheTriggerWhenNothingOverflows()
    {
        SetupModule();
        var cut = RenderToolbar(Buttons(new Spec("Alpha"), new Spec("Beta", ToolbarButtonPriority.Secondary)));
        cut.WaitForAssertion(() => cut.FindAll("[data-tm-toolbar-item]").Should().HaveCount(2));
        cut.InvokeAsync(() => cut.Instance.OnFitChanged(1));
        cut.FindAll("button.tm-toolbar-more").Should().HaveCount(1);

        cut.InvokeAsync(() => cut.Instance.OnFitChanged(2));

        cut.FindAll(".tm-toolbar-item--collapsed").Should().BeEmpty();
        cut.FindAll("button.tm-toolbar-more").Should().BeEmpty("nothing overflows any more");
    }

    [Fact]
    public void MenuItem_InvokesTheButtonsOnClick_OnceAndClosesTheMenu()
    {
        SetupModule();
        var clicks = 0;
        var cut = RenderToolbar(Buttons(
            new Spec("Alpha"),
            new Spec("Hidden", ToolbarButtonPriority.OverflowOnly, OnClick: EventCallback.Factory.Create(this, () => clicks++))));
        cut.WaitForAssertion(() => cut.FindAll("button.tm-toolbar-more").Should().HaveCount(1));

        OpenMenu(cut);
        cut.Find("[role='menuitem']").Click();

        cut.WaitForAssertion(() => clicks.Should().Be(1));
        cut.WaitForAssertion(() => cut.FindAll("[role='menu']").Should().BeEmpty());
    }

    [Fact]
    public void DisabledAndDangerButtons_KeepTheirStateInTheMenu()
    {
        SetupModule();
        var cut = RenderToolbar(Buttons(
            new Spec("Off", ToolbarButtonPriority.OverflowOnly, Disabled: true),
            new Spec("Drop", ToolbarButtonPriority.OverflowOnly, Variant: ButtonVariant.Danger)));
        cut.WaitForAssertion(() => cut.FindAll("button.tm-toolbar-more").Should().HaveCount(1));

        OpenMenu(cut);

        cut.FindAll("[role='menuitem']").Single(i => i.TextContent.Contains("Off")).HasAttribute("disabled").Should().BeTrue();
        cut.FindAll("[role='menuitem']").Single(i => i.TextContent.Contains("Drop")).ClassList
            .Should().Contain("tm-toolbar-more__item--danger");
    }

    [Fact]
    public void OnlyDisabledOverflowItems_DisableTheTrigger()
    {
        SetupModule();
        var cut = RenderToolbar(Buttons(new Spec("Alpha"), new Spec("Off", ToolbarButtonPriority.OverflowOnly, Disabled: true)));
        cut.WaitForAssertion(() => cut.FindAll("button.tm-toolbar-more").Should().HaveCount(1));

        cut.Find("button.tm-toolbar-more").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void RemovingAButton_RemovesItFromTheMenu()
    {
        SetupModule();
        var show = true;
        RenderFragment content = builder =>
        {
            builder.AddContent(0, Buttons(new Spec("Alpha"), new Spec("Gone", ToolbarButtonPriority.OverflowOnly)));
            if (show) builder.AddContent(1, Buttons(new Spec("Extra", ToolbarButtonPriority.OverflowOnly)));
        };
        var cut = RenderToolbar(content);
        cut.WaitForAssertion(() => cut.FindAll("button.tm-toolbar-more").Should().HaveCount(1));
        OpenMenu(cut);
        MenuLabels(cut).Should().Equal("Gone", "Extra");

        show = false;
        cut.Render(p => p.Add(c => c.ChildContent, content));

        cut.WaitForAssertion(() => MenuLabels(cut).Should().Equal("Gone"));
    }

    [Fact]
    public void OverflowNone_RendersEverythingAsWritten_WithNoTrigger()
    {
        SetupModule();
        var cut = RenderToolbar(
            Buttons(new Spec("Alpha"), new Spec("Hidden", ToolbarButtonPriority.OverflowOnly), new Spec("Beta", ToolbarButtonPriority.Secondary)),
            ToolbarOverflow.None);

        BarButtons(cut).Should().Equal("Alpha", "Hidden", "Beta");
        cut.FindAll("button.tm-toolbar-more").Should().BeEmpty();
        cut.FindAll(".tm-toolbar-item--collapsed").Should().BeEmpty();
    }

    [Fact]
    public void OverflowLabel_NamesTheTrigger_FallingBackToTheLocalizedMore()
    {
        SetupModule();
        var defaulted = RenderToolbar(Buttons(new Spec("Alpha"), new Spec("Hidden", ToolbarButtonPriority.OverflowOnly)));
        defaulted.WaitForAssertion(() => defaulted.FindAll("button.tm-toolbar-more").Should().HaveCount(1));
        defaulted.Find("button.tm-toolbar-more").GetAttribute("aria-label").Should().Be("More");

        var custom = RenderToolbar(
            Buttons(new Spec("Alpha"), new Spec("Hidden", ToolbarButtonPriority.OverflowOnly)),
            configure: p => p.Add(c => c.OverflowLabel, "More tools"));
        custom.WaitForAssertion(() => custom.FindAll("button.tm-toolbar-more").Should().HaveCount(1));
        custom.Find("button.tm-toolbar-more").GetAttribute("aria-label").Should().Be("More tools");
    }

    [Fact]
    public void MoreMenu_OnAPhoneViewport_IsTheSharedBottomSheet_AndOverflowPresentationPopoverOptsOut()
    {
        SetupModule();
        var buttons = Buttons(new Spec("Alpha"), new Spec("Hidden", ToolbarButtonPriority.OverflowOnly));

        var sheet = RenderToolbar(buttons, viewport: TmLayoutMode.Mobile);
        sheet.WaitForAssertion(() => sheet.FindAll("button.tm-toolbar-more").Should().HaveCount(1));
        OpenMenu(sheet);
        sheet.FindAll(".tm-overlay-panel-sheet").Should().HaveCount(1, "F3 default: Auto presents as a sheet on a phone");

        var popover = RenderToolbar(buttons, viewport: TmLayoutMode.Mobile,
            configure: p => p.Add(c => c.OverflowPresentation, PanelPresentation.Popover));
        popover.WaitForAssertion(() => popover.FindAll("button.tm-toolbar-more").Should().HaveCount(1));
        OpenMenu(popover);
        popover.FindAll(".tm-overlay-panel-sheet").Should().BeEmpty("an editor toolbar keeps its anchor focus (docs/overlays.md)");
        popover.FindAll("[role='menu']").Should().HaveCount(1);
    }

    [Fact]
    public async Task Module_IsAttachedOncePerToolbar_WithTheOverflowFlag_AndDetachedOnDispose()
    {
        var module = SetupModule();
        var cut = RenderToolbar(Buttons(new Spec("Alpha")));

        cut.WaitForAssertion(() => module.Invocations["attach"].Should().ContainSingle());
        cut.Render();
        cut.Render();
        module.Invocations["attach"].Should().ContainSingle("re-renders must not re-attach");
        System.Text.Json.JsonSerializer.Serialize(module.Invocations["attach"].Single().Arguments[2])
            .Should().Contain("\"overflow\":true", "Overflow=Menu measures");

        await cut.InvokeAsync(() => cut.Instance.DisposeAsync().AsTask());

        module.Invocations["detach"].Should().ContainSingle();
    }

    [Fact]
    public void OverflowNone_StillAttaches_ForTheRovingTabindex_ButDoesNotMeasure()
    {
        var module = SetupModule();
        RenderToolbar(Buttons(new Spec("Alpha")), ToolbarOverflow.None);

        var attach = module.Invocations["attach"];
        attach.Should().ContainSingle();
        System.Text.Json.JsonSerializer.Serialize(attach.Single().Arguments[2]).Should().Contain("\"overflow\":false");
    }

    [Fact]
    public void PinnedButton_StaysOnTheBar_NeverCollapsesAndNeverEntersTheMenu()
    {
        // Q1=A: the trailing Save / primary CTA is Pinned - fixed width on the bar, not counted against the room.
        SetupModule();
        var cut = RenderToolbar(Buttons(
            new Spec("Alpha"), new Spec("Beta", ToolbarButtonPriority.Secondary), new Spec("Save", ToolbarButtonPriority.Pinned)));
        cut.WaitForAssertion(() => cut.FindAll("[data-tm-toolbar-item]").Should().HaveCount(3));

        cut.InvokeAsync(() => cut.Instance.OnFitChanged(0));

        var save = cut.FindAll("button.tm-toolbar-btn").Single(b => b.TextContent.Contains("Save"));
        save.ClassList.Should().NotContain("tm-toolbar-item--collapsed");
        save.HasAttribute("inert").Should().BeFalse();
        save.GetAttribute("data-pin").Should().Be("never", "tm-toolbar.js measures a pinned button as fixed width");
        OpenMenu(cut);
        MenuLabels(cut).Should().Equal("Alpha", "Beta").And.NotContain("Save");
    }

    private static string[] ItemIdsInDomOrder(IRenderedComponent<TmToolbar> cut)
        => cut.FindAll("[data-tm-toolbar-item]").Select(i => i.GetAttribute("data-tm-toolbar-item")!).ToArray();

    [Fact]
    public void EveryBarButton_RendersItsStableMenuId_AsTheItemMarker()
    {
        // H3: the marker carries the id tm-toolbar.js reports back, so C# can re-sort to the DOM order.
        SetupModule();
        var cut = RenderToolbar(Buttons(new Spec("Alpha"), new Spec("Beta")));

        var ids = ItemIdsInDomOrder(cut);
        ids.Should().HaveCount(2).And.OnlyContain(id => !string.IsNullOrEmpty(id)).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void MenuOrderAndTieBreak_FollowTheDomOrderReportedByJs_NotTheRegistrationOrder()
    {
        // H3 (code M2 + architect M1): A and C register first; B is a conditional button inserted BETWEEN them
        // later. Registration order is A, C, B - the DOM (and what JS counts) is A, B, C. With room for two
        // of three equal-rank buttons the LATER one in the bar leaves: C, not B.
        SetupModule();
        var includeB = false;
        RenderFragment content = builder =>
        {
            builder.AddContent(0, Buttons(new Spec("A")));
            if (includeB)
            {
                builder.OpenComponent<TmToolbarButton>(1);
                builder.AddComponentParameter(0, nameof(TmToolbarButton.Text), "B");
                builder.AddComponentParameter(1, nameof(TmToolbarButton.Icon), "plus");
                builder.SetKey("B");
                builder.CloseComponent();
            }
            builder.OpenComponent<TmToolbarButton>(2);
            builder.AddComponentParameter(0, nameof(TmToolbarButton.Text), "C");
            builder.AddComponentParameter(1, nameof(TmToolbarButton.Icon), "plus");
            builder.SetKey("C");
            builder.CloseComponent();
        };
        var cut = RenderToolbar(content);
        cut.WaitForAssertion(() => cut.FindAll("[data-tm-toolbar-item]").Should().HaveCount(2));

        includeB = true;
        cut.Render(p => p.Add(c => c.ChildContent, content));
        cut.WaitForAssertion(() => cut.FindAll("[data-tm-toolbar-item]").Should().HaveCount(3));
        BarButtons(cut).Should().Equal("A", "B", "C");

        cut.InvokeAsync(() => cut.Instance.OnFitChanged(2, ItemIdsInDomOrder(cut)));

        cut.FindAll(".tm-toolbar-item--collapsed").Select(b => b.TextContent.Trim()).Should().Equal(new[] { "C" },
            "ties drop the LAST button of the bar, which is C once the order is the DOM order");
        OpenMenu(cut);
        MenuLabels(cut).Should().Equal("C");
    }

    [Fact]
    public void MenuOrder_IsTheDomOrder_WhenSeveralButtonsLeave()
    {
        SetupModule();
        var swapped = false;
        RenderFragment content = builder =>
        {
            var specs = swapped
                ? new[] { new Spec("B"), new Spec("A"), new Spec("C") }
                : new[] { new Spec("A"), new Spec("B"), new Spec("C") };
            builder.AddContent(0, Buttons(specs));
        };
        var cut = RenderToolbar(content);
        cut.WaitForAssertion(() => cut.FindAll("[data-tm-toolbar-item]").Should().HaveCount(3));

        swapped = true;
        cut.Render(p => p.Add(c => c.ChildContent, content));
        cut.InvokeAsync(() => cut.Instance.OnFitChanged(1, ItemIdsInDomOrder(cut)));

        OpenMenu(cut);
        MenuLabels(cut).Should().Equal("A", "C");
    }

    [Fact]
    public void ButtonsInsideAGroupWrapper_RegisterAndCollapseLikeDirectChildren()
    {
        // H2: the group marker (role=group + data-tm-toolbar-group) is a layout wrapper only; the buttons inside
        // still register with the toolbar, are marked as items and reach the More menu.
        SetupModule();
        RenderFragment content = builder =>
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "role", "group");
            builder.AddAttribute(2, "data-tm-toolbar-group", "");
            builder.AddContent(3, Buttons(new Spec("One"), new Spec("Two"), new Spec("Three")));
            builder.CloseElement();
        };
        var cut = RenderToolbar(content);
        cut.WaitForAssertion(() => cut.FindAll("[role='group'] [data-tm-toolbar-item]").Should().HaveCount(3));

        cut.InvokeAsync(() => cut.Instance.OnFitChanged(1, ItemIdsInDomOrder(cut)));

        cut.FindAll("[role='group'] .tm-toolbar-item--collapsed").Should().HaveCount(2);
        OpenMenu(cut);
        MenuLabels(cut).Should().Equal("Two", "Three");
    }

    [Fact]
    public void OverflowOnlyButton_KeepsItsWrittenPositionInTheMenu_WhenJsReportsTheDomOrder()
    {
        // Review round 2 (I1): [A, X(OverflowOnly), B] - X has no bar button, so JS used to never report its id and
        // ApplyDomOrder pushed it to the end: the menu read "B, X" instead of the written "X, B".
        SetupModule();
        var cut = RenderToolbar(Buttons(
            new Spec("A", ToolbarButtonPriority.Pinned), new Spec("X", ToolbarButtonPriority.OverflowOnly), new Spec("B", ToolbarButtonPriority.Secondary)));
        cut.WaitForAssertion(() => cut.FindAll("[data-tm-toolbar-item]").Should().HaveCount(3));

        cut.InvokeAsync(() => cut.Instance.OnFitChanged(0, ItemIdsInDomOrder(cut)));

        OpenMenu(cut);
        MenuLabels(cut).Should().Equal("X", "B");
    }

    [Fact]
    public void OverflowOnlyButton_LeavesAHiddenZeroWidthPositionMarker_NotAControl()
    {
        SetupModule();
        var cut = RenderToolbar(Buttons(new Spec("A"), new Spec("X", ToolbarButtonPriority.OverflowOnly)));

        var marker = cut.FindAll("[data-tm-toolbar-item]").Single(e => e.TagName != "BUTTON");
        marker.HasAttribute("hidden").Should().BeTrue("a hidden marker takes no room and is not focusable");
        marker.GetAttribute("data-pin").Should().Be("always", "tm-toolbar.js records its id but never measures it");
        marker.TextContent.Should().BeEmpty();
        BarButtons(cut).Should().Equal("A");
    }

    [Fact]
    public void OverflowOnlyButton_WrittenFirstOrLast_KeepsThatPositionInTheMenu()
    {
        SetupModule();
        var cut = RenderToolbar(Buttons(
            new Spec("F", ToolbarButtonPriority.OverflowOnly), new Spec("A", ToolbarButtonPriority.Pinned),
            new Spec("B", ToolbarButtonPriority.Secondary), new Spec("L", ToolbarButtonPriority.OverflowOnly)));
        cut.WaitForAssertion(() => cut.FindAll("[data-tm-toolbar-item]").Should().HaveCount(4));

        cut.InvokeAsync(() => cut.Instance.OnFitChanged(0, ItemIdsInDomOrder(cut)));

        OpenMenu(cut);
        MenuLabels(cut).Should().Equal("F", "B", "L");
    }

    // ── C2: a pressed button keeps its state in the More menu ────────────────────────────────────

    private static IReadOnlyList<string> MenuCheckboxLabels(IRenderedComponent<TmToolbar> cut)
        => cut.FindAll("[role='menu'] [role='menuitemcheckbox']").Select(i => i.TextContent.Trim()).ToList();

    [Theory]
    [InlineData(true, false, "true")]
    [InlineData(false, false, "false")]
    [InlineData(false, true, "mixed")]
    public void PressedButton_InMore_IsMenuItemCheckbox_WithAriaChecked(bool pressed, bool mixed, string expected)
    {
        SetupModule();
        var cut = RenderToolbar(Buttons(
            new Spec("Alpha"),
            new Spec("Pan", ToolbarButtonPriority.OverflowOnly, Pressed: pressed, PressedMixed: mixed)));
        cut.WaitForAssertion(() => cut.FindAll("button.tm-toolbar-more").Should().HaveCount(1));

        OpenMenu(cut);

        var boxes = cut.FindAll("[role='menu'] [role='menuitemcheckbox']");
        boxes.Should().ContainSingle("the collapsed toggle is a menuitemcheckbox, not a menuitem");
        boxes[0].TextContent.Trim().Should().Be("Pan");
        boxes[0].GetAttribute("aria-checked").Should().Be(expected);
        // and the toggle is no longer a plain menuitem: the only menu entry is the checkbox
        // (Alpha fits on the bar at the unmeasured budget and never enters the menu).
        MenuLabels(cut).Should().BeEmpty();
    }

    [Fact]
    public void TogglingPressed_WhileCollapsed_RefreshesTheMenuEntry()
    {
        // The host owns the state: flipping Pressed while the button is collapsed must reach the open
        // menu through the toolbar's ButtonChanged snapshot (Pressed/PressedMixed are part of it).
        SetupModule();
        var pressed = true;
        RenderFragment content = builder => builder.AddContent(0, Buttons(
            new Spec("Alpha"),
            new Spec("Pan", ToolbarButtonPriority.Secondary, Pressed: pressed)));
        var cut = RenderToolbar(content);
        cut.WaitForAssertion(() => cut.FindAll("[data-tm-toolbar-item]").Should().HaveCount(2));
        cut.InvokeAsync(() => cut.Instance.OnFitChanged(0));
        OpenMenu(cut);
        cut.Find("[role='menu'] [role='menuitemcheckbox']").GetAttribute("aria-checked").Should().Be("true");

        pressed = false;
        cut.Render(p => p.Add(c => c.ChildContent, content));

        cut.Find("[role='menu'] [role='menuitemcheckbox']").GetAttribute("aria-checked").Should().Be("false");
    }

    // ── C3: TmToolbarGroup — the collapsed members keep the group as a labelled menu section ─────

    private static RenderFragment GroupContent(string label, RenderFragment buttons) => builder =>
    {
        builder.OpenComponent<TmToolbarGroup>(0);
        builder.AddComponentParameter(1, nameof(TmToolbarGroup.Label), label);
        builder.AddComponentParameter(2, nameof(TmToolbarGroup.ChildContent), buttons);
        builder.CloseComponent();
    };

    private static IReadOnlyList<string> MenuGroupNames(IRenderedComponent<TmToolbar> cut)
        => cut.FindAll("[role='menu'] [role='group']").Select(g => g.GetAttribute("aria-label") ?? string.Empty).ToList();

    private static IReadOnlyList<string> MenuGroupItemLabels(IRenderedComponent<TmToolbar> cut)
        => cut.FindAll("[role='menu'] [role='group'] [role='menuitem'], [role='menu'] [role='group'] [role='menuitemcheckbox']")
            .Select(i => i.TextContent.Trim()).ToList();

    [Fact]
    public void CollapsedMembersOfATmToolbarGroup_KeepTheGroupInMore()
    {
        SetupModule();
        var cut = RenderToolbar(GroupContent("Edit", Buttons(
            new Spec("One", ToolbarButtonPriority.Secondary),
            new Spec("Two", ToolbarButtonPriority.Secondary),
            new Spec("Three", ToolbarButtonPriority.OverflowOnly))));
        cut.WaitForAssertion(() => cut.FindAll("[data-tm-toolbar-item]").Should().HaveCount(3));

        // The clamp keeps One on the bar; Two and Three collapse — both stay inside the group section.
        cut.InvokeAsync(() => cut.Instance.OnFitChanged(0, ItemIdsInDomOrder(cut)));

        OpenMenu(cut);
        var group = cut.Find("[role='menu'] [role='group']");
        group.GetAttribute("aria-label").Should().Be("Edit");
        group.QuerySelector(".tm-toolbar-more__group-label")!.TextContent.Should().Be("Edit", "the header is visible");
        group.QuerySelector(".tm-toolbar-more__group-label")!.GetAttribute("aria-hidden").Should().Be("true", "and decoration only");
        MenuGroupItemLabels(cut).Should().Equal(new[] { "Two", "Three" }, "the collapsed members, in the written order, inside the group");
    }

    [Fact]
    public void GroupedAndUngroupedButtons_FormSeparateRuns_InDomOrder()
    {
        SetupModule();
        RenderFragment content = builder =>
        {
            builder.AddContent(0, GroupContent("Edit", Buttons(
                new Spec("One", ToolbarButtonPriority.Secondary),
                new Spec("Two", ToolbarButtonPriority.Secondary))));
            builder.AddContent(1, Buttons(new Spec("Solo", ToolbarButtonPriority.Secondary)));
        };
        var cut = RenderToolbar(content);
        cut.WaitForAssertion(() => cut.FindAll("[data-tm-toolbar-item]").Should().HaveCount(3));

        // The clamp keeps One on the bar: the menu runs are [Two (Edit)], [Solo (ungrouped)].
        cut.InvokeAsync(() => cut.Instance.OnFitChanged(0, ItemIdsInDomOrder(cut)));
        OpenMenu(cut);

        MenuGroupNames(cut).Should().Equal(new[] { "Edit" });
        MenuGroupItemLabels(cut).Should().Equal(new[] { "Two" });
        var roles = cut.Find("[role='menu'] .tm-toolbar-more__menu").Children.Select(child => child.GetAttribute("role")).ToList();
        roles.Should().Equal(new[] { "group", "separator", "menuitem" }, "the ungrouped Solo is a separate run after the group");
        roles.Should().NotContain(role => role == null, "every run boundary is marked");
    }

    [Fact]
    public void RenamingAGroup_RelabelsItsMoreSection()
    {
        // The label change reaches the group through an inner component (its own state), so the
        // toolbar itself does not re-render: the group must push the relabel through ButtonChanged.
        SetupModule();
        var label = "Edit";
        RenderFragment content = builder => builder.AddContent(0, (RenderFragment)(inner =>
        {
            inner.OpenComponent<GroupSlot>(0);
            inner.AddComponentParameter(1, nameof(GroupSlot.Label), label);
            inner.CloseComponent();
        }));
        var cut = RenderToolbar(content);
        cut.WaitForAssertion(() => cut.FindAll("[data-tm-toolbar-item]").Should().HaveCount(1));
        OpenMenu(cut);
        cut.Find("[role='menu'] [role='group']").GetAttribute("aria-label").Should().Be("Edit");

        label = "Nástroje";
        cut.FindComponent<GroupSlot>().Find("button.group-slot-flip").Click();

        cut.Find("[role='menu'] [role='group']").GetAttribute("aria-label").Should().Be("Nástroje",
            "the open menu re-labelled without the toolbar re-rendering");
    }

    /// <summary>
    /// The placement contract the More panel opens with: it may FLIP to the other side of its trigger (on
    /// by default) and must CAP its height to the room on the resolved side and scroll, so a menu taller
    /// than its viewport can never paint entries outside it. The shared placement knobs live on
    /// <c>TmOverlayPanel</c> (<c>Flip</c> / <c>Shift</c> / <c>ConstrainHeight</c>); without
    /// <c>ConstrainHeight</c> a 768 px tablet viewport loses the tail of the spreadsheet's primary-row
    /// More menu and a real tablet user cannot reach those commands (phase 8 regression, f25a9d47).
    /// </summary>
    [Fact]
    public void MoreMenu_AsksThePanelToStayInsideTheViewport()
    {
        SetupModule();
        var overlay = JSInterop.SetupModule("./_content/Tempo.Blazor/js/overlay.js");
        overlay.SetupVoid("open", _ => true).SetVoidResult();
        overlay.SetupVoid("update", _ => true).SetVoidResult();
        overlay.SetupVoid("close", _ => true).SetVoidResult();

        var cut = RenderToolbar(Buttons(
            new Spec("Hidden", ToolbarButtonPriority.OverflowOnly), new Spec("AlsoHidden", ToolbarButtonPriority.OverflowOnly)));
        cut.WaitForAssertion(() => cut.FindAll("button.tm-toolbar-more").Should().HaveCount(1));
        OpenMenu(cut);

        // open(key, panel, anchor, dotNetRef, options) — options is argument index 4.
        var options = System.Text.Json.JsonSerializer.Serialize(
            overlay.Invocations["open"].Should().ContainSingle().Subject.Arguments[4]);
        options.Should().Contain("\"constrainHeight\":true",
            "the More panel must cap its height to the room and scroll instead of painting entries off-screen");
        options.Should().Contain("\"flip\":true",
            "a panel that does not fit below must be allowed to flip above its trigger");
        options.Should().Contain("\"shift\":true");
    }

    /// <summary>A component between the toolbar and the group that owns the label state: re-rendering it must not re-render the toolbar.</summary>
    private sealed class GroupSlot : Microsoft.AspNetCore.Components.ComponentBase
    {
        [Parameter] public string Label { get; set; } = "Edit";

        private string _label = "Edit";
        private bool _armed;

        protected override void OnParametersSet()
        {
            if (!_armed)
            {
                _label = Label;
                _armed = true;
            }
        }

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "button");
            builder.AddAttribute(1, "class", "group-slot-flip");
            builder.AddAttribute(2, "onclick", Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () =>
            {
                _label = "Nástroje";
            }));
            builder.CloseElement();
            builder.OpenComponent<TmToolbarGroup>(3);
            builder.AddComponentParameter(4, nameof(TmToolbarGroup.Label), _label);
            builder.AddComponentParameter(5, nameof(TmToolbarGroup.ChildContent), (Microsoft.AspNetCore.Components.RenderFragment)(child =>
            {
                child.OpenComponent<TmToolbarButton>(0);
                child.AddComponentParameter(1, nameof(TmToolbarButton.Text), "X");
                child.AddComponentParameter(2, nameof(TmToolbarButton.Priority), ToolbarButtonPriority.OverflowOnly);
                child.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }
}

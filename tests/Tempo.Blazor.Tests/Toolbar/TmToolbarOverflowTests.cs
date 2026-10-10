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
        string? Icon = "plus");

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
        cut.WaitForAssertion(() => cut.FindAll("[data-tm-toolbar-item]").Should().HaveCount(3));

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
}
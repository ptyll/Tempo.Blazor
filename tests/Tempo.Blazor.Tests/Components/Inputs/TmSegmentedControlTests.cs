using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.Inputs;

/// <summary>
/// Plan ff043b2e phase 8: <c>TmSegmentedControl&lt;TValue&gt;</c> — a value out of a small set, drawn as a
/// segmented tray. These facts pin the two APG patterns it offers (a <c>radiogroup</c> of roving-tabindex
/// radios, and a <c>tablist</c> whose tabs point at their panels), the keyboard model (arrows wrap and skip
/// disabled options, Home/End jump, native buttons own Enter/Space), the sliding indicator that is measured
/// in the browser and falls back to an attribute-driven background without JS, the three sizes, the
/// full-width layout, the overflow scroller and the token/forced-colors contract of its stylesheet.
/// </summary>
public class TmSegmentedControlTests : LocalizationTestBase
{
    /// <summary>The strict audit's literal detector: none of these may appear in the stylesheet.</summary>
    private static readonly System.Text.RegularExpressions.Regex ColorLiteral = new(
        @"#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(|(?<![\w-])(?:white|black)(?![\w-])",
        System.Text.RegularExpressions.RegexOptions.Compiled,
        TimeSpan.FromSeconds(5));

    // ---------- fixtures ----------

    private static IReadOnlyList<SegmentedOption<string>> Days() =>
    [
        new() { Label = "Day", Value = "day" },
        new() { Label = "Week", Value = "week" },
        new() { Label = "Month", Value = "month" }
    ];

    private IRenderedComponent<TmSegmentedControl<string>> RenderControl(
        Action<ComponentParameterCollectionBuilder<TmSegmentedControl<string>>> configure)
        => Render<TmSegmentedControl<string>>(configure);

    private static AngleSharp.Dom.IElement[] Options(IRenderedComponent<TmSegmentedControl<string>> cut)
        => cut.FindAll(".tm-segmented__option").ToArray();

    private static KeyboardEventArgs Key(string key) => new() { Key = key };

    // ---------- structure and ARIA ----------

    [Fact]
    public void RendersOneRadioPerOption()
    {
        var cut = RenderControl(p => p
            .Add(c => c.Options, Days())
            .Add(c => c.Value, "week"));

        var options = Options(cut);

        options.Should().HaveCount(3);
        options.Should().OnlyContain(option => option.GetAttribute("role") == "radio");
        options.Select(option => option.TextContent.Trim())
            .Should().Equal("Day", "Week", "Month");
    }

    [Fact]
    public void TheSelectionCarriesAriaChecked_AndOnlyTheSelection()
    {
        var cut = RenderControl(p => p
            .Add(c => c.Options, Days())
            .Add(c => c.Value, "week"));

        Options(cut).Select(option => option.GetAttribute("aria-checked"))
            .Should().Equal("false", "true", "false");
    }

    [Fact]
    public void TheGroupIsARadiogroup_AndANamedOne()
    {
        var cut = RenderControl(p => p.Add(c => c.Options, Days()));

        var group = cut.Find("[role='radiogroup']");

        group.ClassList.Should().Contain("tm-segmented__track");
        cut.Find(".tm-segmented").GetAttribute("data-testid").Should().Be("segmented-control");
        group.GetAttribute("aria-label").Should().Be("Options",
            "a radiogroup without a name is announced as an anonymous group; the default is localized");
        group.GetAttribute("aria-orientation").Should().Be("horizontal");
    }

    [Fact]
    public void ALabelIsVisibleAndNamesTheGroup()
    {
        var cut = RenderControl(p => p
            .Add(c => c.Options, Days())
            .Add(c => c.Label, "Period"));

        var label = cut.Find(".tm-segmented__label");

        label.TextContent.Trim().Should().Be("Period");

        var group = cut.Find("[role='radiogroup']");
        group.GetAttribute("aria-labelledby").Should().Be(label.Id);
        group.GetAttribute("aria-label").Should().BeNull(
            "a visible label already names the group — an aria-label on top would make the accessible name differ from the visible one (WCAG 2.5.3)");
    }

    [Fact]
    public void ALabelThatIsOnlyWhitespace_CountsAsNoLabel()
    {
        var cut = RenderControl(p => p
            .Add(c => c.Options, Days())
            .Add(c => c.Label, "   "));

        cut.FindAll(".tm-segmented__label").Should().BeEmpty();
        cut.Find("[role='radiogroup']").GetAttribute("aria-label").Should().Be("Options");
    }

    [Fact]
    public void AriaLabelNamesTheGroupWhenThereIsNoVisibleLabel()
    {
        var cut = RenderControl(p => p
            .Add(c => c.Options, Days())
            .Add(c => c.AriaLabel, "Group by"));

        cut.Find("[role='radiogroup']").GetAttribute("aria-label").Should().Be("Group by");
    }

    [Fact]
    public void AnExternalLabelledByWins()
    {
        var cut = RenderControl(p => p
            .Add(c => c.Options, Days())
            .Add(c => c.Label, "Period")
            .Add(c => c.AriaLabelledBy, "page-heading"));

        var group = cut.Find("[role='radiogroup']");

        group.GetAttribute("aria-labelledby").Should().Be("page-heading");
        group.GetAttribute("aria-label").Should().BeNull();
    }

    // ---------- roving tabindex and keyboard ----------

    [Fact]
    public void TheSelectionIsTheSingleTabStop()
    {
        var cut = RenderControl(p => p
            .Add(c => c.Options, Days())
            .Add(c => c.Value, "week"));

        Options(cut).Select(option => option.GetAttribute("tabindex"))
            .Should().Equal("-1", "0", "-1");
    }

    [Fact]
    public void WithoutASelection_TheFirstEnabledOptionIsTheTabStop()
    {
        var cut = RenderControl(p => p.Add(c => c.Options,
        [
            new() { Label = "Day", Value = "day", IsDisabled = true },
            new() { Label = "Week", Value = "week" },
            new() { Label = "Month", Value = "month" }
        ]));

        Options(cut).Select(option => option.GetAttribute("tabindex"))
            .Should().Equal("-1", "0", "-1");
    }

    [Fact]
    public void ArrowRightSelectsTheNextOptionAndSkipsDisabledOnes()
    {
        string? changed = null;
        var cut = RenderControl(p => p
            .Add(c => c.Options,
            [
                new() { Label = "Day", Value = "day" },
                new() { Label = "Week", Value = "week", IsDisabled = true },
                new() { Label = "Month", Value = "month" }
            ])
            .Add(c => c.Value, "day")
            .Add(c => c.ValueChanged, (string? value) => changed = value));

        Options(cut)[0].KeyDown(Key("ArrowRight"));

        changed.Should().Be("month");
        Options(cut).Select(option => option.GetAttribute("aria-checked"))
            .Should().Equal("false", "false", "true");
        Options(cut)[2].GetAttribute("tabindex").Should().Be("0");
    }

    [Fact]
    public void ArrowRightWrapsToTheFirstOption()
    {
        string? changed = null;
        var cut = RenderControl(p => p
            .Add(c => c.Options, Days())
            .Add(c => c.Value, "month")
            .Add(c => c.ValueChanged, (string? value) => changed = value));

        Options(cut)[2].KeyDown(Key("ArrowRight"));

        changed.Should().Be("day");
    }

    [Fact]
    public void ArrowLeftWrapsToTheLastOption()
    {
        string? changed = null;
        var cut = RenderControl(p => p
            .Add(c => c.Options, Days())
            .Add(c => c.Value, "day")
            .Add(c => c.ValueChanged, (string? value) => changed = value));

        Options(cut)[0].KeyDown(Key("ArrowLeft"));

        changed.Should().Be("month");
    }

    [Theory]
    [InlineData("ArrowDown", "month")]
    [InlineData("ArrowUp", "day")]
    public void VerticalArrowsBehaveLikeHorizontal_SoBothOrientationsWork(string key, string expected)
    {
        string? changed = null;
        var cut = RenderControl(p => p
            .Add(c => c.Options, Days())
            .Add(c => c.Value, "week")
            .Add(c => c.ValueChanged, (string? value) => changed = value));

        Options(cut)[1].KeyDown(Key(key));

        changed.Should().Be(expected);
    }

    [Fact]
    public void HomeJumpsToTheFirstEnabledOption()
    {
        string? changed = null;
        var cut = RenderControl(p => p
            .Add(c => c.Options,
            [
                new() { Label = "Day", Value = "day", IsDisabled = true },
                new() { Label = "Week", Value = "week" },
                new() { Label = "Month", Value = "month" }
            ])
            .Add(c => c.Value, "month")
            .Add(c => c.ValueChanged, (string? value) => changed = value));

        Options(cut)[2].KeyDown(Key("Home"));

        changed.Should().Be("week", "the first option is disabled, so Home lands on the first ENABLED one");
    }

    [Fact]
    public void EndJumpsToTheLastEnabledOption()
    {
        string? changed = null;
        var cut = RenderControl(p => p
            .Add(c => c.Options,
            [
                new() { Label = "Day", Value = "day" },
                new() { Label = "Week", Value = "week" },
                new() { Label = "Month", Value = "month", IsDisabled = true }
            ])
            .Add(c => c.Value, "day")
            .Add(c => c.ValueChanged, (string? value) => changed = value));

        Options(cut)[0].KeyDown(Key("End"));

        changed.Should().Be("week", "the last option is disabled, so End lands on the last ENABLED one");
    }

    [Fact]
    public void ClickingAnOptionSelectsItExactlyOnce()
    {
        var raised = 0;
        string? changed = null;
        var cut = RenderControl(p => p
            .Add(c => c.Options, Days())
            .Add(c => c.Value, "day")
            .Add(c => c.ValueChanged, (string? value) => { raised++; changed = value; }));

        Options(cut)[2].Click();

        raised.Should().Be(1);
        changed.Should().Be("month");
    }

    [Fact]
    public void ClickingTheSelectedOptionRaisesNothing()
    {
        var raised = 0;
        var cut = RenderControl(p => p
            .Add(c => c.Options, Days())
            .Add(c => c.Value, "day")
            .Add(c => c.ValueChanged, (string? _) => raised++));

        Options(cut)[0].Click();

        raised.Should().Be(0, "re-selecting the current value is not a change — a two-way binding must not loop");
    }

    [Fact]
    public void EnterAndSpaceAreNotEmulated_SoAKeyPressActivateTheOptionOnlyThroughItsNativeClick()
    {
        var raised = 0;
        var cut = RenderControl(p => p
            .Add(c => c.Options, Days())
            .Add(c => c.Value, "day")
            .Add(c => c.ValueChanged, (string? _) => raised++));

        Options(cut)[1].KeyDown(Key("Enter"));
        Options(cut)[1].KeyDown(Key(" "));

        raised.Should().Be(0,
            "the options are native <button> elements — the browser fires the click, and emulating Enter/Space as well would activate twice");

        Options(cut)[1].Click();

        raised.Should().Be(1);
    }

    [Fact]
    public void ADisabledOptionCannotBeSelectedByPointerOrKeyboard()
    {
        string? changed = null;
        var cut = RenderControl(p => p
            .Add(c => c.Options,
            [
                new() { Label = "Day", Value = "day" },
                new() { Label = "Week", Value = "week", IsDisabled = true }
            ])
            .Add(c => c.Value, "day")
            .Add(c => c.ValueChanged, (string? value) => changed = value));

        var disabledOption = Options(cut)[1];

        disabledOption.GetAttribute("aria-disabled").Should().Be("true");
        disabledOption.HasAttribute("disabled").Should().BeTrue();
        disabledOption.GetAttribute("tabindex").Should().Be("-1");

        Options(cut)[0].KeyDown(Key("ArrowRight"));
        Options(cut)[1].Click();

        changed.Should().BeNull();
    }

    [Fact]
    public void ADisabledControlDisablesEveryOptionAndRaisesNothing()
    {
        var raised = 0;
        var cut = RenderControl(p => p
            .Add(c => c.Options, Days())
            .Add(c => c.Value, "day")
            .Add(c => c.Disabled, true)
            .Add(c => c.ValueChanged, (string? _) => raised++));

        Options(cut).Should().OnlyContain(option => option.HasAttribute("disabled"));
        Options(cut).Should().OnlyContain(option => option.GetAttribute("aria-disabled") == "true");

        Options(cut)[1].Click();

        raised.Should().Be(0);
    }

    // ---------- tabs mode ----------

    [Fact]
    public void TabsRoleRendersATablistWithTabs()
    {
        var cut = RenderControl(p => p
            .Add(c => c.Options,
            [
                new() { Label = "Preview", Value = "preview", PanelId = "preview-panel" },
                new() { Label = "Source", Value = "source", PanelId = "source-panel" }
            ])
            .Add(c => c.Value, "preview")
            .Add(c => c.Role, SegmentedControlRole.Tabs));

        var tablist = cut.Find("[role='tablist']");

        tablist.GetAttribute("aria-label").Should().Be("Options");
        Options(cut).Should().OnlyContain(option => option.GetAttribute("role") == "tab");
        Options(cut)[0].GetAttribute("aria-selected").Should().Be("true");
        Options(cut)[0].GetAttribute("aria-controls").Should().Be("preview-panel");
        Options(cut)[1].GetAttribute("aria-selected").Should().Be("false");
        Options(cut)[0].HasAttribute("aria-checked").Should().BeFalse("a tab is not a radio");
    }

    [Fact]
    public void ThePanelIdFallbackAppliesToEveryTab()
    {
        var cut = RenderControl(p => p
            .Add(c => c.Options, Days())
            .Add(c => c.Role, SegmentedControlRole.Tabs)
            .Add(c => c.PanelId, "one-region"));

        Options(cut).Should().OnlyContain(option => option.GetAttribute("aria-controls") == "one-region");
    }

    [Fact]
    public void ATabWithoutAnyPanelIdOmitsAriaControls()
    {
        var cut = RenderControl(p => p
            .Add(c => c.Options, Days())
            .Add(c => c.Role, SegmentedControlRole.Tabs));

        Options(cut)[0].HasAttribute("aria-controls").Should().BeFalse(
            "an idref to an element that does not exist is worse than no idref");
    }

    // ---------- presentation ----------

    [Theory]
    [InlineData(SegmentedControlSize.Sm, "tm-segmented--sm")]
    [InlineData(SegmentedControlSize.Md, "tm-segmented--md")]
    [InlineData(SegmentedControlSize.Lg, "tm-segmented--lg")]
    public void SizeSelectsTheSizeClass(SegmentedControlSize size, string expectedClass)
    {
        var cut = RenderControl(p => p
            .Add(c => c.Options, Days())
            .Add(c => c.Size, size));

        cut.Find(".tm-segmented").ClassList.Should().Contain(expectedClass);
    }

    [Fact]
    public void FullWidthAddsItsModifier()
    {
        var cut = RenderControl(p => p
            .Add(c => c.Options, Days())
            .Add(c => c.FullWidth, true)
            .Add(c => c.Class, "host-class"));

        var root = cut.Find(".tm-segmented");

        root.ClassList.Should().Contain("tm-segmented--full-width");
        root.ClassList.Should().Contain("host-class");
    }

    [Fact]
    public void TheIndicatorIsDecorativeAndTheFallbackDoesNotNeedJavaScript()
    {
        var cut = RenderControl(p => p
            .Add(c => c.Options, Days())
            .Add(c => c.Value, "week"));

        var indicator = cut.Find(".tm-segmented__indicator");

        indicator.GetAttribute("aria-hidden").Should().Be("true",
            "the selected option already carries the state — a painted bar must not repeat it");
        cut.Find(".tm-segmented").HasAttribute("data-tm-indicator").Should().BeFalse(
            "the sliding mode is only claimed once the browser measured the segment; without JS the selected option paints its own background");

        var selected = Options(cut)[1];
        selected.ClassList.Should().Contain("tm-segmented__option--selected");
    }

    [Fact]
    public void CountAndBadgeRenderBesideTheLabel()
    {
        var cut = RenderControl(p => p.Add(c => c.Options,
        [
            new() { Label = "All", Value = "all", Count = 12 },
            new() { Label = "Mine", Value = "mine", Badge = "New" }
        ]));

        cut.Find(".tm-segmented__count").TextContent.Trim().Should().Be("12");
        cut.Find(".tm-segmented__badge").TextContent.Trim().Should().Be("New");
        Options(cut)[0].TextContent.Should().Contain("All").And.Contain("12",
            "the count is part of the option's name, not decoration hidden from a screen reader");
    }

    [Fact]
    public void AnIconRendersInsideTheOption()
    {
        var cut = RenderControl(p => p.Add(c => c.Options,
        [
            new() { Label = "Grid", Value = "grid", Icon = "grid" }
        ]));

        Options(cut)[0].QuerySelector("svg").Should().NotBeNull();
        Options(cut)[0].QuerySelector("svg")!.GetAttribute("aria-hidden").Should().Be("true");
    }

    [Fact]
    public void NoOptions_RenderNothing()
    {
        var cut = RenderControl(p => p.Add(c => c.Options, Array.Empty<SegmentedOption<string>>()));

        cut.FindAll(".tm-segmented").Should().BeEmpty();
    }

    [Fact]
    public void AValueThatIsNotAmongTheOptions_LeavesTheGroupUnselected()
    {
        var cut = RenderControl(p => p
            .Add(c => c.Options, Days())
            .Add(c => c.Value, "fortnight"));

        Options(cut).Should().OnlyContain(option => option.GetAttribute("aria-checked") == "false");
        Options(cut)[0].GetAttribute("tabindex").Should().Be("0");
    }

    [Fact]
    public void TheRootTestIdHonoursThePrefix()
    {
        var cut = RenderControl(p => p
            .Add(c => c.Options, Days())
            .Add(c => c.TestIdPrefix, "period"));

        cut.Find(".tm-segmented").GetAttribute("data-testid").Should().Be("period-segmented-control");
        Options(cut)[0].GetAttribute("data-testid").Should().Be("period-segmented-option");
    }

    [Fact]
    public void AnExplicitDataTestIdWins()
    {
        var cut = RenderControl(p => p
            .Add(c => c.Options, Days())
            .Add(c => c.DataTestId, "my-control"));

        cut.Find(".tm-segmented").GetAttribute("data-testid").Should().Be("my-control");
    }

    [Fact]
    public void EveryOptionCarriesItsValueAsADataAttribute_ForHostsAndTests()
    {
        var cut = RenderControl(p => p.Add(c => c.Options,
        [
            new() { Label = "One", Value = 1.ToString() }
        ]));

        Options(cut)[0].GetAttribute("data-value").Should().Be("1");
    }

    // ---------- stylesheet contract ----------

    [Fact]
    public void TheStylesheetUsesOnlyDesignTokens_AndNoThemeBlock()
    {
        var css = ThemeCss.ComponentCss("_segmented-control.css");

        var literals = ColorLiteral.Matches(css).Select(match => match.Value).ToList();
        literals.Should().BeEmpty(
            "a component stylesheet may only read --tm-* tokens so the dark theme re-points them: {0}",
            string.Join(", ", literals));
        css.Should().NotContain("[data-theme");
    }

    [Fact]
    public void TheTrayScrollsHorizontally_AndKeepsTheFocusedSegmentVisible()
    {
        var css = ThemeCss.Normalise(ThemeCss.ComponentCss("_segmented-control.css"));

        css.Should().Contain("overflow-x: auto",
            "many segments in a narrow container must scroll instead of clipping the last one");
        css.Should().Contain("scroll-padding-inline",
            "a segment scrolled to the very edge must not hide behind the tray edge");
        css.Should().Contain("scroll-snap-type",
            "the tray settles on a segment boundary instead of a half-cut one");
    }

    [Fact]
    public void TheStylesheetCoversForcedColorsAndCoarsePointers()
    {
        var css = ThemeCss.Normalise(ThemeCss.ComponentCss("_segmented-control.css"));

        css.Should().Contain("forced-colors: active");
        css.Should().Contain("Highlight", "the selected segment must stay visible when the UA forces the palette");
        css.Should().Contain("@media (pointer: coarse)");
        css.Should().Contain("var(--tm-touch-target)", "a finger needs the 44px target");
    }

    [Fact]
    public void TheIndicatorIsListedInTheReducedMotionBlock()
    {
        var animations = ThemeCss.Normalise(File.ReadAllText(ThemeCss.CssPath("animations.css")));

        animations.Should().Contain(".tm-segmented__indicator",
            "the sliding indicator must not animate for a reader who asked for less motion");
    }
}

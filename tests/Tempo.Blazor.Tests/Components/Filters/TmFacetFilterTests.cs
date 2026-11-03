using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Filters;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Filters;

/// <summary>
/// Plan ff043b2e phase 4: <c>TmFacetFilter</c> is the faceted filter surface — labelled groups of
/// checkbox options with result counts, a per-group search box above a threshold, a "show more"
/// truncation, active filters as removable chips, a clear-all action, and two presentations
/// (<see cref="FacetFilterPresentation.Popover"/> = anchored dialog popover on a wide layout and a
/// bottom sheet on a mobile one, <see cref="FacetFilterPresentation.Inline"/> = the groups in the
/// page flow).
/// <para>
/// The selection is the public contract: <c>@bind-Selected</c> over
/// <see cref="IReadOnlyDictionary{TKey,TValue}"/> of group key → set of option values. Every fact
/// below pins either that contract, the accessible name a reader gets, or the markup a host styles.
/// </para>
/// </summary>
public class TmFacetFilterTests : LocalizationTestBase
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");

    // ---------- fixtures ----------

    private static FacetOption Option(string value, string? label = null, int? count = null, bool disabled = false)
        => new() { Value = value, Label = label ?? value, Count = count, IsDisabled = disabled };

    private static FacetGroup Group(
        string key,
        string? label = null,
        IReadOnlyList<FacetOption>? options = null,
        bool collapsed = false,
        int? maxVisible = null,
        int? searchThreshold = null)
        => new()
        {
            Key = key,
            Label = label ?? key,
            Options = options ?? [],
            IsCollapsed = collapsed,
            MaxVisible = maxVisible,
            SearchThreshold = searchThreshold
        };

    /// <summary>Two groups: a short one (no search) and a long one (search + truncation).</summary>
    private static IReadOnlyList<FacetGroup> SampleGroups() =>
    [
        Group("status", "Status", [Option("open", "Open", 12), Option("closed", "Closed", 5)]),
        Group("category", "Category",
        [
            Option("electronics", "Electronics", 2_100),
            Option("books", "Books", 480),
            Option("garden", "Garden", 210),
            Option("toys", "Toys", 90),
            Option("clothing", "Clothing", 54),
            Option("food", "Food", 31),
            Option("music", "Music", 12),
            Option("sports", "Sports", 8)
        ])
    ];

    private IRenderedComponent<TmFacetFilter> RenderPopover(
        IReadOnlyList<FacetGroup>? groups = null,
        Action<ComponentParameterCollectionBuilder<TmFacetFilter>>? configure = null)
    {
        // Bound to an Action first: Render<T> also has a RenderFragment overload, and a
        // block-bodied lambda is convertible to both.
        Action<ComponentParameterCollectionBuilder<TmFacetFilter>> setup = parameters =>
        {
            parameters.Add(c => c.Groups, groups ?? SampleGroups());
            configure?.Invoke(parameters);
        };

        return Render<TmFacetFilter>(setup);
    }

    /// <summary>Opens the popover and returns the rendered component.</summary>
    private IRenderedComponent<TmFacetFilter> Open(
        IReadOnlyList<FacetGroup>? groups = null,
        Action<ComponentParameterCollectionBuilder<TmFacetFilter>>? configure = null)
    {
        var cut = RenderPopover(groups, configure);
        cut.Find("[data-testid='facet-filter-trigger']").Click();
        return cut;
    }

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> Selection(params (string Group, string[] Values)[] entries)
        => entries.ToDictionary(
            entry => entry.Group,
            entry => (IReadOnlySet<string>)new HashSet<string>(entry.Values, StringComparer.Ordinal),
            StringComparer.Ordinal);

    // ---------- root / presentation ----------

    [Fact]
    public void Root_RendersTheBaseClassAndTheDefaultTestId()
    {
        var cut = RenderPopover();

        cut.Find(".tm-facet-filter").Should().NotBeNull();
        cut.Find("[data-testid='facet-filter']").Should().NotBeNull();
    }

    [Fact]
    public void DefaultPresentation_IsThePopover_SoTheGroupsStayClosedUntilTheTriggerRuns()
    {
        var cut = RenderPopover();

        cut.Find("[data-testid='facet-filter']").GetAttribute("data-presentation").Should().Be("popover");
        cut.FindAll("[data-testid='facet-filter-group']").Should().BeEmpty("nothing is rendered until the reader asks for the filters");
        cut.Find("[data-testid='facet-filter-trigger']").Should().NotBeNull();
    }

    [Fact]
    public void InlinePresentation_RendersTheGroupsInThePageFlowWithoutATrigger()
    {
        var cut = RenderPopover(configure: p => p.Add(c => c.Presentation, FacetFilterPresentation.Inline));

        cut.Find("[data-testid='facet-filter']").GetAttribute("data-presentation").Should().Be("inline");
        cut.FindAll("[data-testid='facet-filter-trigger']").Should().BeEmpty("an inline filter has no trigger to open");
        cut.FindAll("[data-testid='facet-filter-group']").Should().HaveCount(2);
        cut.Find("h3.tm-facet-filter__title").TextContent.Trim().Should().Be("Filters");
    }

    [Fact]
    public void Trigger_RendersTheLocalizedLabel()
    {
        var cut = RenderPopover();

        var trigger = cut.Find("[data-testid='facet-filter-trigger']");
        trigger.TagName.Should().Be("BUTTON");
        trigger.GetAttribute("type").Should().Be("button");
        trigger.TextContent.Should().Contain("Filters");
        trigger.GetAttribute("aria-expanded").Should().Be("false");
        trigger.GetAttribute("aria-haspopup").Should().Be("dialog");
    }

    [Fact]
    public void TriggerLabel_IsHostOverridable()
    {
        var cut = RenderPopover(configure: p => p.Add(c => c.TriggerLabel, "Refine"));

        cut.Find("[data-testid='facet-filter-trigger']").TextContent.Should().Contain("Refine");
    }

    [Fact]
    public void Popover_OpensOnTheTrigger_AndThePanelIsADialogNamedByItsVisibleTitle()
    {
        var cut = Open();

        cut.Find("[data-testid='facet-filter-trigger']").GetAttribute("aria-expanded").Should().Be("true");
        cut.Find("[data-testid='facet-filter-trigger']").GetAttribute("aria-controls").Should().NotBeNullOrEmpty();

        var panel = cut.Find("[data-testid='facet-filter-panel']");
        panel.GetAttribute("role").Should().Be("dialog");

        var titleId = panel.GetAttribute("aria-labelledby");
        titleId.Should().NotBeNullOrEmpty("a dialog must be named by the visible title, not only by its role");
        cut.Find($"#{titleId}").TextContent.Trim().Should().Be("Filters");
    }

    [Fact]
    public void Panel_KeepsTheTriggerOutOfTheAriaControlsWhenItIsClosed()
    {
        var cut = RenderPopover();

        cut.Find("[data-testid='facet-filter-trigger']").HasAttribute("aria-controls")
            .Should().BeFalse("aria-controls must not point at an element that is not rendered");
    }

    // ---------- groups ----------

    [Fact]
    public void Group_IsADisclosure_WithItsExpandedStateAndItsSelectedCount()
    {
        var cut = Open(configure: p => p.Add(c => c.Selected, Selection(("status", ["open", "closed"]))));

        var group = cut.FindAll("[data-testid='facet-filter-group']")[0];
        group.GetAttribute("data-group-key").Should().Be("status");

        var toggle = group.QuerySelector("[data-testid='facet-filter-group-toggle']")!;
        toggle.TagName.Should().Be("BUTTON");
        toggle.GetAttribute("type").Should().Be("button");
        toggle.GetAttribute("aria-expanded").Should().Be("true");
        toggle.GetAttribute("aria-controls").Should().NotBeNullOrEmpty();

        toggle.QuerySelector(".tm-facet-filter__group-count")!.TextContent.Trim().Should().Be("2");
        toggle.GetAttribute("aria-label").Should().Be("Status (selected: 2)");
    }

    [Fact]
    public void Group_WithoutSelection_HasNoCountAndNoCountInItsName()
    {
        var cut = Open();

        var toggle = cut.FindAll("[data-testid='facet-filter-group-toggle']")[0];
        toggle.QuerySelector(".tm-facet-filter__group-count").Should().BeNull();
        toggle.HasAttribute("aria-label").Should().BeFalse("the visible label already names the disclosure");
    }

    [Fact]
    public void Group_WithIsCollapsed_RendersCollapsed_AndTogglesOpen()
    {
        var cut = Open(groups: [
            Group("status", "Status", [Option("open", "Open", 12)], collapsed: true)
        ]);

        var toggle = cut.Find("[data-testid='facet-filter-group-toggle']");
        toggle.GetAttribute("aria-expanded").Should().Be("false");
        cut.FindAll("[data-testid='facet-filter-option']").Should().BeEmpty("a collapsed group renders no body at all");

        toggle.Click();

        cut.Find("[data-testid='facet-filter-group-toggle']").GetAttribute("aria-expanded").Should().Be("true");
        cut.FindAll("[data-testid='facet-filter-option']").Should().HaveCount(1);
    }

    // ---------- options ----------

    [Fact]
    public void Option_RendersACheckboxWithItsLabelAndAVisibleCount()
    {
        var cut = Open();

        var option = cut.FindAll("[data-testid='facet-filter-option']")[0];
        option.GetAttribute("data-option-value").Should().Be("open");

        var input = option.QuerySelector("input[type='checkbox']")!;
        input.Should().NotBeNull();
        input.HasAttribute("checked").Should().BeFalse();

        option.QuerySelector(".tm-facet-filter__option-count")!.TextContent.Trim().Should().Be("12");
        option.TextContent.Should().Contain("Open");
    }

    [Fact]
    public void Option_AriaLabel_IncludesTheCountSoTheNumberIsNotOnlyVisual()
    {
        var cut = Open();

        var input = cut.FindAll("[data-testid='facet-filter-option']")[0].QuerySelector("input[type='checkbox']")!;
        input.GetAttribute("aria-label").Should().Be("Open (12)");
    }

    [Fact]
    public void Option_WithoutCount_IsNamedByItsLabelAlone()
    {
        var cut = Open(groups: [
            Group("status", "Status", [Option("open", "Open")])
        ]);

        var input = cut.Find("[data-testid='facet-filter-option'] input[type='checkbox']");
        input.GetAttribute("aria-label").Should().Be("Open");
        cut.Find("[data-testid='facet-filter-option']").QuerySelector(".tm-facet-filter__option-count")
            .Should().BeNull("an option without a known count renders no count at all");
    }

    [Fact]
    public void Option_ShowCountsFalse_DropsTheCountFromTheMarkupAndFromTheName()
    {
        var cut = Open(configure: p => p.Add(c => c.ShowCounts, false));

        var option = cut.FindAll("[data-testid='facet-filter-option']")[0];
        option.QuerySelector(".tm-facet-filter__option-count").Should().BeNull();
        option.QuerySelector("input[type='checkbox']")!.GetAttribute("aria-label").Should().Be("Open");
    }

    [Fact]
    public void Option_IsChecked_WhenTheSelectedParameterHoldsIt()
    {
        var cut = Open(configure: p => p.Add(c => c.Selected, Selection(("status", ["open"]))));

        var options = cut.FindAll("[data-testid='facet-filter-option']");
        options[0].QuerySelector("input[type='checkbox']")!.HasAttribute("checked").Should().BeTrue();
        options[1].QuerySelector("input[type='checkbox']")!.HasAttribute("checked").Should().BeFalse();
    }

    [Fact]
    public void Option_Toggling_RaisesSelectedChangedWithAnImmutableSnapshot()
    {
        var snapshots = new List<IReadOnlyDictionary<string, IReadOnlySet<string>>>();
        var cut = Open(configure: p => p.Add(c => c.SelectedChanged, value => snapshots.Add(value)));

        cut.FindAll("[data-testid='facet-filter-option']")[0].QuerySelector("input[type='checkbox']")!.Change(true);

        snapshots.Should().HaveCount(1);
        snapshots[0].Should().ContainKey("status");
        snapshots[0]["status"].Should().BeEquivalentTo(["open"]);
        cut.FindAll("[data-testid='facet-filter-option']")[0]
            .QuerySelector("input[type='checkbox']")!.HasAttribute("checked").Should().BeTrue("the component keeps the user's choice");

        // A second toggle produces a NEW snapshot: a host that kept the first one never sees it mutate.
        cut.FindAll("[data-testid='facet-filter-option']")[1].QuerySelector("input[type='checkbox']")!.Change(true);

        snapshots.Should().HaveCount(2);
        snapshots[0]["status"].Should().BeEquivalentTo(["open"], "the earlier snapshot must not be mutated in place");
        snapshots[1]["status"].Should().BeEquivalentTo(["open", "closed"]);
        snapshots[1].Should().NotBeSameAs(snapshots[0]);
    }

    [Fact]
    public void Option_TogglingOff_RemovesOnlyThatValue_AndEmptiesTheGroupKey()
    {
        IReadOnlyDictionary<string, IReadOnlySet<string>>? captured = null;
        var cut = Open(configure: p => p
            .Add(c => c.Selected, Selection(("status", ["open"])))
            .Add(c => c.SelectedChanged, value => captured = value));

        cut.FindAll("[data-testid='facet-filter-option']")[0].QuerySelector("input[type='checkbox']")!.Change(false);

        captured.Should().NotBeNull();
        captured!.Should().NotContainKey("status", "an empty group is left out of the snapshot instead of shipping an empty set");
    }

    [Fact]
    public void Option_IsDisabled_CannotBeToggledAndStillShowsItsCount()
    {
        IReadOnlyDictionary<string, IReadOnlySet<string>>? captured = null;
        var cut = Open(groups: [Group("status", "Status", [Option("open", "Open", 12, disabled: true)])], configure: p => p.Add(c => c.SelectedChanged, value => captured = value));

        var input = cut.Find("[data-testid='facet-filter-option'] input[type='checkbox']");
        input.HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='facet-filter-option'] .tm-facet-filter__option-count").TextContent.Trim().Should().Be("12");

        captured.Should().BeNull("a disabled option raises nothing");
    }

    [Fact]
    public void Option_WithAnIcon_RendersItAsDecorationOnly()
    {
        var cut = Open(groups: [
            Group("status", "Status", [Option("open", "Open", 12) with { Icon = "check" }])
        ]);

        var icon = cut.Find("[data-testid='facet-filter-option'] .tm-facet-filter__option-icon");
        icon.GetAttribute("aria-hidden").Should().Be("true");
        icon.QuerySelector("svg").Should().NotBeNull();
    }

    // ---------- search ----------

    [Fact]
    public void Search_RendersOnlyForAGroupAtOrAboveTheThreshold()
    {
        var cut = Open();

        var groups = cut.FindAll("[data-testid='facet-filter-group']");
        groups[0].QuerySelectorAll("[data-testid='facet-filter-search']").Should().BeEmpty("two options do not need a search box");
        groups[1].QuerySelectorAll("[data-testid='facet-filter-search']").Should().HaveCount(1, "eight options do");
    }

    [Fact]
    public void Search_GroupOverrideZero_DisablesTheBoxEvenForALongGroup()
    {
        var cut = Open(groups: [
            Group("status", "Status", [Option("open", "Open", 1), Option("closed", "Closed", 2)], searchThreshold: 0)
        ]);

        cut.FindAll("[data-testid='facet-filter-search']").Should().BeEmpty();
    }

    [Fact]
    public void Search_GroupOverrideOne_AddsTheBoxToAShortGroup()
    {
        var cut = Open(groups: [
            Group("status", "Status", [Option("open", "Open", 1), Option("closed", "Closed", 2)], searchThreshold: 1)
        ]);

        cut.FindAll("[data-testid='facet-filter-search']").Should().HaveCount(1);
    }

    [Fact]
    public void Search_IsNamedByItsGroup_SoTheBoxesStayDistinguishable()
    {
        var cut = Open();

        var input = cut.FindAll("[data-testid='facet-filter-group']")[1]
            .QuerySelector("[data-testid='facet-filter-search']")!;
        input.GetAttribute("aria-label").Should().Be("Search Category");
    }

    [Fact]
    public void Search_Placeholder_InCzech_UsesTheVePreposition()
    {
        // "v" + "skupině" is ungrammatical: Czech needs "ve" before the "sk-" cluster. The
        // placeholder is also the box's accessible name, so the slip reaches a screen reader.
        UseCzechLocalization();
        var cut = Open();

        var input = cut.FindAll("[data-testid='facet-filter-group']")[1]
            .QuerySelector("[data-testid='facet-filter-search']")!;
        input.GetAttribute("aria-label").Should().Be("Hledat ve skupině Category");
    }

    [Fact]
    public void Search_FiltersTheOptions_AccentInsensitively_AndReportsNoMatch()
    {
        var cut = Open(groups: [
            Group("city", "City", [Option("usti", "Ústí nad Labem", 3), Option("praha", "Praha", 9)], searchThreshold: 1)
        ]);

        cut.Find("[data-testid='facet-filter-search']").Input("usti");

        cut.FindAll("[data-testid='facet-filter-option']").Should().HaveCount(1);
        cut.Find("[data-testid='facet-filter-option']").GetAttribute("data-option-value").Should().Be("usti");
        cut.FindAll(".tm-facet-filter__no-results").Should().BeEmpty();

        cut.Find("[data-testid='facet-filter-search']").Input("nikde");

        cut.FindAll("[data-testid='facet-filter-option']").Should().BeEmpty();
        cut.Find(".tm-facet-filter__no-results").TextContent.Trim().Should().Be("No option matches");
    }

    [Fact]
    public void Search_MatchesTheLabelCaseInsensitively()
    {
        var cut = Open(groups: [
            Group("city", "City", [Option("praha", "Praha", 9), Option("brno", "Brno", 4)], searchThreshold: 1)
        ]);

        cut.Find("[data-testid='facet-filter-search']").Input("PRA");

        cut.FindAll("[data-testid='facet-filter-option']").Should().HaveCount(1);
    }

    // ---------- show more ----------

    [Fact]
    public void MaxVisible_TruncatesTheGroup_AndShowMoreRevealsTheRest()
    {
        var cut = Open(configure: p => p.Add(c => c.MaxVisible, 3));

        var options = cut.FindAll("[data-testid='facet-filter-group']")[1]
            .QuerySelectorAll("[data-testid='facet-filter-option']");
        options.Should().HaveCount(3, "the long group shows MaxVisible options and hides the rest");

        var showMore = cut.Find("[data-testid='facet-filter-show-more']");
        showMore.TextContent.Trim().Should().Be("Show 5 more");

        showMore.Click();

        cut.FindAll("[data-testid='facet-filter-group']")[1]
            .QuerySelectorAll("[data-testid='facet-filter-option']").Should().HaveCount(8);
        cut.FindAll("[data-testid='facet-filter-show-more']").Should().BeEmpty();
    }

    [Fact]
    public void ShowMore_IsAbsentWhenEveryOptionFits()
    {
        var cut = Open();

        cut.FindAll("[data-testid='facet-filter-group']")[0]
            .QuerySelectorAll("[data-testid='facet-filter-option']").Should().HaveCount(2);
        cut.FindAll("[data-testid='facet-filter-group']")[0]
            .QuerySelectorAll("[data-testid='facet-filter-show-more']").Should().BeEmpty();
    }

    [Fact]
    public void MaxVisible_GroupOverrideZero_ShowsEveryOptionOfThatGroup()
    {
        var cut = Open(groups: [
            Group("all", "All",
                Enumerable.Range(0, 10).Select(i => Option($"v{i}", $"Value {i}", i)).ToList(),
                maxVisible: 0)
        ]);

        cut.FindAll("[data-testid='facet-filter-option']").Should().HaveCount(10);
        cut.FindAll("[data-testid='facet-filter-show-more']").Should().BeEmpty();
    }

    [Fact]
    public void MaxVisible_GroupOverride_TruncatesThatGroupOnly()
    {
        var cut = Open(groups: [
            Group("short", "Short", Enumerable.Range(0, 5).Select(i => Option($"s{i}", $"S {i}", i)).ToList()),
            Group("pinned", "Pinned", Enumerable.Range(0, 5).Select(i => Option($"p{i}", $"P {i}", i)).ToList(), maxVisible: 2)
        ]);

        var groups = cut.FindAll("[data-testid='facet-filter-group']");
        groups[0].QuerySelectorAll("[data-testid='facet-filter-option']").Should().HaveCount(5);
        groups[1].QuerySelectorAll("[data-testid='facet-filter-option']").Should().HaveCount(2);
    }

    [Theory]
    [InlineData("en", 2, "Show 2 more")]
    [InlineData("cs", 2, "Zobrazit další 2")]
    [InlineData("cs", 5, "Zobrazit dalších 5")]
    [InlineData("cs", 1, "Zobrazit další 1")]
    public void ShowMore_UsesTheGrammaticalFormOfTheCount(string language, int hidden, string expected)
    {
        if (language == "cs")
        {
            UseCzechLocalization();
        }

        var options = Enumerable.Range(0, hidden + 3).Select(i => Option($"v{i}", $"Value {i}", i)).ToList();
        var cut = Open(groups: [Group("all", "All", options)], configure: p => p.Add(c => c.MaxVisible, 3));

        cut.Find("[data-testid='facet-filter-show-more']").TextContent.Trim().Should().Be(expected);
    }

    // ---------- active filter chips ----------

    [Fact]
    public void ActiveChips_RenderOneChipPerSelectedValue_WithTheGroupAsTheChipField()
    {
        var cut = Open(configure: p => p.Add(c => c.Selected, Selection(("status", ["open"]), ("category", ["books"]))));

        var chips = cut.FindAll("[data-testid='facet-filter-chips'] [data-testid='facet-filter-chip']");
        chips.Should().HaveCount(2);
        chips[0].TextContent.Should().Contain("Status");
        chips[0].TextContent.Should().Contain("Open");
        chips[1].TextContent.Should().Contain("Category");
        chips[1].TextContent.Should().Contain("Books");

        cut.Find("[data-testid='facet-filter-chips']").GetAttribute("aria-label").Should().Be("Active filters");
    }

    [Fact]
    public void ActiveChips_AreAbsentWhenNothingIsSelected()
    {
        var cut = Open();

        cut.FindAll("[data-testid='facet-filter-chips']").Should().BeEmpty();
    }

    [Fact]
    public void Chip_Remove_RemovesOnlyThatValue_AndKeepsTheRest()
    {
        IReadOnlyDictionary<string, IReadOnlySet<string>>? captured = null;
        var cut = Open(configure: p => p
            .Add(c => c.Selected, Selection(("status", ["open"]), ("category", ["books"])))
            .Add(c => c.SelectedChanged, value => captured = value));

        cut.FindAll("[data-testid='facet-filter-chips'] [data-testid='facet-filter-chip']")[0]
            .QuerySelector(".tm-filter-chip-remove")!.Click();

        captured.Should().NotBeNull();
        captured!.Should().NotContainKey("status");
        captured["category"].Should().BeEquivalentTo(["books"]);
    }

    [Fact]
    public void ClearAll_RemovesEveryValue_AndIsAbsentWhenTheSelectionIsEmpty()
    {
        IReadOnlyDictionary<string, IReadOnlySet<string>>? captured = null;
        var cut = Open(configure: p => p
            .Add(c => c.Selected, Selection(("status", ["open"]), ("category", ["books"])))
            .Add(c => c.SelectedChanged, value => captured = value));

        cut.Find("[data-testid='facet-filter-clear-all']").Click();

        captured.Should().NotBeNull();
        captured!.Should().BeEmpty();

        cut.FindAll("[data-testid='facet-filter-chips']").Should().BeEmpty();
    }

    [Fact]
    public void ClearAll_IsNotRenderedWhileNothingIsSelected()
    {
        var cut = Open();

        cut.FindAll("[data-testid='facet-filter-clear-all']").Should().BeEmpty();
    }

    [Fact]
    public void Chip_Edit_OpensThePanelAndExpandsItsGroup()
    {
        var cut = RenderPopover(groups: [Group("status", "Status", [Option("open", "Open", 12)], collapsed: true)], configure: p => p.Add(c => c.Selected, Selection(("status", ["open"]))));

        cut.FindAll("[data-testid='facet-filter-option']").Should().BeEmpty("the group starts collapsed");

        cut.Find("[data-testid='facet-filter-chips'] [data-testid='facet-filter-chip'] .tm-filter-chip-label").Click();

        cut.Find("[data-testid='facet-filter-trigger']").GetAttribute("aria-expanded").Should().Be("true");
        cut.Find("[data-testid='facet-filter-group-toggle']").GetAttribute("aria-expanded").Should().Be("true");
        cut.FindAll("[data-testid='facet-filter-option']").Should().HaveCount(1);
    }

    // ---------- focus handoff (review round: UX findings 1-3) ----------

    /// <summary>
    /// The module the focus handoff borrows its <c>focusById</c> export from. The same module backs
    /// <c>TmOverlayPanel</c>, so a test can inspect every invocation on one handler.
    /// </summary>
    private const string OverlayModulePath = "./_content/Tempo.Blazor/js/overlay.js";

    private BunitJSModuleInterop SetupOverlayModule() => JSInterop.SetupModule(OverlayModulePath);

    /// <summary>The id of the last element the component asked <c>focusById</c> to focus.</summary>
    private static string? FocusedId(BunitJSModuleInterop module)
        => module.Invocations
            .Where(invocation => invocation.Identifier == "focusById")
            .Select(invocation => invocation.Arguments.Count > 0 ? invocation.Arguments[0] as string : null)
            .LastOrDefault();

    [Fact]
    public void Chip_Remove_MovesFocusToTheNextChipsRemoveButton()
    {
        var module = SetupOverlayModule();
        var cut = RenderPopover(configure: p => p.Add(c => c.Selected,
            Selection(("status", ["open", "closed"]), ("category", ["books"]))));

        var chips = cut.FindAll("[data-testid='facet-filter-chip']");
        chips.Should().HaveCount(3);
        var removedId = chips[0].QuerySelector(".tm-filter-chip-remove")!.GetAttribute("id");
        removedId.Should().NotBeNullOrEmpty("a chip's remove button needs a stable id to hand focus to");

        chips[0].QuerySelector(".tm-filter-chip-remove")!.Click();

        cut.WaitForAssertion(() => FocusedId(module).Should().Be(removedId));
        var after = cut.FindAll("[data-testid='facet-filter-chip']");
        after.Should().HaveCount(2);
        after[0].GetAttribute("data-option-value").Should().Be("closed",
            "focus lands on the value that took the removed chip's place, not back on the page");
        after[0].QuerySelector(".tm-filter-chip-remove")!.GetAttribute("id").Should().Be(removedId,
            "the ids are positional, so the chip that moved up now carries the removed chip's id");
    }

    [Fact]
    public void Chip_Remove_LastChip_FocusesThePopoverTrigger()
    {
        var module = SetupOverlayModule();
        var cut = RenderPopover(configure: p => p.Add(c => c.Selected, Selection(("status", ["open"]))));
        var triggerId = cut.Find("[data-testid='facet-filter-trigger']").GetAttribute("id");

        cut.Find("[data-testid='facet-filter-chip'] .tm-filter-chip-remove")!.Click();

        cut.FindAll("[data-testid='facet-filter-chip']").Should().BeEmpty();
        cut.WaitForAssertion(() => FocusedId(module).Should().Be(triggerId));
    }

    [Fact]
    public void Chip_Remove_LastChip_FocusesTheInlineFirstGroupToggle()
    {
        var module = SetupOverlayModule();
        var cut = RenderPopover(configure: p => p
            .Add(c => c.Presentation, FacetFilterPresentation.Inline)
            .Add(c => c.Selected, Selection(("status", ["open"]))));

        var toggleId = cut.Find("[data-testid='facet-filter-group-toggle']").GetAttribute("id");
        toggleId.Should().NotBeNullOrEmpty();

        cut.Find("[data-testid='facet-filter-chip'] .tm-filter-chip-remove")!.Click();

        cut.WaitForAssertion(() => FocusedId(module).Should().Be(toggleId));
    }

    [Fact]
    public void Chip_Remove_LastChip_WithNoGroups_FocusesTheInlineHeading()
    {
        var module = SetupOverlayModule();
        var cut = RenderPopover(groups: [], configure: p => p
            .Add(c => c.Presentation, FacetFilterPresentation.Inline)
            .Add(c => c.Selected, Selection(("retired", ["legacy"]))));

        var heading = cut.Find(".tm-facet-filter__title");
        var headingId = heading.GetAttribute("id");
        headingId.Should().NotBeNullOrEmpty();
        heading.GetAttribute("tabindex").Should().Be("-1",
            "with no group to land on, the inline heading is the only landmark left and must be focusable");

        cut.Find("[data-testid='facet-filter-chip'] .tm-filter-chip-remove")!.Click();

        cut.FindAll("[data-testid='facet-filter-chip']").Should().BeEmpty();
        cut.WaitForAssertion(() => FocusedId(module).Should().Be(headingId));
    }

    [Fact]
    public void PageClearAll_MovesFocusBackToThePopoverTrigger()
    {
        var module = SetupOverlayModule();
        var cut = RenderPopover(configure: p => p.Add(c => c.Selected,
            Selection(("status", ["open"]), ("category", ["books"]))));
        var triggerId = cut.Find("[data-testid='facet-filter-trigger']").GetAttribute("id");

        cut.Find("[data-testid='facet-filter-clear-all']").Click();

        cut.FindAll("[data-testid='facet-filter-chip']").Should().BeEmpty("the whole chip row leaves the DOM");
        cut.WaitForAssertion(() => FocusedId(module).Should().Be(triggerId));
    }

    [Fact]
    public void PageClearAll_Inline_MovesFocusToTheFirstGroupToggle()
    {
        var module = SetupOverlayModule();
        var cut = RenderPopover(configure: p => p
            .Add(c => c.Presentation, FacetFilterPresentation.Inline)
            .Add(c => c.Selected, Selection(("status", ["open"]))));

        var toggleId = cut.Find("[data-testid='facet-filter-group-toggle']").GetAttribute("id");
        cut.Find("[data-testid='facet-filter-clear-all']").Click();

        cut.WaitForAssertion(() => FocusedId(module).Should().Be(toggleId));
    }

    [Fact]
    public void FooterClearAll_MovesFocusToTheConfirmButton()
    {
        var module = SetupOverlayModule();
        var cut = Open(configure: p => p.Add(c => c.Selected, Selection(("status", ["open"]))));

        var confirmId = cut.Find("[data-testid='facet-filter-confirm']").GetAttribute("id");
        confirmId.Should().NotBeNullOrEmpty();

        cut.Find("[data-testid='facet-filter-panel-clear-all']").Click();

        cut.Find("[data-testid='facet-filter-panel-clear-all']").HasAttribute("disabled")
            .Should().BeTrue("the footer action disables itself once nothing is selected, which drops focus in some browsers");
        cut.WaitForAssertion(() => FocusedId(module).Should().Be(confirmId));
    }

    [Fact]
    public void ShowMore_MovesFocusToTheFirstNewlyRevealedOption()
    {
        var module = SetupOverlayModule();
        var cut = Open(configure: p => p.Add(c => c.MaxVisible, 6));

        var category = cut.FindAll("[data-testid='facet-filter-group']")[1];
        var bodyId = category.QuerySelector(".tm-facet-filter__group-body")!.GetAttribute("id");
        bodyId.Should().NotBeNullOrEmpty();

        category.QuerySelector("[data-testid='facet-filter-show-more']")!.Click();

        var options = cut.FindAll("[data-testid='facet-filter-group']")[1]
            .QuerySelectorAll("[data-testid='facet-filter-option']");
        options.Should().HaveCount(8);
        options[6].GetAttribute("data-option-value").Should().Be("music",
            "index 6 is the first option the truncation used to hide");
        var inputId = options[6].QuerySelector("input[type='checkbox']")!.GetAttribute("id");
        inputId.Should().Be($"{bodyId}-opt-6",
            "every option input carries a positional id so the focus handoff can address it");
        cut.WaitForAssertion(() => FocusedId(module).Should().Be(inputId));
    }

    // ---------- trigger count ----------

    [Fact]
    public void Trigger_ShowsTheActiveCount_AndCarriesItInItsAccessibleName()
    {
        var cut = RenderPopover(configure: p => p.Add(c => c.Selected, Selection(("status", ["open", "closed"]))));

        var trigger = cut.Find("[data-testid='facet-filter-trigger']");
        trigger.QuerySelector(".tm-facet-filter__trigger-count")!.TextContent.Trim().Should().Be("2");
        trigger.GetAttribute("aria-label").Should().Be("Filters (selected: 2)");
    }

    // ---------- confirm action ----------

    [Fact]
    public void Confirm_ShowsTheResultCount_RaisesOnApplyAndClosesThePanel()
    {
        var applied = 0;
        var cut = Open(configure: p => p
            .Add(c => c.ResultCount, 12)
            .Add(c => c.OnApply, () => applied++));

        var confirm = cut.Find("[data-testid='facet-filter-confirm']");
        confirm.TagName.Should().Be("BUTTON");
        confirm.TextContent.Trim().Should().Be("Show 12 results");

        confirm.Click();

        applied.Should().Be(1);
        cut.FindAll("[data-testid='facet-filter-panel']").Should().BeEmpty();
        cut.Find("[data-testid='facet-filter-trigger']").GetAttribute("aria-expanded").Should().Be("false");
    }

    [Fact]
    public void Confirm_WithoutAResultCount_KeepsThePlainLabel()
    {
        var cut = Open();

        cut.Find("[data-testid='facet-filter-confirm']").TextContent.Trim().Should().Be("Show results");
    }

    [Fact]
    public void Confirm_UsesTheGrammaticalFormOfTheResultCount()
    {
        var cut = Open(configure: p => p.Add(c => c.ResultCount, 1));

        cut.Find("[data-testid='facet-filter-confirm']").TextContent.Trim().Should().Be("Show 1 result");
    }

    // ---------- mobile sheet ----------

    [Fact]
    public void MobileLayout_PopoverPresentation_RendersABottomSheetInsteadOfTheAnchoredPanel()
    {
        var cut = Open(configure: p => p.Add(c => c.LayoutMode, TmLayoutMode.Mobile));

        cut.FindAll("[data-testid='facet-filter-panel']").Should().BeEmpty("a phone gets the sheet, not a small popover");
        cut.FindComponents<TmOverlayPanel>().Should().BeEmpty();

        var sheet = cut.FindComponents<TmDrawer>().Should().ContainSingle().Subject;
        sheet.Instance.Position.Should().Be(DrawerPosition.Bottom);
        sheet.Instance.IsOpen.Should().BeTrue();

        cut.Find("[data-testid='facet-filter-sheet']").Should().NotBeNull();
        cut.Find("[data-testid='facet-filter-confirm']").TextContent.Trim().Should().Be("Show results");
    }

    [Fact]
    public void MobileLayout_SheetKeepsTheGroupsAndTheClearAllAction()
    {
        var cut = Open(configure: p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.Selected, Selection(("status", ["open"])))
            .Add(c => c.ResultCount, 7));

        cut.FindAll("[data-testid='facet-filter-group']").Should().HaveCount(2);
        cut.Find("[data-testid='facet-filter-panel-clear-all']").Should().NotBeNull();
        cut.Find("[data-testid='facet-filter-confirm']").TextContent.Trim().Should().Be("Show 7 results");
    }

    [Fact]
    public void MobileLayout_SheetConfirm_ClosesTheSheetAndRaisesOnApply()
    {
        var applied = 0;
        var cut = Open(configure: p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.ResultCount, 3)
            .Add(c => c.OnApply, () => applied++));

        cut.Find("[data-testid='facet-filter-confirm']").Click();

        applied.Should().Be(1);
        cut.FindComponents<TmDrawer>().Should().ContainSingle().Subject.Instance.IsOpen
            .Should().BeFalse("the confirmed sheet closes");
        cut.FindAll("[data-testid='facet-filter-sheet']").Should().BeEmpty("a closed sheet renders no content");
    }

    [Fact]
    public void MobileLayout_InlinePresentation_StaysInline()
    {
        var cut = RenderPopover(configure: p => p
            .Add(c => c.Presentation, FacetFilterPresentation.Inline)
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile));

        cut.FindComponents<TmDrawer>().Should().BeEmpty("there is nothing to open in the inline presentation");
        cut.FindAll("[data-testid='facet-filter-group']").Should().HaveCount(2);
    }

    // ---------- keyboard / dismissal ----------

    [Fact]
    public async Task Escape_DismissesThePanel_AndTheTriggerReportsCollapsed()
    {
        var cut = Open();

        // overlay.js consumes Escape in the window capture phase in a real browser (and restores
        // focus to the anchor trigger there); the component's own dismissal path is this
        // JSInvokable callback, not a keydown handler in the markup.
        var panel = cut.FindComponent<TmOverlayPanel>();
        await cut.InvokeAsync(() => panel.Instance.NotifyDismissedAsync("escape"));

        cut.FindAll("[data-testid='facet-filter-panel']").Should().BeEmpty();
        cut.Find("[data-testid='facet-filter-trigger']").GetAttribute("aria-expanded").Should().Be("false");
    }

    [Fact]
    public void Checkboxes_AreNativeInputs_SoSpaceAndTabNeedNoEmulation()
    {
        var cut = Open();

        foreach (var input in cut.FindAll("[data-testid='facet-filter-option'] input[type='checkbox']"))
        {
            input.GetAttribute("type").Should().Be("checkbox");
            input.HasAttribute("tabindex").Should().BeFalse("a native checkbox is in the Tab order already");
        }
    }

    [Fact]
    public void DisclosureButtons_AreNativeButtons_WithNoKeyHandler()
    {
        var cut = Open();

        var toggle = cut.Find("[data-testid='facet-filter-group-toggle']");
        toggle.TagName.Should().Be("BUTTON");
        toggle.GetAttribute("type").Should().Be("button");
        toggle.HasAttribute("@onkeydown").Should().BeFalse();
    }

    // ---------- host data safety / formatting ----------

    [Fact]
    public void UnknownSelectedValues_ArePreserved_InTheSnapshot_AndRemovableAsChips()
    {
        IReadOnlyDictionary<string, IReadOnlySet<string>>? captured = null;
        var cut = Open(configure: p => p
            .Add(c => c.Selected, Selection(("status", ["open"]), ("retired", ["legacy"])))
            .Add(c => c.SelectedChanged, value => captured = value));

        var chips = cut.FindAll("[data-testid='facet-filter-chips'] [data-testid='facet-filter-chip']");
        chips.Should().HaveCount(2);
        chips[1].TextContent.Should().Contain("retired").And.Contain("legacy");

        cut.FindAll("[data-testid='facet-filter-option']")[1].QuerySelector("input[type='checkbox']")!.Change(true);

        captured.Should().NotBeNull();
        captured!.Should().ContainKey("retired", "an unknown key belongs to the host and must survive a toggle");
        captured["retired"].Should().BeEquivalentTo(["legacy"]);
    }

    [Fact]
    public void Counts_FormatWithTheSuppliedCulture()
    {
        var cut = Open(configure: p => p.Add(c => c.Culture, Czech));

        var option = cut.FindAll("[data-testid='facet-filter-option']")[2];
        option.QuerySelector(".tm-facet-filter__option-count")!.TextContent.Trim()
            .Should().Be(2100.ToString("N0", Czech));
    }

    [Fact]
    public void EmptyGroups_RenderTheEmptyState()
    {
        var cut = RenderPopover(groups: Array.Empty<FacetGroup>());

        cut.Find("[data-testid='facet-filter-trigger']").Click();

        cut.Find(".tm-facet-filter__empty").TextContent.Trim().Should().Be("No filters available");
    }

    [Fact]
    public void Groups_WithoutAKey_AreSkipped()
    {
        var cut = Open(groups: [
            Group("", "Broken", [Option("x", "X", 1)]),
            Group("status", "Status", [Option("open", "Open", 2)])
        ]);

        cut.FindAll("[data-testid='facet-filter-group']").Should().HaveCount(1);
        cut.Find("[data-testid='facet-filter-group']").GetAttribute("data-group-key").Should().Be("status");
    }

    [Fact]
    public void HostParameterThatKeepsItsIdentity_DoesNotUndoTheUsersChoice()
    {
        IReadOnlyDictionary<string, IReadOnlySet<string>>? captured = null;
        var cut = Open(configure: p => p.Add(c => c.SelectedChanged, value => captured = value));

        cut.FindAll("[data-testid='facet-filter-option']")[0].QuerySelector("input[type='checkbox']")!.Change(true);
        captured.Should().NotBeNull();

        // The host re-renders with the SAME (still unbound) parameter value it always passed.
        cut.Render(p => p.Add(c => c.Groups, SampleGroups()));

        cut.FindAll("[data-testid='facet-filter-option']")[0]
            .QuerySelector("input[type='checkbox']")!.HasAttribute("checked")
            .Should().BeTrue("an unchanged host input must not silently clear what the reader picked");
    }

    [Fact]
    public void HostParameterThatChanges_IsAdopted()
    {
        var cut = Open(configure: p => p.Add(c => c.Selected, Selection(("status", ["open"]))));

        cut.FindAll("[data-testid='facet-filter-option']")[0]
            .QuerySelector("input[type='checkbox']")!.HasAttribute("checked").Should().BeTrue();

        cut.Render(p => p
            .Add(c => c.Groups, SampleGroups())
            .Add(c => c.Selected, Selection(("status", ["closed"]))));

        cut.FindAll("[data-testid='facet-filter-option']")[0]
            .QuerySelector("input[type='checkbox']")!.HasAttribute("checked").Should().BeFalse();
        cut.FindAll("[data-testid='facet-filter-option']")[1]
            .QuerySelector("input[type='checkbox']")!.HasAttribute("checked").Should().BeTrue();
    }

    // ---------- live region ----------

    [Fact]
    public void LiveRegion_IsOnePoliteStatus_AndStaysSilentOnTheFirstRender()
    {
        var cut = Open(configure: p => p.Add(c => c.ResultCount, 5));

        var regions = cut.FindAll("[role='status']");
        regions.Should().HaveCount(1, "one polite live region, no duplicate announcements");
        regions[0].GetAttribute("aria-live").Should().Be("polite");
        regions[0].TextContent.Trim().Should().BeEmpty("the count is already in the button's name at first paint");
    }

    [Fact]
    public void LiveRegion_AnnouncesTheNewResultCountOnlyWhenItActuallyChanges()
    {
        var cut = Open(configure: p => p.Add(c => c.ResultCount, 5));

        cut.Render(p => p.Add(c => c.Groups, SampleGroups()).Add(c => c.ResultCount, 7));

        cut.FindAll("[role='status']").Should().HaveCount(1);
        cut.Find("[role='status']").TextContent.Trim().Should().Be("Show 7 results");
    }

    // ---------- test id conventions ----------

    [Fact]
    public void TestIdPrefix_NamespacesTheInternalIds()
    {
        var cut = RenderPopover(configure: p => p.Add(c => c.TestIdPrefix, "left"));

        cut.Find("[data-testid='left-facet-filter']").Should().NotBeNull();
        cut.Find("[data-testid='left-facet-filter-trigger']").Should().NotBeNull();
    }

    [Fact]
    public void DataTestId_OverridesTheRootId()
    {
        var cut = RenderPopover(configure: p => p.Add(c => c.DataTestId, "my-filters"));

        cut.Find("[data-testid='my-filters']").Should().NotBeNull();
    }

    // ---------- culture defaults ----------

    [Fact]
    public void DefaultCulture_IsTheAmbientOne()
    {
        using var _ = CultureScope.Use(English);

        var cut = Open();

        cut.FindAll("[data-testid='facet-filter-option']")[2]
            .QuerySelector(".tm-facet-filter__option-count")!.TextContent.Trim().Should().Be("2,100");
    }
}

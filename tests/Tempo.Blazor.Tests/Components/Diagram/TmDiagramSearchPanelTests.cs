using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Diagram;

/// <summary>
/// Contract tests for <see cref="TmDiagramSearchPanel"/>: the Query / ReplaceQuery values reach
/// the inputs as bound state (F3 — the literal-string bindings once pre-filled the fields with
/// the C# field NAMES), a valid pattern renders no error and keeps the replace actions enabled,
/// and the effective read-only state renders the panel search-only (F2).
/// </summary>
public class TmDiagramSearchPanelTests : LocalizationTestBase
{
    public TmDiagramSearchPanelTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void QueryParameter_IsRenderedAsTheInputValue()
    {
        var cut = Render<TmDiagramSearchPanel>(p => p
            .Add(c => c.Query, "foo")
            .Add(c => c.ReplaceQuery, "bar"));

        var inputs = cut.FindAll("input[type='search']");
        inputs.Should().HaveCount(2, "the search field and the replace field");
        inputs[0].GetAttribute("value").Should().Be("foo");
        inputs[1].GetAttribute("value").Should().Be("bar");
    }

    [Fact]
    public void RegexError_Null_RendersNoErrorAndEnablesReplace()
    {
        var cut = Render<TmDiagramSearchPanel>(p => p
            .Add(c => c.Query, "a")
            .Add(c => c.ResultsCount, 1)
            .Add(c => c.RegexError, (string?)null));

        cut.FindAll(".tm-diagram-search-panel__error").Should().BeEmpty();
        var buttons = cut.FindAll(".tm-diagram-search-panel__actions button");
        buttons.Should().HaveCount(2);
        buttons.Should().NotContain(b => b.HasAttribute("disabled"),
            "a valid query with results keeps Replace / Replace all enabled");
    }

    [Fact]
    public void ReadOnly_True_HidesReplaceFieldAndButtons()
    {
        var cut = Render<TmDiagramSearchPanel>(p => p
            .Add(c => c.Query, "a")
            .Add(c => c.ResultsCount, 1)
            .Add(c => c.ReadOnly, true));

        cut.FindAll(".tm-diagram-search-panel__replace").Should().BeEmpty(
            "the replace field is an editing affordance");
        cut.FindAll(".tm-diagram-search-panel__actions").Should().BeEmpty(
            "Replace / Replace all must not render in read-only");
        cut.FindAll("input[type='search']").Should().ContainSingle(
            "the search field stays for navigation");
    }

    [Fact]
    public void ReadOnly_False_RendersReplaceFieldAndEnabledButtons()
    {
        var cut = Render<TmDiagramSearchPanel>(p => p
            .Add(c => c.Query, "a")
            .Add(c => c.ResultsCount, 1)
            .Add(c => c.ReadOnly, false));

        cut.FindAll(".tm-diagram-search-panel__replace").Should().HaveCount(1);
        cut.FindAll(".tm-diagram-search-panel__actions button").Should().HaveCount(2);
        cut.FindAll(".tm-diagram-search-panel__actions button")
            .Should().NotContain(b => b.HasAttribute("disabled"));
    }

    // ── Review M/O: the error is announced, the counter is a live region ──────

    [Fact]
    public void RegexError_RendersRoleAlert_AndTiesTheQueryInputToIt()
    {
        var cut = Render<TmDiagramSearchPanel>(p => p
            .Add(c => c.Query, "(")
            .Add(c => c.RegexError, "Invalid regex: too many ("));

        var error = cut.Find(".tm-diagram-search-panel__error");
        error.GetAttribute("role").Should().Be("alert",
            "the pattern error must be announced as soon as it appears");
        error.Id.Should().StartWith("tm-diagram-search-error-",
            "the id is namespaced per component kind");

        var query = cut.Find("input[type='search']");
        query.GetAttribute("aria-invalid").Should().Be("true");
        query.GetAttribute("aria-describedby").Should().Be(error.Id,
            "the input points at the error message that describes it");
    }

    [Fact]
    public void RegexError_IdIsPerInstance_AndReferencedByEveryOwnInput()
    {
        // Two panels on one page must not share the error id — a duplicate id breaks the
        // aria-describedby reference for every instance after the first.
        var first = Render<TmDiagramSearchPanel>(p => p
            .Add(c => c.Query, "(")
            .Add(c => c.RegexError, "bad pattern one"));
        var second = Render<TmDiagramSearchPanel>(p => p
            .Add(c => c.Query, "[")
            .Add(c => c.RegexError, "bad pattern two"));

        var firstError = first.Find(".tm-diagram-search-panel__error");
        var secondError = second.Find(".tm-diagram-search-panel__error");
        firstError.Id.Should().NotBe(secondError.Id, "instance ids never collide");
        firstError.Id.Should().StartWith("tm-diagram-search-error-");
        secondError.Id.Should().StartWith("tm-diagram-search-error-");

        first.Find("input[type='search']").GetAttribute("aria-describedby").Should().Be(firstError.Id);
        second.Find("input[type='search']").GetAttribute("aria-describedby").Should().Be(secondError.Id);
    }

    /// <summary>
    /// TmSearchInput has no Clearable parameter — a Clearable="true" attribute is splatted onto
    /// the native input as an invalid DOM attribute. No usage may carry it.
    /// </summary>
    [Fact]
    public void SearchInput_HasNoInvalidClearableAttribute()
    {
        var cut = Render<TmDiagramSearchPanel>(p => p
            .Add(c => c.Query, "foo")
            .Add(c => c.RegexError, "bad pattern"));

        cut.FindAll("input[clearable]").Should().BeEmpty(
            "TmSearchInput clears itself — Clearable is not a parameter and must not leak to the DOM");
    }

    [Fact]
    public void RegexError_Clears_AriaState_AndCounterCarriesLiveRegionSemantics()
    {
        var cut = Render<TmDiagramSearchPanel>(p => p
            .Add(c => c.Query, "a")
            .Add(c => c.ResultsCount, 2));

        var query = cut.Find("input[type='search']");
        query.HasAttribute("aria-invalid").Should().BeFalse();
        query.HasAttribute("aria-describedby").Should().BeFalse();

        var counter = cut.Find(".tm-diagram-search-panel__counter");
        counter.GetAttribute("role").Should().Be("status",
            "prev/next navigation announces the counter without moving focus");
        counter.GetAttribute("aria-live").Should().Be("polite");
        counter.GetAttribute("aria-atomic").Should().Be("true");
    }

    // ── UX review: the options row (all-pages + regex) shares one flex wrapper ──

    [Fact]
    public void OptionsRow_AllPagesAndRegex_ShareOneWrapper()
    {
        var cut = Render<TmDiagramSearchPanel>(p => p
            .Add(c => c.Query, "a"));

        var options = cut.Find(".tm-diagram-search-panel__options");
        options.QuerySelector(".tm-diagram-search-panel__all-pages").Should().NotBeNull();
        options.QuerySelector(".tm-diagram-search-panel__regex-toggle").Should().NotBeNull();
    }

    [Fact]
    public void RegexToggle_CarriesTheUseRegexAccessibleName()
    {
        var cut = Render<TmDiagramSearchPanel>(p => p
            .Add(c => c.Query, "a"));

        // The switch's inner input carries the aria-label (never the ".*" glyph alone).
        cut.Find(".tm-diagram-search-panel__regex-toggle input")
            .GetAttribute("aria-label").Should().Be("Use regular expressions");
    }
}

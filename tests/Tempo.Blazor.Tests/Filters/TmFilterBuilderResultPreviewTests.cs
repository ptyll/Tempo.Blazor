using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Filters;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Filters;

/// <summary>
/// Result preview of <see cref="TmFilterBuilder"/>: a debounced count from
/// <see cref="TmFilterBuilder.ResultCountProvider"/>, rendered as a pluralized, polite live region.
/// The preview renders only when both <c>ShowResultPreview</c> and the provider are set.
/// </summary>
public class TmFilterBuilderResultPreviewTests : LocalizationTestBase
{
    private static readonly ActiveFilter NameContainsA =
        new("name", "Name", FilterOperator.Contains, "A", "A");

    private static readonly ActiveFilter StatusActive =
        new("status", "Status", FilterOperator.Equals, "active", "Active");

    private static readonly ActiveFilter ScoreHigh =
        new("score", "Score", FilterOperator.GreaterThan, "3", "3");

    private static readonly FilterDefinition NameField = new()
    {
        FieldName = "name",
        FieldLabel = "Name",
        FieldType = FilterFieldType.Text,
    };

    private static Func<IReadOnlyList<ActiveFilter>, CancellationToken, Task<int?>> Count(int? value)
        => (_, _) => Task.FromResult(value);

    /// <summary>Without a provider the preview is not part of the document at all.</summary>
    [Fact]
    public void ResultPreview_WithoutProvider_RendersNothing()
    {
        var cut = Render<TmFilterBuilder>(p => p
            .Add(c => c.ShowResultPreview, true)
            .Add(c => c.ActiveFilters, new[] { NameContainsA }));

        cut.FindAll(".tm-filter-result-preview").Should().BeEmpty();
    }

    /// <summary>The provider alone is not enough: ShowResultPreview stays an opt-in.</summary>
    [Fact]
    public void ResultPreview_ProviderWithoutFlag_RendersNothing()
    {
        var cut = Render<TmFilterBuilder>(p => p
            .Add(c => c.ResultCountProvider, Count(3))
            .Add(c => c.ActiveFilters, new[] { NameContainsA }));

        cut.WaitForAssertion(() =>
            cut.FindAll(".tm-filter-result-preview").Should().BeEmpty(),
            timeout: TimeSpan.FromSeconds(2));
    }

    /// <summary>Both set: the preview shows the pluralized count in a polite status region.</summary>
    [Fact]
    public void ResultPreview_WithProvider_RendersPluralizedCount()
    {
        var calls = 0;
        var cut = Render<TmFilterBuilder>(p => p
            .Add(c => c.ShowResultPreview, true)
            .Add(c => c.ResultCountProvider, (IReadOnlyList<ActiveFilter> _, CancellationToken _) =>
            {
                calls++;
                return Task.FromResult<int?>(3);
            })
            .Add(c => c.ActiveFilters, new[] { NameContainsA }));

        cut.WaitForAssertion(() =>
            cut.Find(".tm-filter-result-preview").TextContent.Should().Contain("3 matching records"),
            timeout: TimeSpan.FromSeconds(2));

        var region = cut.Find(".tm-filter-result-preview");
        region.GetAttribute("aria-live").Should().Be("polite");
        region.GetAttribute("role").Should().Be("status");
        calls.Should().Be(1);
    }

    /// <summary>One record takes the singular form.</summary>
    [Fact]
    public void ResultPreview_OneRecord_UsesSingular()
    {
        var cut = Render<TmFilterBuilder>(p => p
            .Add(c => c.ShowResultPreview, true)
            .Add(c => c.ResultCountProvider, Count(1))
            .Add(c => c.ActiveFilters, new[] { NameContainsA }));

        cut.WaitForAssertion(() =>
            cut.Find(".tm-filter-result-preview").TextContent.Trim().Should().Be("1 matching record"),
            timeout: TimeSpan.FromSeconds(2));
    }

    /// <summary>Czech agrees with the count: 1 / 3 / 5 pick one / few / other.</summary>
    [Theory]
    [InlineData(1, "1 odpovídající záznam")]
    [InlineData(3, "3 odpovídající záznamy")]
    [InlineData(5, "5 odpovídajících záznamů")]
    public void ResultPreview_Czech_AgreesWithCount(int count, string expected)
    {
        UseCzechLocalization();

        var cut = Render<TmFilterBuilder>(p => p
            .Add(c => c.ShowResultPreview, true)
            .Add(c => c.ResultCountProvider, Count(count))
            .Add(c => c.ActiveFilters, new[] { NameContainsA }));

        cut.WaitForAssertion(() =>
            cut.Find(".tm-filter-result-preview").TextContent.Trim().Should().Be(expected),
            timeout: TimeSpan.FromSeconds(2));
    }

    /// <summary>A null count means "unknown": the live region stays, but it has nothing to announce.</summary>
    [Fact]
    public void ResultPreview_NullCount_LeavesTheRegionEmpty()
    {
        var cut = Render<TmFilterBuilder>(p => p
            .Add(c => c.ShowResultPreview, true)
            .Add(c => c.ResultCountProvider, Count(null)));

        cut.WaitForAssertion(() =>
        {
            var region = cut.Find(".tm-filter-result-preview");
            region.TextContent.Trim().Should().BeEmpty();
            region.GetAttribute("role").Should().Be("status");
        }, timeout: TimeSpan.FromSeconds(2));
    }

    /// <summary>A throwing provider is swallowed: the region stays empty and the builder keeps working.</summary>
    [Fact]
    public void ResultPreview_ThrowingProvider_LeavesTheRegionEmptyAndSurvives()
    {
        var cut = Render<TmFilterBuilder>(p => p
            .Add(c => c.FilterDefinitions, new[] { NameField })
            .Add(c => c.ShowResultPreview, true)
            .Add(c => c.ResultCountProvider, (IReadOnlyList<ActiveFilter> _, CancellationToken _) =>
                throw new InvalidOperationException("count failed")));

        cut.WaitForAssertion(() =>
            cut.Find(".tm-filter-result-preview").TextContent.Trim().Should().BeEmpty(),
            timeout: TimeSpan.FromSeconds(2));

        cut.Find(".tm-filter-builder-add").Click();
        cut.FindAll(".tm-filter-field-option").Should().NotBeEmpty();
    }

    /// <summary>Three changes inside the debounce window collapse into one call, with the latest filters.</summary>
    [Fact]
    public void ResultPreview_RapidChanges_CallsProviderOnce()
    {
        var calls = 0;
        IReadOnlyList<ActiveFilter> last = [];
        var cut = Render<TmFilterBuilder>(p => p
            .Add(c => c.ShowResultPreview, true)
            .Add(c => c.ResultCountProvider, (IReadOnlyList<ActiveFilter> filters, CancellationToken _) =>
            {
                calls++;
                last = filters;
                return Task.FromResult<int?>(2);
            })
            .Add(c => c.ActiveFilters, new[] { NameContainsA }));

        cut.WaitForAssertion(() => calls.Should().Be(1), timeout: TimeSpan.FromSeconds(2));

        calls = 0;
        cut.Render(p => p.Add(c => c.ActiveFilters, new[] { NameContainsA, StatusActive }));
        cut.Render(p => p.Add(c => c.ActiveFilters, new[] { StatusActive }));
        cut.Render(p => p.Add(c => c.ActiveFilters, new[] { ScoreHigh }));

        // Still inside the debounce window: nothing new has been asked yet.
        calls.Should().Be(0);

        cut.WaitForAssertion(() =>
        {
            calls.Should().Be(1);
            last.Should().ContainSingle().Which.FieldName.Should().Be("score");
        }, timeout: TimeSpan.FromSeconds(2));
    }

    /// <summary>A count that resolves as null clears the text but leaves the live region in place.</summary>
    [Fact]
    public void ResultPreview_CountBecomesNull_ClearsTheRegion()
    {
        var next = 4;
        var cut = Render<TmFilterBuilder>(p => p
            .Add(c => c.ShowResultPreview, true)
            .Add(c => c.ResultCountProvider, (IReadOnlyList<ActiveFilter> _, CancellationToken _) =>
                Task.FromResult<int?>(next))
            .Add(c => c.ActiveFilters, new[] { NameContainsA }));

        cut.WaitForAssertion(() =>
            cut.Find(".tm-filter-result-preview").TextContent.Should().Contain("4"),
            timeout: TimeSpan.FromSeconds(2));

        next = -1;
        cut.Render(p => p
            .Add(c => c.ResultCountProvider, Count(null))
            .Add(c => c.ActiveFilters, new[] { StatusActive }));

        cut.WaitForAssertion(() =>
        {
            var region = cut.Find(".tm-filter-result-preview");
            region.TextContent.Trim().Should().BeEmpty();
            region.TextContent.Should().NotContain("Counting");
        }, timeout: TimeSpan.FromSeconds(2));
    }

    /// <summary>While the first count is in flight the region says it is counting, with a spinner.</summary>
    [Fact]
    public void ResultPreview_WhileCounting_ShowsLoadingText()
    {
        var gate = new TaskCompletionSource<int?>();
        var cut = Render<TmFilterBuilder>(p => p
            .Add(c => c.ShowResultPreview, true)
            .Add(c => c.ResultCountProvider, (IReadOnlyList<ActiveFilter> _, CancellationToken _) => gate.Task)
            .Add(c => c.ActiveFilters, new[] { NameContainsA }));

        cut.WaitForAssertion(() =>
        {
            var region = cut.Find(".tm-filter-result-preview");
            region.TextContent.Should().Contain("Counting");
            region.QuerySelector(".tm-spinner").Should().NotBeNull();
        }, timeout: TimeSpan.FromSeconds(2));

        gate.SetResult(2);
        cut.WaitForAssertion(() =>
            cut.Find(".tm-filter-result-preview").TextContent.Should().Contain("2 matching records"),
            timeout: TimeSpan.FromSeconds(2));
    }

    /// <summary>The in-progress editor filter counts too, once it has a value.</summary>
    [Fact]
    public void ResultPreview_DraftFilter_IsCountedWithTheActiveOnes()
    {
        IReadOnlyList<ActiveFilter> last = [];
        var cut = Render<TmFilterBuilder>(p => p
            .Add(c => c.FilterDefinitions, new[] { NameField })
            .Add(c => c.ShowResultPreview, true)
            .Add(c => c.ResultCountProvider, (IReadOnlyList<ActiveFilter> filters, CancellationToken _) =>
            {
                last = filters;
                return Task.FromResult<int?>(1);
            })
            .Add(c => c.ActiveFilters, new[] { StatusActive }));

        cut.WaitForAssertion(() => last.Should().ContainSingle(), timeout: TimeSpan.FromSeconds(2));

        cut.Find(".tm-filter-builder-add").Click();
        cut.Find(".tm-filter-field-option").Click();
        cut.Find(".tm-filter-value-input").Change("Ann");

        cut.WaitForAssertion(() =>
        {
            last.Should().HaveCount(2);
            last.Should().Contain(f => f.FieldName == "name" && f.DisplayValue == "Ann");
        }, timeout: TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// French "whole millions" use the <c>_Many</c> form. The number is formatted in full, so the
    /// template must read "&lt;N&gt; d'enregistrements correspondants" — the old template added
    /// "de millions", which claimed a count a million times too large.
    /// </summary>
    [Fact]
    public void ResultPreview_French_ManyForm_FormatsTheFullNumberWithoutTheMillionsWord()
    {
        Services.AddSingleton<ITmLocalizer>(new MockTmLocalizer(
            new Dictionary<string, string>
            {
                ["TmFilterBuilder_ResultPreview_One"] = "{0} enregistrement correspondant",
                ["TmFilterBuilder_ResultPreview_Many"] = "{0:N0} d'enregistrements correspondants",
                ["TmFilterBuilder_ResultPreview_Other"] = "{0:N0} enregistrements correspondants",
            },
            System.Globalization.CultureInfo.GetCultureInfo("fr")));

        var cut = Render<TmFilterBuilder>(p => p
            .Add(c => c.ShowResultPreview, true)
            .Add(c => c.ResultCountProvider, Count(2000000))
            .Add(c => c.ActiveFilters, new[] { NameContainsA }));

        cut.WaitForAssertion(() =>
        {
            var text = cut.Find(".tm-filter-result-preview").TextContent;
            text.Should().Contain("d'enregistrements correspondants");
            text.Should().NotContain("de millions");
        }, timeout: TimeSpan.FromSeconds(2));
    }

    /// <summary>A parent re-render with the same inputs must not ask again or replace the count.</summary>
    [Fact]
    public void ResultPreview_ParentRerender_DoesNotRecount()
    {
        var calls = 0;
        var filters = new[] { NameContainsA };
        Func<IReadOnlyList<ActiveFilter>, CancellationToken, Task<int?>> provider = (_, _) =>
        {
            calls++;
            return Task.FromResult<int?>(3);
        };

        var cut = Render<TmFilterBuilder>(p => p
            .Add(c => c.ShowResultPreview, true)
            .Add(c => c.ResultCountProvider, provider)
            .Add(c => c.ActiveFilters, filters));

        cut.WaitForAssertion(() =>
        {
            calls.Should().Be(1);
            cut.Find(".tm-filter-result-preview").TextContent.Should().Contain("3 matching records");
        }, timeout: TimeSpan.FromSeconds(2));

        cut.Render(p => p
            .Add(c => c.ShowResultPreview, true)
            .Add(c => c.ResultCountProvider, provider)
            .Add(c => c.ActiveFilters, filters));

        calls.Should().Be(1);
        var region = cut.Find(".tm-filter-result-preview");
        region.TextContent.Should().Contain("3 matching records");
        region.TextContent.Should().NotContain("Counting");
    }

    /// <summary>A later change keeps the previous count on screen until the new one arrives.</summary>
    [Fact]
    public void ResultPreview_LaterChange_KeepsPreviousCount()
    {
        var gate = new TaskCompletionSource<int?>();
        var calls = 0;
        var cut = Render<TmFilterBuilder>(p => p
            .Add(c => c.ShowResultPreview, true)
            .Add(c => c.ResultCountProvider, (IReadOnlyList<ActiveFilter> _, CancellationToken _) =>
            {
                calls++;
                return calls == 1 ? Task.FromResult<int?>(4) : gate.Task;
            })
            .Add(c => c.ActiveFilters, new[] { NameContainsA }));

        cut.WaitForAssertion(() =>
            cut.Find(".tm-filter-result-preview").TextContent.Should().Contain("4 matching records"),
            timeout: TimeSpan.FromSeconds(2));

        cut.Render(p => p.Add(c => c.ActiveFilters, new[] { StatusActive }));

        cut.WaitForAssertion(() =>
        {
            var region = cut.Find(".tm-filter-result-preview");
            region.TextContent.Should().Contain("4 matching records");
            region.TextContent.Should().NotContain("Counting");
            region.GetAttribute("aria-busy").Should().Be("true");
        }, timeout: TimeSpan.FromSeconds(2));

        gate.SetResult(1);
        cut.WaitForAssertion(() =>
        {
            var region = cut.Find(".tm-filter-result-preview");
            region.TextContent.Trim().Should().Be("1 matching record");
            region.GetAttribute("aria-busy").Should().BeNull();
        }, timeout: TimeSpan.FromSeconds(2));
    }
}

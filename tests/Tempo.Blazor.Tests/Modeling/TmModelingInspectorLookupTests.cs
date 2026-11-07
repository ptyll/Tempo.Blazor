using Bunit;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Phase 5 review (A7): the async lookup behind a reference field. The inspector owns the debounce,
/// the request generation and the paging because a "Load more" APPEND plus continuation-token paging
/// cannot be expressed through <c>IDropdownDataProvider</c> + a core combobox — but the request
/// mapping itself lives in ONE private adapter.
/// <para>
/// Pinned here: a burst of typing issues ONE request (300 ms debounce), an answer for an older search
/// never lands (out-of-order), a <c>SearchRequired = false</c> descriptor offers its first page on
/// open, and "Load more" APPENDS the next page through the host's own cursor.
/// </para>
/// </summary>
public sealed class TmModelingInspectorLookupTests : LocalizationTestBase
{
    private static readonly TimeSpan TestWait = TimeSpan.FromSeconds(15);

    [Fact]
    public void A_burst_of_typing_issues_one_request()
    {
        var provider = new RecordingLookupProvider();
        using var cut = RenderField(provider);

        Open(cut);
        var filter = cut.Find(".tm-multiselect__filter-input");
        filter.Input("r");
        filter.Input("ri");
        filter.Input("ris");

        cut.WaitForAssertion(
            () => provider.Requests.Should().ContainSingle(request => request.SearchText == "ris"),
            TestWait);
        provider.Requests.Should().NotContain(request => request.SearchText == "r" || request.SearchText == "ri",
            "the debounce replaces the earlier keystrokes");
    }

    [Fact]
    public async Task An_older_answer_never_overwrites_a_newer_one()
    {
        var provider = new GatedLookupProvider();
        using var cut = RenderField(provider);

        Open(cut);
        var filter = cut.Find(".tm-multiselect__filter-input");
        filter.Input("old");
        await WaitForGateAsync(cut, provider, "old");

        filter.Input("new");
        await WaitForGateAsync(cut, provider, "new");

        // The NEW search answers first and is shown…
        provider.GateFor("new")!.SetResult(Page(("sys-new", "New system")));
        cut.WaitForAssertion(
            () => cut.Find(".tm-multiselect__options").TextContent.Should().Contain("New system"),
            TestWait);

        // …and the OLD answer that arrives afterwards is dropped (its generation is stale).
        provider.GateFor("old")!.SetResult(Page(("sys-old", "Old system")));
        await cut.InvokeAsync(() => Task.Delay(100));

        cut.Find(".tm-multiselect__options").TextContent.Should().NotContain("Old system");
        cut.Find(".tm-multiselect__options").TextContent.Should().Contain("New system");
    }

    [Fact]
    public void A_search_not_required_lookup_offers_its_first_page_on_open()
    {
        var provider = new RecordingLookupProvider();
        using var cut = RenderField(provider, searchRequired: false);

        Open(cut);

        cut.WaitForAssertion(
            () => provider.Requests.Should().ContainSingle(request => request.SearchText == string.Empty),
            TestWait);
        cut.WaitForAssertion(
            () => cut.Find(".tm-multiselect__options").TextContent.Should().Contain("First page"),
            TestWait);
    }

    [Fact]
    public void Below_the_minimum_length_the_field_asks_for_more_characters()
    {
        var provider = new RecordingLookupProvider();
        using var cut = RenderField(provider);

        Open(cut);
        cut.Find(".tm-multiselect__filter-input").Input("r");

        cut.WaitForAssertion(
            () => cut.Find(".tm-multiselect__empty").TextContent.Should().Contain("at least 2"),
            TestWait);
        provider.Requests.Should().BeEmpty("nothing is asked below the minimum length");
    }

    [Fact]
    public void Load_more_appends_the_next_page_through_the_host_cursor()
    {
        var provider = new RecordingLookupProvider { HasMore = true, ContinuationToken = "cursor-2" };
        using var cut = RenderField(provider, searchRequired: false);

        Open(cut);
        cut.WaitForAssertion(
            () => provider.Requests.Should().ContainSingle(request => request.ContinuationToken == null),
            TestWait);
        cut.WaitForAssertion(
            () => cut.Find("[data-testid='modeling-inspector-field-systems-load-more']").Should().NotBeNull(),
            TestWait);

        cut.Find("[data-testid='modeling-inspector-field-systems-load-more']").Click();

        cut.WaitForAssertion(
            () => provider.Requests.Should().Contain(request => request.ContinuationToken == "cursor-2"),
            TestWait);
        cut.WaitForAssertion(
            () => cut.Find(".tm-multiselect__options").TextContent.Should().Contain("Second page"),
            TestWait);
        cut.Find(".tm-multiselect__options").TextContent.Should().Contain("First page",
            "Load more APPENDS — it never replaces the page already offered");
    }

    [Fact]
    public void Load_more_pages_by_offset_when_the_host_returns_no_cursor()
    {
        // Review A7: a host that pages by OFFSET (no ContinuationToken) still works — the next page
        // sends Skip = the number of items it already returned, never the first page again.
        var provider = new OffsetLookupProvider();
        using var cut = RenderField(provider, searchRequired: false);

        Open(cut);
        cut.WaitForAssertion(
            () => cut.Find("[data-testid='modeling-inspector-field-systems-load-more']").Should().NotBeNull(),
            TestWait);

        cut.Find("[data-testid='modeling-inspector-field-systems-load-more']").Click();

        cut.WaitForAssertion(
            () => provider.Requests.Should().Contain(request => request.Skip == 1),
            TestWait);
        provider.Requests.Should().Contain(request => request.Skip == 0);
        provider.Requests.Should().OnlyContain(request => request.ContinuationToken == null);
        cut.WaitForAssertion(
            () => cut.Find(".tm-multiselect__options").TextContent.Should().Contain("Offset page two"),
            TestWait);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private IRenderedComponent<TmModelingInspector> RenderField(
        IModelingLookupProvider provider,
        bool searchRequired = true)
        => Render<TmModelingInspector>(parameters => parameters
            .Add(p => p.Element, CreateElement())
            .Add(p => p.ReadOnly, false)
            .Add(p => p.PropertySchema, CreateSchema(searchRequired))
            .Add(p => p.ModelId, "model-1")
            .Add(p => p.LookupProvider, provider));

    private static void Open(IRenderedComponent<TmModelingInspector> cut)
    {
        cut.Find("[data-testid='modeling-inspector-field-systems'] .tm-multiselect").Click();
        cut.WaitForAssertion(() => cut.Find(".tm-multiselect__filter-input").Should().NotBeNull(), TestWait);
    }

    /// <summary>Waits OFF the render loop for the debounced request to reach the gated provider. A
    /// <c>WaitForAssertion</c> is wrong here: a suppressed answer renders nothing, so there is no
    /// render to re-check the assertion on — the wait has to be clock-based.</summary>
    private static async Task WaitForGateAsync(
        IRenderedComponent<TmModelingInspector> cut,
        GatedLookupProvider provider,
        string searchText)
    {
        var deadline = DateTime.UtcNow + TestWait;
        while (provider.GateFor(searchText) is null && DateTime.UtcNow < deadline)
        {
            await cut.InvokeAsync(() => Task.Delay(25));
        }

        (provider.GateFor(searchText) is not null).Should().BeTrue(
            $"the debounced '{searchText}' lookup must reach the host");
    }

    private static ModelingLookupResultDto Page(params (string Id, string Display)[] items)
        => new()
        {
            Items = [.. items.Select(item => new ModelingLookupItemDto { RefType = "system", RefId = item.Id, Display = item.Display })],
            TotalCount = items.Length
        };

    private static ModelingElementDto CreateElement() => new()
    {
        Id = "element-1",
        Notation = "bpmn",
        SemanticType = "userTask",
        Name = "Approve request"
    };

    private static ModelingPropertySchemaDto CreateSchema(bool searchRequired) => new()
    {
        SchemaId = "tests.lookup",
        Version = "1",
        Notation = "bpmn",
        SemanticType = "userTask",
        Sections =
        [
            new ModelingPropertySectionDto
            {
                Key = "ownership",
                Label = "Ownership",
                Fields =
                [
                    new ModelingPropertyFieldDto
                    {
                        Key = "systems",
                        Label = "Systems",
                        Type = ModelingPropertyFieldType.EntityReferenceList,
                        Lookup = new ModelingLookupDescriptor
                        {
                            LookupType = "system",
                            MinQueryLength = 2,
                            PageSize = 10,
                            SearchRequired = searchRequired
                        }
                    }
                ]
            }
        ]
    };

    private sealed class RecordingLookupProvider : IModelingLookupProvider
    {
        public string ProviderKey => "tempo.tests.lookup";

        public bool HasMore { get; init; }

        public string? ContinuationToken { get; init; }

        public List<ModelingLookupRequest> Requests { get; } = [];

        public Task<ModelingLookupResultDto> LookupAsync(ModelingLookupRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var isFirstPage = request.ContinuationToken is null;
            var more = HasMore && isFirstPage;
            return Task.FromResult(new ModelingLookupResultDto
            {
                Items =
                [
                    new ModelingLookupItemDto
                    {
                        RefType = "system",
                        RefId = isFirstPage ? "sys-first" : "sys-second",
                        Display = isFirstPage ? "First page" : "Second page"
                    }
                ],
                TotalCount = 2,
                HasMore = more,
                ContinuationToken = more ? ContinuationToken : null
            });
        }
    }

    private sealed class GatedLookupProvider : IModelingLookupProvider
    {
        private readonly Dictionary<string, TaskCompletionSource<ModelingLookupResultDto>> _gates = new(StringComparer.Ordinal);

        public string ProviderKey => "tempo.tests.lookup.gated";

        public TaskCompletionSource<ModelingLookupResultDto>? GateFor(string searchText)
            => _gates.TryGetValue(searchText, out var gate) ? gate : null;

        public Task<ModelingLookupResultDto> LookupAsync(ModelingLookupRequest request, CancellationToken cancellationToken)
        {
            var gate = new TaskCompletionSource<ModelingLookupResultDto>(TaskCreationOptions.RunContinuationsAsynchronously);
            _gates[request.SearchText] = gate;
            return gate.Task;
        }
    }

    /// <summary>Pages by OFFSET only — no continuation cursor (review A7).</summary>
    private sealed class OffsetLookupProvider : IModelingLookupProvider
    {
        public string ProviderKey => "tempo.tests.lookup.offset";

        public List<ModelingLookupRequest> Requests { get; } = [];

        public Task<ModelingLookupResultDto> LookupAsync(ModelingLookupRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var second = request.Skip > 0;
            return Task.FromResult(new ModelingLookupResultDto
            {
                Items =
                [
                    new ModelingLookupItemDto
                    {
                        RefType = "system",
                        RefId = second ? "sys-offset-two" : "sys-offset-one",
                        Display = second ? "Offset page two" : "Offset page one"
                    }
                ],
                TotalCount = 2,
                HasMore = !second,
                ContinuationToken = null
            });
        }
    }
}

using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Inputs;

/// <summary>TDD tests for TmQueryInput — JQL-style query input with autocomplete and error underlining.</summary>
public class TmQueryInputTests : LocalizationTestBase
{
    private static Func<QuerySuggestionRequest, Task<IReadOnlyList<QuerySuggestion>>> Provider(params QuerySuggestion[] items)
        => _ => Task.FromResult<IReadOnlyList<QuerySuggestion>>(items);

    private static readonly QuerySuggestion[] Sample =
    [
        new("status", "status", QuerySuggestionKind.Field, "Work item status"),
        new("priority", "priority", QuerySuggestionKind.Field),
    ];

    // ── Rendering ──────────────────────────────────────────────────────────────

    [Fact]
    public void QueryInput_Renders_Input()
    {
        var cut = Render<TmQueryInput>();
        cut.Find(".tm-query-input__input").Should().NotBeNull();
    }

    [Fact]
    public void QueryInput_Monospace_ByDefault()
    {
        var cut = Render<TmQueryInput>();
        cut.Find(".tm-query-input").ClassList.Should().Contain("tm-query-input--mono");
    }

    [Fact]
    public void QueryInput_Monospace_False_NoModifier()
    {
        var cut = Render<TmQueryInput>(p => p.Add(c => c.Monospace, false));
        cut.Find(".tm-query-input").ClassList.Should().NotContain("tm-query-input--mono");
    }

    [Fact]
    public void QueryInput_Label_Rendered_WhenProvided()
    {
        var cut = Render<TmQueryInput>(p => p.Add(c => c.Label, "TQL query"));
        cut.Find(".tm-query-input__label").TextContent.Should().Be("TQL query");
    }

    [Fact]
    public void QueryInput_Disabled_SetsDisabledAttribute()
    {
        var cut = Render<TmQueryInput>(p => p.Add(c => c.Disabled, true));
        cut.Find(".tm-query-input__input").HasAttribute("disabled").Should().BeTrue();
        cut.Find(".tm-query-input").ClassList.Should().Contain("tm-query-input--disabled");
    }

    [Fact]
    public void QueryInput_AriaAutocomplete_IsList()
    {
        var cut = Render<TmQueryInput>();
        cut.Find(".tm-query-input__input").GetAttribute("aria-autocomplete").Should().Be("list");
    }

    // ── Value / ValueChanged ────────────────────────────────────────────────────

    [Fact]
    public void QueryInput_Input_FiresValueChanged()
    {
        string? changed = null;
        var cut = Render<TmQueryInput>(p => p
            .Add(c => c.ValueChanged, v => changed = v));

        cut.Find(".tm-query-input__input").Input("status = Active");

        changed.Should().Be("status = Active");
    }

    // ── Suggestions ─────────────────────────────────────────────────────────────

    [Fact]
    public void QueryInput_Suggestions_RenderInListbox()
    {
        var cut = Render<TmQueryInput>(p => p
            .Add(c => c.DebounceMs, 0)
            .Add(c => c.SuggestionsProvider, Provider(Sample)));

        cut.Find(".tm-query-input__input").Input("st");

        cut.WaitForAssertion(() =>
        {
            cut.Find("[role='listbox']").Should().NotBeNull();
            cut.FindAll(".tm-query-input__option").Count.Should().Be(2);
        });
    }

    [Fact]
    public void QueryInput_Debounce_QueriesProviderAfterDelay()
    {
        var calls = 0;
        var cut = Render<TmQueryInput>(p => p
            .Add(c => c.DebounceMs, 60)
            .Add(c => c.SuggestionsProvider, _ =>
            {
                calls++;
                return Task.FromResult<IReadOnlyList<QuerySuggestion>>(Sample);
            }));

        cut.Find(".tm-query-input__input").Input("st");

        calls.Should().Be(0); // not queried synchronously — debounced
        cut.WaitForAssertion(() => calls.Should().Be(1), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void QueryInput_AcceptSuggestion_InsertsInsertTextAtCaret()
    {
        // Caret is read via JS interop — mock it to point at the end of "status = Hi".
        JSInterop.Setup<int>("tmQueryInput.getCaret", _ => true).SetResult(11);
        string? changed = null;

        var cut = Render<TmQueryInput>(p => p
            .Add(c => c.DebounceMs, 0)
            .Add(c => c.SuggestionsProvider, Provider(new QuerySuggestion("High", "High", QuerySuggestionKind.Value)))
            .Add(c => c.ValueChanged, v => changed = v));

        cut.Find(".tm-query-input__input").Input("status = Hi");
        cut.WaitForAssertion(() => cut.FindAll(".tm-query-input__option").Count.Should().Be(1));

        cut.Find(".tm-query-input__option").Click();

        changed.Should().NotBeNull();
        changed!.Should().Contain("status = High"); // "Hi" partial token replaced with the InsertText
    }

    [Fact]
    public void QueryInput_KeyboardNav_ArrowDown_Then_Enter_AcceptsSecond()
    {
        JSInterop.Setup<int>("tmQueryInput.getCaret", _ => true).SetResult(0);
        string? changed = null;

        var cut = Render<TmQueryInput>(p => p
            .Add(c => c.DebounceMs, 0)
            .Add(c => c.SuggestionsProvider, Provider(Sample))
            .Add(c => c.ValueChanged, v => changed = v));

        var input = cut.Find(".tm-query-input__input");
        input.Input("");
        cut.WaitForAssertion(() => cut.FindAll(".tm-query-input__option").Count.Should().Be(2));

        cut.Find(".tm-query-input__input").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" }); // move 0 -> 1
        cut.Find(".tm-query-input__input").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        changed.Should().NotBeNull();
        changed!.Should().Contain("priority"); // second suggestion accepted
    }

    [Fact]
    public async Task QueryInput_Escape_ClosesDropdown()
    {
        var cut = Render<TmQueryInput>(p => p
            .Add(c => c.DebounceMs, 0)
            .Add(c => c.SuggestionsProvider, Provider(Sample)));

        cut.Find(".tm-query-input__input").Input("st");
        cut.WaitForAssertion(() => cut.FindAll("[role='listbox']").Should().HaveCount(1));

        // A bubbled Escape keydown on the input must not close on its own: overlay.js consumes
        // Escape at the window capture phase and delivers it through NotifyDismissedAsync — the
        // component's own Escape cases were dead code in a real browser (dead-branch sweep).
        cut.Find(".tm-query-input__input").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        cut.FindAll("[role='listbox']").Should().HaveCount(1,
            "a bubbled Escape keydown is not the dismissal path — overlay.js owns it");

        var overlay = cut.FindComponent<Tempo.Blazor.Components.Overlay.TmOverlayPanel>();
        await cut.InvokeAsync(() => overlay.Instance.NotifyDismissedAsync("escape"));

        cut.FindAll("[role='listbox']").Should().BeEmpty();
    }

    [Fact]
    public async Task QueryInput_Dismissal_InvalidatesInFlightSuggestionRequest()
    {
        // 20D carry-forward: overlay dismissal routed SetOpen(false) — it did not bump
        // _requestVersion or clear suggestions, so an in-flight LoadSuggestionsAsync could
        // land afterwards and re-set _isOpen=true, reopening the just-closed dropdown.
        var gate = new TaskCompletionSource<IReadOnlyList<QuerySuggestion>>();
        var cut = Render<TmQueryInput>(p => p
            .Add(c => c.DebounceMs, 0)
            .Add(c => c.SuggestionsProvider, _ => gate.Task));

        cut.Find(".tm-query-input__input").Input("st");
        cut.WaitForAssertion(() => cut.FindAll("[role='listbox']").Should().HaveCount(1));

        var overlay = cut.FindComponent<Tempo.Blazor.Components.Overlay.TmOverlayPanel>();
        await cut.InvokeAsync(() => overlay.Instance.NotifyDismissedAsync("escape"));
        cut.FindAll("[role='listbox']").Should().BeEmpty();

        // The late response must be discarded — the dismissal invalidated its version.
        gate.SetResult(Sample);
        await Task.Delay(250); // let the provider continuation run

        cut.FindAll("[role='listbox']").Should().BeEmpty(
            "a suggestion response landing after dismissal must not reopen the dropdown");
    }

    [Fact]
    public void QueryInput_Enter_WhenClosed_FiresOnSubmit()
    {
        string? submitted = null;
        var cut = Render<TmQueryInput>(p => p
            .Add(c => c.OnSubmit, v => submitted = v)
            .Add(c => c.ValueChanged, _ => { }));

        cut.Find(".tm-query-input__input").Input("status = Active");
        cut.Find(".tm-query-input__input").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        submitted.Should().Be("status = Active");
    }

    // ── Error spans ─────────────────────────────────────────────────────────────

    [Fact]
    public void QueryInput_ErrorSpans_Render_WithTooltip()
    {
        var cut = Render<TmQueryInput>(p => p
            .Add(c => c.Value, "status = Bogus")
            .Add(c => c.Errors, new List<QueryErrorSpan> { new(9, 5, "Unknown value 'Bogus'") }));

        var error = cut.Find(".tm-query-input__error");
        error.TextContent.Should().Be("Bogus");
        error.GetAttribute("title").Should().Be("Unknown value 'Bogus'");
    }

    [Fact]
    public void QueryInput_AriaInvalid_WhenErrors()
    {
        var cut = Render<TmQueryInput>(p => p
            .Add(c => c.Value, "status = Bogus")
            .Add(c => c.Errors, new List<QueryErrorSpan> { new(9, 5, "bad") }));

        cut.Find(".tm-query-input__input").GetAttribute("aria-invalid").Should().Be("true");
    }

    [Fact]
    public void QueryInput_NoErrors_AriaInvalidFalse()
    {
        var cut = Render<TmQueryInput>(p => p.Add(c => c.Value, "status = Active"));
        cut.Find(".tm-query-input__input").GetAttribute("aria-invalid").Should().Be("false");
    }

    // ── Dropdown states ─────────────────────────────────────────────────────────

    [Fact]
    public void QueryInput_EmptyState_ShownWhenNoSuggestions()
    {
        var cut = Render<TmQueryInput>(p => p
            .Add(c => c.DebounceMs, 0)
            .Add(c => c.SuggestionsProvider, Provider())); // empty result

        cut.Find(".tm-query-input__input").Input("zzz");

        cut.WaitForAssertion(() =>
            cut.Find(".tm-query-input__status").TextContent.Should().Be("No suggestions"));
    }

    [Fact]
    public void QueryInput_ErrorState_ShownWhenProviderThrows()
    {
        var cut = Render<TmQueryInput>(p => p
            .Add(c => c.DebounceMs, 0)
            .Add(c => c.SuggestionsProvider, _ => throw new InvalidOperationException("boom")));

        cut.Find(".tm-query-input__input").Input("st");

        cut.WaitForAssertion(() =>
            cut.Find(".tm-query-input__status--error").TextContent.Should().Be("Suggestions unavailable"));
    }

    [Fact]
    public void QueryInput_Disabled_DoesNotQuerySuggestions()
    {
        var calls = 0;
        var cut = Render<TmQueryInput>(p => p
            .Add(c => c.Disabled, true)
            .Add(c => c.DebounceMs, 0)
            .Add(c => c.SuggestionsProvider, _ => { calls++; return Task.FromResult<IReadOnlyList<QuerySuggestion>>(Sample); }));

        cut.Find(".tm-query-input__input").Input("st");

        calls.Should().Be(0);
        cut.FindAll("[role='listbox']").Should().BeEmpty();
    }

    /// <summary>
    /// N168: the overlay Anchor is the <c>.tm-query-input__field</c> wrapper — overlay.js calls
    /// <c>.focus()</c> on it for Escape/outside dismissal. Without tabindex a plain div swallows
    /// that as a no-op and focus falls to <c>&lt;body&gt;</c>. <c>-1</c> makes it
    /// script-focusable without adding a Tab stop.
    /// </summary>
    [Fact]
    public void TmQueryInput_AnchorWrapper_IsProgrammaticallyFocusable()
    {
        var cut = Render<TmQueryInput>();

        cut.Find(".tm-query-input__field").GetAttribute("tabindex").Should().Be("-1");
    }
}

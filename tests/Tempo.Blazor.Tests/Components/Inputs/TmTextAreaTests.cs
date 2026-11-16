using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Inputs;

/// <summary>TDD tests for TmTextArea.</summary>
public class TmTextAreaTests : LocalizationTestBase
{
    [Fact]
    public async Task TmTextArea_FocusAsync_MovesFocusToTheTextarea()
    {
        var cut = Render<TmTextArea>(p => p.Add(c => c.Value, "hello"));

        await cut.InvokeAsync(async () => await cut.Instance.FocusAsync());

        JSInterop.Invocations.Should().Contain(invocation =>
            invocation.Identifier.Contains("focus", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TmTextArea_Renders_Textarea_Element()
    {
        var cut = Render<TmTextArea>();
        cut.Find("textarea").Should().NotBeNull();
    }

    [Fact]
    public void TmTextArea_Has_Base_CssClass()
    {
        var cut = Render<TmTextArea>();
        cut.Find("textarea").ClassList.Should().Contain("tm-input");
    }

    /// <summary>
    /// Fáze 18.1 fixes the measured `.tm-input|.tm-textarea` cascade tie with the compound
    /// <c>.tm-input.tm-textarea</c> — a selector that only reaches a textarea carrying BOTH
    /// classes. This pins the markup contract it relies on.
    /// </summary>
    [Fact]
    public void TmTextArea_CarriesBaseAndModifier_OnTheSameElement()
    {
        var cut = Render<TmTextArea>();

        cut.Find("textarea").ClassList.Should()
            .Contain("tm-input")
            .And.Contain("tm-textarea");
    }

    [Fact]
    public void TmTextArea_Default_Rows_Is_3()
    {
        var cut = Render<TmTextArea>();
        cut.Find("textarea").GetAttribute("rows").Should().Be("3");
    }

    [Fact]
    public void TmTextArea_Custom_Rows_Applied()
    {
        var cut = Render<TmTextArea>(p => p.Add(c => c.Rows, 6));
        cut.Find("textarea").GetAttribute("rows").Should().Be("6");
    }

    [Fact]
    public void TmTextArea_Label_Renders_Label_Element()
    {
        var cut = Render<TmTextArea>(p => p.Add(c => c.Label, "Description"));
        cut.Find("label").TextContent.Trim().Should().Be("Description");
    }

    [Fact]
    public void TmTextArea_No_Label_When_Null()
    {
        var cut = Render<TmTextArea>();
        cut.FindAll("label").Should().BeEmpty();
    }

    [Fact]
    public void TmTextArea_Placeholder_Applied()
    {
        var cut = Render<TmTextArea>(p => p.Add(c => c.Placeholder, "Enter text..."));
        cut.Find("textarea").GetAttribute("placeholder").Should().Be("Enter text...");
    }

    [Fact]
    public void TmTextArea_Disabled_Sets_Disabled_Attribute()
    {
        var cut = Render<TmTextArea>(p => p.Add(c => c.Disabled, true));
        cut.Find("textarea").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void TmTextArea_Error_Adds_Error_CssClass()
    {
        var cut = Render<TmTextArea>(p => p.Add(c => c.Error, "Too short"));
        cut.Find("textarea").ClassList.Should().Contain("tm-input-error");
    }

    [Fact]
    public void TmTextArea_Error_Shows_Error_Message()
    {
        var cut = Render<TmTextArea>(p => p.Add(c => c.Error, "Too short"));
        cut.Find("[data-testid='error-message']").TextContent.Should().Contain("Too short");
    }

    [Fact]
    public void TmTextArea_MaxLength_Applied()
    {
        var cut = Render<TmTextArea>(p => p.Add(c => c.MaxLength, 200));
        cut.Find("textarea").GetAttribute("maxlength").Should().Be("200");
    }

    [Fact]
    public void TmTextArea_ValueChanged_Fires_On_Change()
    {
        string? captured = null;
        var cut = Render<TmTextArea>(p => p
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => captured = v)));

        cut.Find("textarea").Change("hello world");

        captured.Should().Be("hello world");
    }

    /// <summary>
    /// Fáze 3 (plan 98c80bc3) binds the value as the <c>value</c> ATTRIBUTE, never as child text. A
    /// textarea's displayed value lives in the DOM <c>value</c> property — text between the tags is only
    /// the initial default — so a host that re-renders with a new bound value (a loaded draft, a reset,
    /// a simulated stream) used to leave the visible text stale, and hosts needed a keyed remount
    /// workaround to show it. The attribute form lets Blazor sync the property on every render.
    /// </summary>
    [Fact]
    public void TmTextArea_RendersTheBoundValueAsTheValueAttributeNotChildText()
    {
        var cut = Render<TmTextArea>(p => p.Add(c => c.Value, "draft text"));

        var textarea = cut.Find("textarea");
        textarea.GetAttribute("value").Should().Be("draft text",
            "the bound value rides the value attribute so Blazor can sync the DOM value property");
        textarea.TextContent.Should().BeEmpty(
            "child text is only the initial default and never updates — the value must not be there");
    }

    [Fact]
    public void TmTextArea_ExternalValueChangeReachesTheValueAttribute()
    {
        var cut = Render<TmTextArea>(p => p.Add(c => c.Value, "first"));

        cut.Render(p => p.Add(c => c.Value, "second"));

        cut.Find("textarea").GetAttribute("value").Should().Be("second");
    }

    // ── Required (accessibility) ─────────────────────────────────

    [Fact]
    public void TmTextArea_Required_SetsAriaRequiredOnTextarea()
    {
        var cut = Render<TmTextArea>(p => p.Add(c => c.Required, true));
        cut.Find("textarea").GetAttribute("aria-required").Should().Be("true");
    }

    [Fact]
    public void TmTextArea_Required_AddsRequiredMarkerClassToLabel()
    {
        var cut = Render<TmTextArea>(p => p
            .Add(c => c.Label, "Comment")
            .Add(c => c.Required, true));
        cut.Find("label").ClassList.Should().Contain("tm-input-label-required");
    }

    [Fact]
    public void TmTextArea_NotRequired_HasNoAriaRequiredAndNoMarker()
    {
        var cut = Render<TmTextArea>(p => p.Add(c => c.Label, "Comment"));
        cut.Find("textarea").HasAttribute("aria-required").Should().BeFalse();
        cut.Find("label").ClassList.Should().NotContain("tm-input-label-required");
    }

    // ── Invalid state (aria-invalid while a message shows) ───────

    [Fact]
    public void TmTextArea_Error_SetsAriaInvalidOnTextarea()
    {
        var cut = Render<TmTextArea>(p => p.Add(c => c.Error, "Required"));

        cut.Find("textarea").GetAttribute("aria-invalid").Should().Be("true");
    }

    /// <summary>
    /// A field-level error message (the <see cref="Tempo.Blazor.Components.Forms.TmFormField"/>
    /// error-id cascade the Signing steps use) must flag the textarea invalid too — the message
    /// the signer reads is described on the control.
    /// </summary>
    [Fact]
    public void TmTextArea_FormFieldErrorId_SetsAriaInvalidAndDescribedBy()
    {
        var cut = Render<TmTextArea>(p => p
            .Add(c => c.Label, "Comment")
            .AddCascadingValue(Tempo.Blazor.Components.Forms.TmFormField.ErrorIdCascadingName, "field-error-1"));

        cut.Find("textarea").GetAttribute("aria-invalid").Should().Be("true");
        cut.Find("textarea").GetAttribute("aria-describedby").Should().Be("field-error-1");
    }

    [Fact]
    public void TmTextArea_NoMessage_NoAriaInvalid()
    {
        var cut = Render<TmTextArea>(p => p.Add(c => c.Label, "Comment"));

        cut.Find("textarea").GetAttribute("aria-invalid").Should().BeNull();
    }

    // ── ReadOnly, stable id, AriaDescribedBy, LabelTrailing (plan 98c80bc3, Fáze 3) ──

    /// <summary>
    /// Fáze 3: <c>ReadOnly</c> is the readable "locked value" state — the native textarea carries the
    /// <c>readonly</c> attribute plus the readable <c>tm-input-readonly</c> surface, and it is NOT the
    /// disabled presentation (no <c>disabled</c> attribute, no disabled gray).
    /// </summary>
    [Fact]
    public void TmTextArea_ReadOnly_IsReadableAndNotDisabled()
    {
        var cut = Render<TmTextArea>(p => p
            .Add(c => c.Value, "Locked draft")
            .Add(c => c.ReadOnly, true));

        var textarea = cut.Find("textarea");
        textarea.HasAttribute("readonly").Should().BeTrue();
        textarea.ClassList.Should().Contain("tm-input-readonly");
        textarea.ClassList.Should().NotContain("tm-input-disabled");
        textarea.HasAttribute("disabled").Should().BeFalse(
            "readonly text stays readable and focusable — disabled is the different, gray state");
    }

    [Fact]
    public void TmTextArea_NotReadOnly_HasNoReadonlyState()
    {
        var cut = Render<TmTextArea>();

        var textarea = cut.Find("textarea");
        textarea.HasAttribute("readonly").Should().BeFalse();
        textarea.ClassList.Should().NotContain("tm-input-readonly");
    }

    [Fact]
    public void TmTextArea_DefaultId_IsPrefixedAndStableForTheInstance()
    {
        var cut = Render<TmTextArea>();

        var first = cut.Instance.Id;
        first.Should().MatchRegex("^tm-textarea-\\d+$",
            "the default id is the component prefix plus a sequence number, not a GUID fragment");

        cut.Render(p => p.Add(c => c.Value, "changed"));
        cut.Instance.Id.Should().Be(first, "the id never churns between parameter snapshots");

        Render<TmTextArea>().Instance.Id.Should().NotBe(first, "two instances still get distinct ids");
    }

    /// <summary>
    /// Fáze 3: <c>AriaDescribedBy</c> names extra ids the control must reference. The ids are APPENDED
    /// to the component's own chain (own error first, then the field cascade, then the host ids) and
    /// duplicates are dropped.
    /// </summary>
    [Fact]
    public void TmTextArea_AriaDescribedBy_AppendsToTheOwnChain()
    {
        var cut = Render<TmTextArea>(p => p.Add(c => c.AriaDescribedBy, "length-counter"));

        cut.Find("textarea").GetAttribute("aria-describedby").Should().Be("length-counter");

        var withError = Render<TmTextArea>(p => p
            .Add(c => c.Error, "Required")
            .Add(c => c.AriaDescribedBy, "length-counter"));

        withError.Find("textarea").GetAttribute("aria-describedby")
            .Should().Be($"{withError.Instance.Id}-error length-counter",
                "the own chain stays first, the host ids follow");
    }

    /// <summary>
    /// Fáze 3: the <c>LabelTrailing</c> slot renders at the end of the label row — the slot for the
    /// counter or the status chip a host used to overlay absolutely over the field. Without the slot
    /// the label markup is unchanged.
    /// </summary>
    [Fact]
    public void TmTextArea_LabelTrailing_RendersAtTheEndOfTheLabelRow()
    {
        var cut = Render<TmTextArea>(p => p
            .Add(c => c.Label, "Description")
            .Add(c => c.LabelTrailing, "<span id='desc-counter'>42</span>"));

        var row = cut.Find(".tm-input-label-row");
        row.Should().NotBeNull();
        row.QuerySelector("label")!.TextContent.Trim().Should().Be("Description");
        row.QuerySelector("#desc-counter")!.TextContent.Should().Be("42",
            "the slot content sits at the end of the label row");
    }

    [Fact]
    public void TmTextArea_WithoutLabelTrailing_KeepsThePlainLabelMarkup()
    {
        var cut = Render<TmTextArea>(p => p.Add(c => c.Label, "Description"));

        cut.FindAll(".tm-input-label-row").Should().BeEmpty(
            "no slot, no wrapper — the label markup is unchanged for hosts that do not use it");
    }

    /// <summary>
    /// Review round (plan 98c80bc3, Fáze 3, S3): the PRERENDERED markup must carry the bound value
    /// too. <see cref="Microsoft.AspNetCore.Components.Web.HtmlRenderer"/> is the headless static
    /// renderer an InteractiveAuto host prerenders with — no JS, no bUnit — and its output is the
    /// first HTML the browser paints before the circuit arrives. The bound value rides the
    /// <c>value</c> attribute there as well, so form.reset() and the browser restore the prerender
    /// value (CHANGELOG 2.12, "Test-visible changes", item 4).
    /// </summary>
    [Fact]
    public async Task TmTextArea_PrerenderedMarkupCarriesTheBoundValue()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Tempo.Blazor.Localization.ITmLocalizer>(BuildEnglishLocalizer());
        await using var provider = services.BuildServiceProvider();

        await using var htmlRenderer = new Microsoft.AspNetCore.Components.Web.HtmlRenderer(
            provider, provider.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>());

        var html = await htmlRenderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await htmlRenderer.RenderComponentAsync<TmTextArea>(
                Microsoft.AspNetCore.Components.ParameterView.FromDictionary(
                    new Dictionary<string, object?> { [nameof(TmTextArea.Value)] = "prerender draft" }));
            return output.ToHtmlString();
        });

        // The headless static renderer paints a textarea's "value" attribute as its initial CONTENT
        // (the HTML defaultValue) — exactly what the browser shows before the interactive circuit
        // arrives and what form.reset() restores.
        html.Should().Contain(">prerender draft</textarea>",
            "the prerendered textarea must already show the bound value");
    }
}

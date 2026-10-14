using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Inputs;

/// <summary>
/// Fáze 5: the <c>AnnounceError</c> contract applied consistently to every input component that
/// renders a validation error message. The default output is untouched (<c>role="alert"</c> on the
/// message, <c>aria-describedby</c> + <c>aria-invalid</c> on the control); setting
/// <c>AnnounceError="false"</c> drops ONLY the <c>role</c> attribute, so a host that moves focus to
/// the invalid field (the error is then read via <c>aria-describedby</c>) does not get the same
/// error announced twice by screen readers.
/// </summary>
public class TmInputAnnounceErrorTests : LocalizationTestBase
{
    // ── shared fixtures ─────────────────────────────────────────

    private record EntityRecord(int Id, string Name);

    private static Task<IEnumerable<EntityRecord>> EntitySearch(string query)
        => Task.FromResult<IEnumerable<EntityRecord>>([new EntityRecord(1, "Alice")]);

    private static readonly List<SelectOption<string>> FruitOptions =
    [
        SelectOption<string>.From("apple", "Apple"),
        SelectOption<string>.From("banana", "Banana"),
    ];

    /// <summary>
    /// Asserts the muted-error contract: no <c>role</c> anywhere on the message,
    /// <c>aria-invalid="true"</c> kept, and <c>aria-describedby</c> kept — matched against the
    /// message id programmatically, because several components generate their ids.
    /// </summary>
    private static void AssertMutedAnnouncement(IElement message, IElement control)
    {
        message.HasAttribute("role").Should().BeFalse(
            "AnnounceError=false must remove role=alert so the error is not announced twice");
        message.TextContent.Should().NotBeEmpty("the error text itself must still render");

        control.GetAttribute("aria-invalid").Should().Be("true");
        var describedBy = control.GetAttribute("aria-describedby");
        describedBy.Should().NotBeNullOrEmpty("aria-describedby must stay so focus reads the error");
        message.GetAttribute("id").Should().Be(describedBy,
            "aria-describedby must keep pointing at the rendered error message");
    }

    /// <summary>Asserts the default contract: role="alert" plus the describedby link.</summary>
    private static void AssertDefaultAnnouncement(IElement message, IElement control)
    {
        message.GetAttribute("role").Should().Be("alert");
        control.GetAttribute("aria-describedby").Should().Be(message.GetAttribute("id"));
        control.GetAttribute("aria-invalid").Should().Be("true");
    }

    // ── TmTextArea ──────────────────────────────────────────────

    [Fact]
    public void TmTextArea_Error_DefaultAnnouncesViaRoleAlert()
    {
        var cut = Render<TmTextArea>(p => p
            .Add(c => c.Id, "ta")
            .Add(c => c.Error, "Too long"));

        AssertDefaultAnnouncement(cut.Find(".tm-input-error-message"), cut.Find("textarea"));
    }

    [Fact]
    public void TmTextArea_AnnounceError_False_RoleDropped_DescriptionStays()
    {
        var cut = Render<TmTextArea>(p => p
            .Add(c => c.Id, "ta")
            .Add(c => c.Error, "Too long")
            .Add(c => c.AnnounceError, false));

        AssertMutedAnnouncement(cut.Find(".tm-input-error-message"), cut.Find("textarea"));
    }

    // ── TmSelect ────────────────────────────────────────────────

    [Fact]
    public void TmSelect_Error_DefaultAnnouncesViaRoleAlert()
    {
        var cut = Render<TmSelect<string>>(p => p
            .Add(c => c.Id, "sel")
            .Add(c => c.Options, FruitOptions)
            .Add(c => c.Error, "Pick one"));

        AssertDefaultAnnouncement(cut.Find(".tm-select-error-message"), cut.Find("select"));
    }

    [Fact]
    public void TmSelect_AnnounceError_False_RoleDropped_DescriptionStays()
    {
        var cut = Render<TmSelect<string>>(p => p
            .Add(c => c.Id, "sel")
            .Add(c => c.Options, FruitOptions)
            .Add(c => c.Error, "Pick one")
            .Add(c => c.AnnounceError, false));

        AssertMutedAnnouncement(cut.Find(".tm-select-error-message"), cut.Find("select"));
    }

    // ── TmNumberInput ───────────────────────────────────────────

    [Fact]
    public void TmNumberInput_Error_DefaultAnnouncesViaRoleAlert()
    {
        var cut = Render<TmNumberInput>(p => p
            .Add(c => c.Id, "num")
            .Add(c => c.Error, "Out of range"));

        AssertDefaultAnnouncement(cut.Find(".tm-input-error-message"), cut.Find("input"));
    }

    [Fact]
    public void TmNumberInput_AnnounceError_False_RoleDropped_DescriptionStays()
    {
        var cut = Render<TmNumberInput>(p => p
            .Add(c => c.Id, "num")
            .Add(c => c.Error, "Out of range")
            .Add(c => c.AnnounceError, false));

        AssertMutedAnnouncement(cut.Find(".tm-input-error-message"), cut.Find("input"));
    }

    // ── TmMultiSelect ───────────────────────────────────────────

    [Fact]
    public void TmMultiSelect_Error_DefaultAnnouncesViaRoleAlert()
    {
        var cut = Render<TmMultiSelect<SelectOption<string>, string>>(p => p
            .Add(c => c.Id, "multi")
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, o => o.Label)
            .Add(c => c.ValueField, o => o.Value)
            .Add(c => c.Error, "Select at least two"));

        AssertDefaultAnnouncement(cut.Find(".tm-input-error-message"), cut.Find(".tm-multiselect"));
    }

    [Fact]
    public void TmMultiSelect_AnnounceError_False_RoleDropped_DescriptionStays()
    {
        var cut = Render<TmMultiSelect<SelectOption<string>, string>>(p => p
            .Add(c => c.Id, "multi")
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, o => o.Label)
            .Add(c => c.ValueField, o => o.Value)
            .Add(c => c.Error, "Select at least two")
            .Add(c => c.AnnounceError, false));

        AssertMutedAnnouncement(cut.Find(".tm-input-error-message"), cut.Find(".tm-multiselect"));
    }

    // ── TmEntityPicker ──────────────────────────────────────────

    [Fact]
    public void TmEntityPicker_Error_DefaultAnnouncesViaRoleAlert()
    {
        var cut = Render<TmEntityPicker<EntityRecord, int>>(p => p
            .Add(c => c.SearchProvider, EntitySearch)
            .Add(c => c.ValueSelector, i => i.Id)
            .Add(c => c.DisplaySelector, i => i.Name)
            .Add(c => c.Error, "Required"));

        AssertDefaultAnnouncement(cut.Find(".tm-input-error-message"), cut.Find("input"));
    }

    [Fact]
    public void TmEntityPicker_AnnounceError_False_RoleDropped_DescriptionStays()
    {
        var cut = Render<TmEntityPicker<EntityRecord, int>>(p => p
            .Add(c => c.SearchProvider, EntitySearch)
            .Add(c => c.ValueSelector, i => i.Id)
            .Add(c => c.DisplaySelector, i => i.Name)
            .Add(c => c.Error, "Required")
            .Add(c => c.AnnounceError, false));

        AssertMutedAnnouncement(cut.Find(".tm-input-error-message"), cut.Find("input"));
    }

    // ── TmDecimalInput ──────────────────────────────────────────

    [Fact]
    public void TmDecimalInput_Error_DefaultAnnouncesViaRoleAlert()
    {
        var cut = Render<TmDecimalInput>(p => p
            .Add(c => c.Id, "dec")
            .Add(c => c.Error, "Invalid number"));

        AssertDefaultAnnouncement(cut.Find(".tm-input-error-message"), cut.Find("input"));
    }

    [Fact]
    public void TmDecimalInput_AnnounceError_False_RoleDropped_DescriptionStays()
    {
        var cut = Render<TmDecimalInput>(p => p
            .Add(c => c.Id, "dec")
            .Add(c => c.Error, "Invalid number")
            .Add(c => c.AnnounceError, false));

        AssertMutedAnnouncement(cut.Find(".tm-input-error-message"), cut.Find("input"));
    }

    // ── TmCodeEditor ────────────────────────────────────────────

    [Fact]
    public void TmCodeEditor_Error_DefaultAnnouncesViaRoleAlert()
    {
        var cut = Render<TmCodeEditor>(p => p
            .Add(c => c.Id, "code")
            .Add(c => c.Error, "Syntax error"));

        AssertDefaultAnnouncement(cut.Find(".tm-input-error-message"), cut.Find("textarea"));
    }

    [Fact]
    public void TmCodeEditor_AnnounceError_False_RoleDropped_DescriptionStays()
    {
        var cut = Render<TmCodeEditor>(p => p
            .Add(c => c.Id, "code")
            .Add(c => c.Error, "Syntax error")
            .Add(c => c.AnnounceError, false));

        AssertMutedAnnouncement(cut.Find(".tm-input-error-message"), cut.Find("textarea"));
    }

    // ── TmExpressionEditor ──────────────────────────────────────

    [Fact]
    public void TmExpressionEditor_Error_DefaultAnnouncesViaRoleAlert()
    {
        var cut = Render<TmExpressionEditor>(p => p
            .Add(c => c.Error, "Unknown variable"));

        AssertDefaultAnnouncement(cut.Find(".tm-expression-editor__error"), cut.Find("textarea"));
    }

    [Fact]
    public void TmExpressionEditor_AnnounceError_False_RoleDropped_DescriptionStays()
    {
        var cut = Render<TmExpressionEditor>(p => p
            .Add(c => c.Error, "Unknown variable")
            .Add(c => c.AnnounceError, false));

        AssertMutedAnnouncement(cut.Find(".tm-expression-editor__error"), cut.Find("textarea"));
    }

    // ── AnnounceError across re-renders (TmTextInput) ───────────

    /// <summary>
    /// The announcement decision is a per-render decision, not a one-time snapshot: render
    /// <c>TmTextInput</c> with <c>AnnounceError="false"</c> and no error, re-render with a new
    /// error — the message appears muted (no <c>role</c>, <c>aria-describedby</c> linked,
    /// <c>aria-invalid="true"</c>) — and flipping <c>AnnounceError</c> back to <c>true</c>
    /// promotes the same message to an assertive live region again.
    /// </summary>
    [Fact]
    public void TmTextInput_AnnounceError_FlipsAcrossRerenders()
    {
        var cut = Render<TmTextInput>(p => p
            .Add(c => c.Id, "amount")
            .Add(c => c.AnnounceError, false));

        cut.FindAll(".tm-input-error-message").Should().BeEmpty("no error is set yet");

        // The error arrives in a later render: the message appears, still muted.
        cut.Render(p => p.Add(c => c.Error, "Required"));

        AssertMutedAnnouncement(cut.Find(".tm-input-error-message"), cut.Find("input"));

        // Flipping the announcement back on re-promotes the same message to a live region.
        cut.Render(p => p.Add(c => c.AnnounceError, true));

        var message = cut.Find(".tm-input-error-message");
        message.GetAttribute("role").Should().Be("alert");
        cut.Find("input").GetAttribute("aria-describedby").Should().Be(message.GetAttribute("id"));
        cut.Find("input").GetAttribute("aria-invalid").Should().Be("true");
    }
}

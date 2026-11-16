using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Inputs;

/// <summary>
/// Review round (plan 98c80bc3, Fáze 3, B2): the read-only select must not let a user attempt linger.
/// These tests live in their own file to keep <c>TmSelectTests.cs</c> under the 1200-line ceiling
/// (N270/F13) — the class name keeps the TmSelect test family discoverable.
/// </summary>
public class TmSelectReadOnlyTests : LocalizationTestBase
{    /// <summary>
    /// Review round (plan 98c80bc3, Fáze 3, B2): a read-only select must not let the user's attempt
    /// linger — every option the locked value is NOT carrying renders <c>disabled</c>, so neither a
    /// pointer pick nor a keyboard typeahead can move the visible selection in the first place.
    /// The selected option itself stays enabled (it is the displayed value, not an offer).
    /// </summary>
    [Fact]
    public void TmSelect_ReadOnly_DisablesEveryOptionThatIsNotSelected()
    {
        var options = new List<SelectOption<string>>
        {
            new("cz", "Czech"),
            new("de", "German"),
            new("pl", "Polish", isDisabled: true),
        };
        var cut = Render<TmSelect<string>>(p => p
            .Add(c => c.Options, options)
            .Add(c => c.Value, "de")
            .Add(c => c.ReadOnly, true));

        var rendered = cut.FindAll("option");
        rendered.Single(o => o.GetAttribute("value") == "de").HasAttribute("disabled").Should().BeFalse(
            "the selected option is the displayed value and stays enabled");
        rendered.Single(o => o.GetAttribute("value") == "cz").HasAttribute("disabled").Should().BeTrue(
            "an unselected option is disabled while the select is read-only");
        rendered.Single(o => o.GetAttribute("value") == "pl").HasAttribute("disabled").Should().BeTrue(
            "an already-disabled option stays disabled");
    }

    [Fact]
    public void TmSelect_NotReadOnly_LeavesTheUnselectedOptionsEnabled()
    {
        var options = new List<SelectOption<string>>
        {
            new("cz", "Czech"),
            new("de", "German"),
        };
        var cut = Render<TmSelect<string>>(p => p
            .Add(c => c.Options, options)
            .Add(c => c.Value, "de"));

        cut.FindAll("option").Where(o => o.GetAttribute("value") == "cz")
            .Should().OnlyContain(o => !o.HasAttribute("disabled"),
                "the disabled-unselected behaviour belongs to ReadOnly alone");
    }

    [Fact]
    public void TmSelect_ReadOnly_DisablesTheSelectablePlaceholderOnceAValueIsSet()
    {
        // A locked value cannot be cleared: the selectable placeholder becomes disabled too, while
        // every option that is not the locked value stays disabled.
        var options = new List<SelectOption<int?>> { new(3, "Three") };
        var cut = Render<TmSelect<int?>>(p => p
            .Add(c => c.Placeholder, "Vyberte…")
            .Add(c => c.PlaceholderSelectable, true)
            .Add(c => c.Options, options)
            .Add(c => c.Value, (int?)3)
            .Add(c => c.ReadOnly, true));

        cut.FindAll("option")[0].HasAttribute("disabled").Should().BeTrue(
            "the placeholder must not offer a way out of the locked value");
    }

    [Fact]
    public void TmSelect_ReadOnly_KeepsThePlaceholderEnabledWhileItCarriesTheSelection()
    {
        // With no value selected the placeholder IS the displayed value (when selectable), so it
        // stays enabled — the disabled-unselected rule never touches what the select shows.
        var options = new List<SelectOption<int?>> { new(3, "Three") };
        var cut = Render<TmSelect<int?>>(p => p
            .Add(c => c.Placeholder, "Vyberte…")
            .Add(c => c.PlaceholderSelectable, true)
            .Add(c => c.Options, options)
            .Add(c => c.ReadOnly, true));

        cut.FindAll("option")[0].HasAttribute("disabled").Should().BeFalse(
            "the placeholder carrying the locked (empty) selection stays enabled");
        cut.FindAll("option")[1].HasAttribute("disabled").Should().BeTrue();
    }

    /// <summary>
    /// Review round (B2): when a host supplies raw <c>&lt;option&gt;</c> elements the component
    /// cannot disable them, so a browser could still move the DOM selection onto one. The guard
    /// keeps the value and — to not leave the attempt on screen — bumps the select's key so the
    /// element remounts from the rendered value. bUnit sees the aftermath: the selection markers,
    /// the preselection attribute and the class list are all still the locked value's.
    /// </summary>
    [Fact]
    public async Task TmSelect_ReadOnly_ChildContentAttempt_KeepsTheLockedValueInTheMarkup()
    {
        string? raised = null;
        var cut = Render<TmSelect<string>>(p => p
            .Add(c => c.Value, "sales")
            .Add(c => c.ReadOnly, true)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string?>(this, v => raised = v))
            .AddChildContent("<option value=\"dbo\">dbo</option><option value=\"sales\">sales</option>"));

        await cut.Find("select").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "dbo" });

        raised.Should().BeNull("the guard keeps the value even for raw ChildContent options");
        cut.Find("select").GetAttribute("value").Should().Be("sales",
            "the re-rendered select pins the locked value again — the attempt did not survive");
    }
}

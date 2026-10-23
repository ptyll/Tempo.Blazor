using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Navigation;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Navigation;

/// <summary>
/// The leave-confirmation dialog must speak the application's language even when the host passes nothing.
/// </summary>
/// <remarks>
/// <para>
/// Until 2.10.0 every text of <see cref="TmNavigationGuard"/> was a hardcoded ENGLISH default on the
/// parameter itself (<c>ConfirmTitle = "Unsaved changes"</c>, …). A host that rendered the guard without
/// passing all five strings therefore showed English inside a Czech/Slovak/French application — and the
/// host could only fix it by re-typing the very copy the library already ships in its resources. The
/// library was the one place that could not be localized by the localization it ships.
/// </para>
/// <para>
/// Fáze 11 localized the DEMO's answers, which hid the defect from the demo screenshots: the guard in
/// the demo passes every string explicitly. These tests therefore render the guard with NO text
/// parameters at all, through the same <see cref="MockTmLocalizer"/> the rest of the suite uses.
/// </para>
/// <para>
/// The English strings are asserted BYTE-IDENTICAL to the 2.9.x defaults on purpose: an E2E that finds
/// the dialog's button by its role name ("Leave", "Stay", "Save and leave") must not break because the
/// text moved from a C# default into a resource file.
/// </para>
/// </remarks>
public class TmNavigationGuardLocalizationTests : LocalizationTestBase
{
    private BunitNavigationManager Nav => Services.GetRequiredService<NavigationManager>() as BunitNavigationManager
        ?? throw new InvalidOperationException("BunitNavigationManager not registered.");

    [Fact]
    public void Defaults_ComeFromTheEnglishResources_WhenNothingIsPassed()
    {
        var cut = Render<TmNavigationGuard>(p => p.Add(x => x.IsDirty, true));

        OpenConfirmDialog(cut);

        cut.Find(".tm-dialog-title").TextContent.Trim().Should().Be("Unsaved changes");
        cut.Find(".tm-dialog-message").TextContent.Trim().Should().Be(
            "You have unsaved changes. If you leave now, they will be lost.");
        cut.Find(".tm-dialog-btn-ok").TextContent.Trim().Should().Be("Leave");
        cut.Find(".tm-dialog-btn-cancel").TextContent.Trim().Should().Be("Stay");
    }

    [Fact]
    public void Defaults_ComeFromTheConfiguredCulture_NotFromHardcodedEnglish()
    {
        // The proof that the strings are RESOLVED and not merely equal to a lucky English default: the
        // same render, the same parameters, a Czech localizer.
        Services.AddSingleton<ITmLocalizer>(BuildCzechLocalizer());

        var cut = Render<TmNavigationGuard>(p => p.Add(x => x.IsDirty, true));

        OpenConfirmDialog(cut);

        cut.Find(".tm-dialog-title").TextContent.Trim().Should().Be("Neuložené změny");
        cut.Find(".tm-dialog-btn-ok").TextContent.Trim().Should().Be("Odejít");
        cut.Find(".tm-dialog-btn-cancel").TextContent.Trim().Should().Be("Zůstat");
    }

    [Fact]
    public void Defaults_ResolveWhenTheHostPassesNull_AndNotTheKey()
    {
        var cut = Render<TmNavigationGuard>(p => p
            .Add(x => x.IsDirty, true)
            .Add(x => x.ConfirmTitle, (string?)null)
            .Add(x => x.ConfirmMessage, (string?)null)
            .Add(x => x.ConfirmLeaveText, (string?)null)
            .Add(x => x.CancelText, (string?)null));

        OpenConfirmDialog(cut);

        cut.Find(".tm-dialog-title").TextContent.Trim().Should().Be("Unsaved changes");
        cut.FindAll(".tm-dialog").Should().NotBeEmpty();
        cut.Markup.Should().NotContain("TmNavigationGuard_ConfirmTitle",
            "an unresolved key would be a worse failure than the English default it replaced");
    }

    [Fact]
    public void HostTexts_StillWin_OverTheLocalizedDefaults()
    {
        var cut = Render<TmNavigationGuard>(p => p
            .Add(x => x.IsDirty, true)
            .Add(x => x.ConfirmTitle, "Zahodit rozpracovaný e-mail?")
            .Add(x => x.ConfirmMessage, "Rozpracované změny se ztratí.")
            .Add(x => x.ConfirmLeaveText, "Zahodit")
            .Add(x => x.CancelText, "Zpět"));

        OpenConfirmDialog(cut);

        cut.Find(".tm-dialog-title").TextContent.Trim().Should().Be("Zahodit rozpracovaný e-mail?");
        cut.Find(".tm-dialog-message").TextContent.Trim().Should().Be("Rozpracované změny se ztratí.");
        cut.Find(".tm-dialog-btn-ok").TextContent.Trim().Should().Be("Zahodit");
        cut.Find(".tm-dialog-btn-cancel").TextContent.Trim().Should().Be("Zpět");
    }

    [Fact]
    public void SaveAndLeaveButton_IsLocalizedToo_AndOnlyShownWithTheCallback()
    {
        var cut = Render<TmNavigationGuard>(p => p
            .Add(x => x.IsDirty, true)
            .Add(x => x.OnSaveAndLeave, () => Task.FromResult(true)));

        OpenConfirmDialog(cut);

        cut.FindAll(".tm-dialog-footer button").Select(b => b.TextContent.Trim())
            .Should().Equal("Stay", "Save and leave", "Leave");
    }

    [Fact]
    public void SaveAndLeaveText_ResolvesFromTheCzechResources()
    {
        Services.AddSingleton<ITmLocalizer>(BuildCzechLocalizer());

        var cut = Render<TmNavigationGuard>(p => p
            .Add(x => x.IsDirty, true)
            .Add(x => x.OnSaveAndLeave, () => Task.FromResult(true)));

        OpenConfirmDialog(cut);

        cut.FindAll(".tm-dialog-footer button").Select(b => b.TextContent.Trim())
            .Should().Equal("Zůstat", "Uložit a odejít", "Odejít");
    }

    private void OpenConfirmDialog(IRenderedComponent<TmNavigationGuard> cut)
    {
        Nav.NavigateTo("/next-page");
        cut.WaitForState(() => Nav.History.Count > 0);
        Nav.History.First().State.Should().Be(NavigationState.Prevented);
        cut.WaitForElement(".tm-dialog");
    }
}

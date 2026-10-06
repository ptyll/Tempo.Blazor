using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Feedback;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>
/// B1: <c>TmDialog</c> used to ship English literals as the default button texts
/// (<c>OkButtonText = "OK"</c>, <c>CancelButtonText = "Cancel"</c>), so every caller that
/// did not pass its own text rendered "Cancel" in a Czech UI. The defaults must come from
/// the localizer (<c>Tm_Ok</c> / <c>Tm_Cancel</c>) — an explicit parameter still wins.
/// </summary>
public class TmDialogLocalizationTests : LocalizationTestBase
{
    public TmDialogLocalizationTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void DefaultButtons_UseLocalizedTexts()
    {
        UseCzechLocalization();

        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Type, DialogType.Confirm)
            .Add(d => d.Title, "Smazat")
            .Add(d => d.Message, "Opravdu?"));

        cut.Find(".tm-dialog-btn-cancel").TextContent.Trim()
            .Should().Be("Zrušit",
                "B1: the default cancel text must come from Loc[\"Tm_Cancel\"], not the English literal");
        cut.Find(".tm-dialog-btn-ok").TextContent.Trim()
            .Should().Be("OK",
                "B1: the default ok text must come from Loc[\"Tm_Ok\"], not a hardcoded parameter default");
    }

    [Fact]
    public void ExplicitButtonTexts_OverrideTheLocalizer()
    {
        UseCzechLocalization();

        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Type, DialogType.Confirm)
            .Add(d => d.Title, "Smazat")
            .Add(d => d.OkButtonText, "Ano, smazat")
            .Add(d => d.CancelButtonText, "Nechat"));

        cut.Find(".tm-dialog-btn-cancel").TextContent.Trim().Should().Be("Nechat");
        cut.Find(".tm-dialog-btn-ok").TextContent.Trim().Should().Be("Ano, smazat");
    }
}

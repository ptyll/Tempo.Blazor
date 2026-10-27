using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>
/// Exercises the decorator documented in the <c>ITmLocalizer</c> JSON docs —
/// <see cref="AppTranslationOverrideLocalizer"/> in DocumentedTranslationOverrideLocalizer.cs,
/// compiled so the documented sample cannot rot — through real DI: registered AFTER
/// <c>AddTempoBlazor()</c> it wins (the last <c>ITmLocalizer</c> registration), its listed keys
/// return the override and everything else falls through to the shipped table, checked here
/// against the Slovak resources under <see cref="CultureInfo"/>("sk").
/// </summary>
public class DocumentedTranslationOverrideLocalizerTests
{
    [Fact]
    public void OverriddenKeysWin_AndEverythingElseFallsThroughToTheShippedSlovakTable()
    {
        var uiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("sk");

            var services = new ServiceCollection();
            services.AddTempoBlazor();
            services.AddSingleton<ITmLocalizer, AppTranslationOverrideLocalizer>();
            using var provider = services.BuildServiceProvider();
            var localizer = provider.GetRequiredService<ITmLocalizer>();

            localizer.Should().BeOfType<AppTranslationOverrideLocalizer>(
                "the last ITmLocalizer registration wins over AddTempoBlazor()'s TryAddSingleton");

            // Overridden keys return the override — plain and formatted.
            localizer["TmDataTable_SaveView"].Should().Be("Uložit pohled jako…");
            localizer["Tm_Search"].Should().Be("Hledat…");

            // Every other key falls through to the shipped Slovak table.
            localizer["TmFileDropZone_DragDrop"].Should().Be("Presuňte sem súbory");

            // Plural resolves the family per the localizer's culture. The Modeling issue count is
            // now a NEUTRAL single template (Modeling redesign plan, phase 1), so the override
            // serves every count through the plain-key fall-through of Plural().
            localizer.Plural("TmModelingIssuePanel_Count", 1).Should().Be("Celkem nálezů: 1");
            localizer.Plural("TmModelingIssuePanel_Count", 3).Should().Be("Celkem nálezů: 3");
            localizer.Plural("TmModelingIssuePanel_Count", 7).Should().Be("Celkem nálezů: 7");
        }
        finally
        {
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }
}

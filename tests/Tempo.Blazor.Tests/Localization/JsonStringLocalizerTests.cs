using System.Globalization;
using FluentAssertions;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Resources;
using Xunit;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>
/// Exercises the real <see cref="JsonStringLocalizer{TResourceSource}"/> against the JSON resources
/// embedded in Tempo.Blazor.dll. This is the resolution path used at runtime (under both Server and
/// WebAssembly), unlike the inline <c>MockTmLocalizer</c> the component tests use.
/// </summary>
public class JsonStringLocalizerTests
{
    // Culture is read PER LOOKUP (the localizer walks CultureInfo.CurrentUICulture at indexer time),
    // so every test pins the culture with a CultureScope for its whole body — a bare
    // "CurrentUICulture = …" in a helper leaked the language into every later test on the thread.
    private static readonly JsonStringLocalizer<TmResources> Localizer = new();

    private static CultureScope UseCulture(string culture) => CultureScope.Use(culture);

    [Fact]
    public void Resolves_czech_value()
    {
        using var _ = UseCulture("cs");
        Localizer["TmFileDropZone_DragDrop"].Value.Should().Be("Přetáhněte soubory sem");
    }

    [Fact]
    public void Resolves_slovak_value()
    {
        using var _ = UseCulture("sk");
        Localizer["TmFileDropZone_DragDrop"].Value.Should().Be("Presuňte sem súbory");
    }

    [Fact]
    public void Slovak_region_culture_falls_back_to_slovak() // sk-SK → sk
    {
        using var _ = UseCulture("sk-SK");
        Localizer["TmFileDropZone_DragDrop"].Value.Should().Be("Presuňte sem súbory");
    }

    [Fact]
    public void Slovak_resolves_the_color_palette_keys_reported_missing_by_applications()
    {
        // SpravaFinanci reported these keys missing in sk (plan Fáze 3 context).
        using var _ = UseCulture("sk");
        Localizer["TmColorPalette_SelectColor"].Value.Should().Be("Vybrať farbu");
        Localizer["TmColorPalette_Clear"].Value.Should().Be("Vymazať");
        Localizer["TmColorPalette_SelectColorNamed", "Červená"].Value.Should().Be("Vybrať farbu Červená");
    }

    /// <summary>
    /// A member whose subject IS the neutral table must reach it through
    /// <see cref="CultureInfo.InvariantCulture"/> rather than through <c>en</c>: pinning <c>en</c>
    /// looks equivalent only until <c>TmResources.en.json</c> exists; the day it does, <c>en</c>
    /// would read that file and leave this assertion green over a different subject.
    /// <c>InvariantCulture</c>'s chain is the neutral table alone (<c>BuildChain</c> stops on an
    /// empty <c>CultureInfo.Name</c>), so this member stays on <c>TmResources.json</c> by
    /// construction. The rule is scoped to the SUBJECT: it binds a member that asserts WHICH table
    /// served a value, not one that pins a culture only to spell an expectation. Such a pin also
    /// buys less than it looks — the scope sets <c>CurrentUICulture</c>, which selects the
    /// table; the argument-taking indexer still formats under the ambient <c>CurrentCulture</c>
    /// (<c>string.Format(CultureInfo.CurrentCulture, …)</c> in <c>JsonStringLocalizer</c>).
    /// The sibling <see cref="Unknown_culture_falls_back_to_neutral"/> still covers the key
    /// through <c>de</c>. (<c>UseCulture("")</c> passes <c>CultureInfo.GetCultureInfo("")</c>: an
    /// empty <c>Name</c>, equal to <see cref="CultureInfo.InvariantCulture"/> though not the same
    /// instance, so it takes that same neutral-only chain.)
    /// </summary>
    [Fact]
    public void Resolves_neutral_english_value()
    {
        using var _ = UseCulture("");
        Localizer["TmFileDropZone_DragDrop"].Value.Should().Be("Drag and drop files here");
    }

    [Fact]
    public void Region_culture_falls_back_to_language() // cs-CZ → cs
    {
        using var _ = UseCulture("cs-CZ");
        Localizer["TmDataTable_ShowingItems"].Value.Should().Be("Zobrazeno {0}–{1} z {2}");
    }

    [Fact]
    public void Unknown_culture_falls_back_to_neutral() // de has no table → neutral English
    {
        using var _ = UseCulture("de");
        Localizer["TmFileDropZone_DragDrop"].Value.Should().Be("Drag and drop files here");
    }

    [Fact]
    public void Missing_key_returns_the_key_and_flags_not_found()
    {
        using var _ = UseCulture("cs");
        var localized = Localizer["This_Key_Does_Not_Exist_12345"];

        localized.Value.Should().Be("This_Key_Does_Not_Exist_12345");
        localized.ResourceNotFound.Should().BeTrue();
    }

    [Fact]
    public void Found_key_is_flagged_resource_found()
    {
        using var _ = UseCulture("cs");
        Localizer["TmFileDropZone_DragDrop"].ResourceNotFound.Should().BeFalse();
    }

    [Fact]
    public void Formats_placeholder_arguments()
    {
        using var _ = UseCulture("en");
        Localizer["TmDataTable_ShowingItems", 1, 10, 42].Value.Should().Be("Showing 1–10 of 42");
    }

    [Fact]
    public void Tm_Time_date_format_is_a_real_format_string_not_the_key()
    {
        // Regression for the scrambled-timestamp bug: the key must resolve to a date format pattern.
        // (The old TmTimeline_DateFormat keys were removed in 2.9.1 — the formatter reads Tm_Time_DateFormat.)
        using (UseCulture("en"))
            Localizer["Tm_Time_DateFormat"].Value.Should().Be("MMM d, yyyy");
        using (UseCulture("cs"))
            Localizer["Tm_Time_DateFormat"].Value.Should().Be("d. M. yyyy");
    }

    [Fact]
    public void GetAllStrings_includes_neutral_keys_with_parent_cultures()
    {
        using var _ = UseCulture("cs");
        var all = Localizer
            .GetAllStrings(includeParentCultures: true)
            .Select(s => s.Name)
            .ToHashSet(StringComparer.Ordinal);

        all.Should().Contain("TmFileDropZone_DragDrop");
        all.Should().Contain("TmDataTable_ShowingItems");
    }
}

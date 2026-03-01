using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Files;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>
/// RED tests – TmFileDropZone must use ITmLocalizer instead of hardcoded strings.
/// </summary>
public class TmFileDropZoneLocalizationTests : LocalizationTestBase
{
    [Fact]
    public void TmFileDropZone_DefaultHint_UsesLocalizer_DragDrop()
    {
        // Czech localizer is registered
        UseCzechLocalization();

        var cut = RenderComponent<TmFileDropZone>();

        // Must render the localized "Přetáhněte soubory sem" (not hardcoded English)
        cut.Find(".tm-file-drop-zone__hint").TextContent
            .Should().Contain("Přetáhněte soubory sem");
    }

    [Fact]
    public void TmFileDropZone_DefaultHint_UsesLocalizer_Browse()
    {
        UseCzechLocalization();

        var cut = RenderComponent<TmFileDropZone>();

        cut.Find(".tm-file-drop-zone__browse").TextContent
            .Should().Contain("Vybrat soubory");
    }

    [Fact]
    public void TmFileDropZone_DefaultHint_English_ShowsEnglishText()
    {
        // English (default) – already registered by base class

        var cut = RenderComponent<TmFileDropZone>();

        cut.Find(".tm-file-drop-zone__hint").TextContent
            .Should().Contain("Drag and drop files here");
    }

    [Fact]
    public void TmFileDropZone_ChildContent_Overrides_DefaultHint()
    {
        // When ChildContent is provided, the default hint must NOT appear
        var cut = RenderComponent<TmFileDropZone>(p => p
            .AddChildContent("<span class=\"custom-slot\">Custom content</span>"));

        cut.FindAll(".tm-file-drop-zone__hint").Should().BeEmpty();
        cut.Find(".custom-slot").TextContent.Should().Be("Custom content");
    }
}

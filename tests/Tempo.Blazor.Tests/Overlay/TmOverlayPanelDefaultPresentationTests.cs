using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Overlay;

/// <summary>
/// F3 review round 1 (T1): <see cref="TmOverlayPanel.MobilePresentation"/> defaults to
/// <see cref="PanelPresentation.Popover"/>. The anchored panel only becomes a sheet when a
/// consumer explicitly opts into <c>Auto</c>/<c>Sheet</c> — a sheet moves focus to the header
/// and breaks surfaces whose anchor keeps focus (typeaheads, caret menus, inline toolbars).
/// </summary>
public class TmOverlayPanelDefaultPresentationTests : LocalizationTestBase
{
    [Fact]
    public void DefaultPresentation_IsPopover_OnMobileViewport()
    {
        var viewport = new TmLayoutContext(TmLayoutMode.Mobile, TmLayoutMode.Mobile);

        var cut = Render(builder =>
        {
            builder.OpenComponent<CascadingValue<TmLayoutContext>>(0);
            builder.AddAttribute(1, "Name", TmLayoutScopes.Viewport);
            builder.AddAttribute(2, "Value", viewport);
            builder.AddAttribute(3, "ChildContent", (RenderFragment)(b =>
            {
                b.OpenComponent<TmOverlayPanel>(0);
                b.AddAttribute(1, "IsOpen", true);
                b.AddAttribute(2, "Title", "Filters");
                b.AddAttribute(3, "ChildContent", (RenderFragment)(bb => bb.AddContent(0, "Body")));
                b.CloseComponent();
            }));
            builder.CloseComponent();
        });

        cut.FindAll(".tm-overlay-panel").Should().HaveCount(1);
        cut.FindAll(".tm-drawer").Should().BeEmpty();
    }

    [Fact]
    public void ForcedLayoutModeMobile_WithDefaultPresentation_RendersPopover()
    {
        // A combobox consumer that never sets MobilePresentation keeps its anchored popover
        // even when the layout is forced mobile — the sheet would steal focus from the input.
        var cut = Render<TmOverlayPanel>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .AddChildContent("<div>Body</div>"));

        cut.FindAll(".tm-overlay-panel").Should().HaveCount(1);
        cut.FindAll(".tm-drawer").Should().BeEmpty();
    }
}

/// <summary>
/// Source sweep: the known focus-keeping consumers never opt into Auto/Sheet on their
/// TmOverlayPanel, and the deliberate sheet opt-ins (column picker, the three date pickers)
/// carry an explicit <c>MobilePresentation="PanelPresentation.Auto"</c>.
/// </summary>
public class TmOverlayPanelMobilePresentationSweepTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    // Quote-aware tag match: attribute values may contain '>' (e.g. lambdas "@(v => ...)"),
    // which a naive [^>]*? scan mis-reads as the end of the tag.
    private static readonly Regex OverlayPanelTag =
        new(@"<TmOverlayPanel\b(?:[^>""]|""[^""]*"")*>", RegexOptions.Compiled | RegexOptions.Singleline, RegexTimeout);

    private static readonly Regex AutoOrSheet =
        new(@"MobilePresentation\s*=\s*""(?:PanelPresentation\.)?(?:Auto|Sheet)""", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex ExplicitAuto =
        new(@"MobilePresentation\s*=\s*""PanelPresentation\.Auto""", RegexOptions.Compiled, RegexTimeout);

    // Surfaces whose anchor keeps focus (typeahead, caret menus, inline toolbars): presenting
    // them as a modal sheet moves focus to the sheet header and loses keystrokes.
    private static readonly string[] FocusKeepingConsumers =
    [
        "TmEntityPicker.razor",
        "TmQueryInput.razor",
        "TmMultiColumnComboBox.razor",
        "TmTagPicker.razor",
        "TmCommentMentionInput.razor",
        "TmNotionMentionMenu.razor",
        "TmNotionAiMenu.razor",
        "TmFilterableDropdown.razor",
        "TmMultiSelect.razor",
    ];

    // Deliberate sheet opt-ins (verified by behaviour tests elsewhere).
    private static readonly string[] SheetOptIns =
    [
        "TmColumnPicker.razor",
        "TmDatePicker.razor",
        "TmDateRangePicker.razor",
        "TmDateTimePicker.razor",
    ];

    [Theory]
    [MemberData(nameof(FocusKeepingConsumerData))]
    public void FocusKeepingConsumer_DoesNotOptIntoAutoOrSheet(string fileName)
    {
        var tag = OverlayPanelTagIn(fileName);

        AutoOrSheet.IsMatch(tag).Should().BeFalse(
            $"{fileName}'s panel keeps its anchor focused — a sheet presentation would steal " +
            "keystrokes. Rely on the Popover default or pass PanelPresentation.Popover explicitly.");
    }

    [Theory]
    [MemberData(nameof(SheetOptInData))]
    public void SheetOptIn_DeclaresExplicitAuto(string fileName)
    {
        var tag = OverlayPanelTagIn(fileName);

        ExplicitAuto.IsMatch(tag).Should().BeTrue(
            $"{fileName} presents as a bottom sheet on mobile and must opt in explicitly with " +
            "MobilePresentation=\"PanelPresentation.Auto\" — the TmOverlayPanel default is Popover.");
    }

    public static TheoryData<string> FocusKeepingConsumerData => new(FocusKeepingConsumers);

    public static TheoryData<string> SheetOptInData => new(SheetOptIns);

    private static string OverlayPanelTagIn(string fileName)
    {
        var root = ThemeCss.RepositoryRoot().FullName;
        var path = Directory
            .EnumerateFiles(Path.Combine(root, "src"), fileName, SearchOption.AllDirectories)
            .FirstOrDefault(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
        path.Should().NotBeNull($"the sweep's denominator is the {fileName} source under src/");

        var razor = File.ReadAllText(path!);
        var match = OverlayPanelTag.Match(razor);
        match.Success.Should().BeTrue($"{fileName} must render its panel through a TmOverlayPanel tag");
        return match.Value;
    }
}

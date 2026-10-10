using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Toolbar;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Toolbar;

/// <summary>
/// F4: visible button labels. <see cref="TmToolbarButton.LabelPosition"/> places the label inline,
/// below the icon or hides it; <see cref="TmToolbar.Labels"/> decides for the toolbar — Auto shows
/// labels and drops them (CSS container query on the TOOLBAR's width, so there is no flash and no JS)
/// below the mobile breakpoint. A hidden label is never an empty span, and the text stays the
/// accessible name.
/// </summary>
public class TmToolbarButtonLabelTests : LocalizationTestBase
{
    /// <summary>Renders the button standalone — no toolbar around it.</summary>
    private IRenderedComponent<TmToolbarButton> RenderButton(
        Action<ComponentParameterCollectionBuilder<TmToolbarButton>> configure)
        => Render<TmToolbarButton>(configure);

    /// <summary>
    /// Renders the button as the child of a real <see cref="TmToolbar"/> with the given
    /// <c>Labels</c> (the button reads it through the cascade).
    /// </summary>
    private IRenderedComponent<TmToolbar> RenderInToolbar(
        ToolbarLabels labels,
        string? icon = "plus",
        string? text = "New",
        string? tooltip = null,
        ToolbarLabelPosition position = ToolbarLabelPosition.Inline)
        => Render<TmToolbar>(p => p
            .Add(c => c.Labels, labels)
            .Add(c => c.ChildContent, builder =>
            {
                builder.OpenComponent<TmToolbarButton>(0);
                builder.AddComponentParameter(1, nameof(TmToolbarButton.Icon), icon);
                builder.AddComponentParameter(2, nameof(TmToolbarButton.Text), text);
                builder.AddComponentParameter(3, nameof(TmToolbarButton.Tooltip), tooltip);
                builder.AddComponentParameter(4, nameof(TmToolbarButton.LabelPosition), position);
                builder.CloseComponent();
            }));
    [Fact]
    public void LabelBelow_RendersVisibleLabel()
    {
        var cut = RenderButton(p => p
            .Add(c => c.Icon, "plus").Add(c => c.Text, "New").Add(c => c.LabelPosition, ToolbarLabelPosition.Below));

        var button = cut.Find("button.tm-toolbar-btn");
        button.ClassList.Should().Contain("tm-toolbar-btn--label-below");
        button.QuerySelector(".tm-toolbar-btn-text")!.TextContent.Should().Be("New");
        button.QuerySelector(".tm-icon").Should().NotBeNull("the icon sits above the label");
    }

    [Fact]
    public void LabelInline_IsTheDefault_AndCarriesNoBelowModifier()
    {
        var cut = RenderButton(p => p.Add(c => c.Icon, "plus").Add(c => c.Text, "New"));

        cut.Find("button.tm-toolbar-btn").ClassList.Should().NotContain("tm-toolbar-btn--label-below");
        cut.Find(".tm-toolbar-btn-text").TextContent.Should().Be("New");
    }

    [Fact]
    public void LabelHidden_OmitsTheSpan_AndKeepsTheTextAsTheAccessibleNameAndTitle()
    {
        var cut = RenderButton(p => p
            .Add(c => c.Icon, "plus").Add(c => c.Text, "New").Add(c => c.LabelPosition, ToolbarLabelPosition.Hidden));

        var button = cut.Find("button.tm-toolbar-btn");
        cut.FindAll(".tm-toolbar-btn-text").Should().BeEmpty("a hidden label is omitted, not an empty span");
        button.GetAttribute("aria-label").Should().Be("New");
        button.GetAttribute("title").Should().Be("New");
        button.ClassList.Should().Contain("tm-toolbar-btn--icon-only");
    }

    [Fact]
    public void TextOnlyButton_NeverHidesItsLabel()
    {
        var cut = RenderInToolbar(ToolbarLabels.Icons, icon: null, text: "Export", position: ToolbarLabelPosition.Hidden);

        cut.Find(".tm-toolbar-btn-text").TextContent.Should().Be("Export",
            "without an icon the text is the only thing there is to click");
    }

    [Fact]
    public void LabelsAuto_SwitchesByContainerWidth()
    {
        var cut = RenderInToolbar(ToolbarLabels.Auto);

        var button = cut.Find("button.tm-toolbar-btn");
        button.ClassList.Should().Contain("tm-toolbar-btn--labels-auto", "the CSS hook the container query drops the label by");
        cut.Find(".tm-toolbar-btn-text").Should().NotBeNull("Auto renders the label; narrow widths hide it in CSS");
        button.GetAttribute("aria-label").Should().Be("New", "a CSS-hidden label must not leave the button nameless");

        var css = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Tempo.Blazor", "wwwroot", "css", "components", "_toolbar.css"));
        css.Should().MatchRegex(@"@container\s+tm-toolbar\s*\(\s*width\s*<\s*640px\s*\)\s*\{[^@]*\.tm-toolbar-btn--labels-auto\s+\.tm-toolbar-btn-text\s*\{[^}]*display:\s*none",
            "Auto drops icon-button labels below TmBreakpoints.Sm measured on the toolbar itself (F1 contract: half-open range, constant width)");
    }

    [Fact]
    public void LabelsIcons_OmitsIconButtonLabels_ButIsStillNamed()
    {
        var cut = RenderInToolbar(ToolbarLabels.Icons);

        cut.FindAll(".tm-toolbar-btn-text").Should().BeEmpty();
        cut.Find("button.tm-toolbar-btn").GetAttribute("aria-label").Should().Be("New");
    }

    [Fact]
    public void LabelsIconsWithText_KeepsLabels_WithoutTheAutoHook()
    {
        var cut = RenderInToolbar(ToolbarLabels.IconsWithText);

        cut.Find(".tm-toolbar-btn-text").TextContent.Should().Be("New");
        cut.Find("button.tm-toolbar-btn").ClassList.Should().NotContain("tm-toolbar-btn--labels-auto");
    }

    [Fact]
    public void ButtonLabelHidden_WinsOverToolbarLabelsIconsWithText()
    {
        var cut = RenderInToolbar(ToolbarLabels.IconsWithText, position: ToolbarLabelPosition.Hidden);

        cut.FindAll(".tm-toolbar-btn-text").Should().BeEmpty();
    }

    [Fact]
    public void Tooltip_StaysTheAccessibleName_WhenGiven()
    {
        var cut = RenderInToolbar(ToolbarLabels.Auto, tooltip: "Create a document");

        var button = cut.Find("button.tm-toolbar-btn");
        button.GetAttribute("aria-label").Should().Be("Create a document");
        button.GetAttribute("title").Should().Be("Create a document");
    }

    [Fact]
    public void Standalone_IgnoresPriority_AndRendersAsBefore()
    {
        var cut = RenderButton(p => p
            .Add(c => c.Icon, "plus").Add(c => c.Text, "New").Add(c => c.Priority, ToolbarButtonPriority.OverflowOnly));

        cut.Find("button.tm-toolbar-btn").TextContent.Trim().Should().Be("New", "without a toolbar there is no overflow to be in");
    }

    [Fact]
    public void DanglingDivider_IsHiddenByVisibility_NotByDisplay_SoTheMeasuredWidthStaysStable()
    {
        // A divider with no visible button after it (everything behind it collapsed into More) is a
        // stray rule. It must keep its box - display:none would change the measured fixed width and
        // make the fit measurement oscillate at the boundary (hide -> room -> expand -> show -> no room).
        var css = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Tempo.Blazor", "wwwroot", "css", "components", "_toolbar.css"));

        css.Should().MatchRegex(
            @"\.tm-toolbar-divider:has\(~\s*\.tm-toolbar-item--collapsed\):not\(:has\(~\s*\.tm-toolbar-btn:not\(\.tm-toolbar-item--collapsed\)\)\)\s*\{[^}]*visibility:\s*hidden",
            "a divider with collapsed buttons after it and no visible one is hidden with visibility - and ONLY then: a divider before " +
            "non-button content (TmDiagramEditor groups) or in a toolbar that collapses nothing must stay");
        css.Should().NotMatchRegex(
            @"\.tm-toolbar-divider:[^{]*\{[^}]*display:\s*none",
            "display:none would feed back into the measurement");
    }
    [Fact]
    public void Ribbon_LabelBelowTiles_AreRoomyAndDividersSpanTheTileHeight()
    {
        // UX review vs diagram-editor--tablet.png: ribbon tiles have a roomy hit area (space-2 / space-3
        // padding, not the 4px/8px of an inline button) and the group dividers run the tile's height.
        var css = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Tempo.Blazor", "wwwroot", "css", "components", "_toolbar.css"));

        css.Should().MatchRegex(@"\.tm-toolbar-btn--label-below\s*\{[^}]*padding:\s*var\(--tm-space-2\)\s+var\(--tm-space-3\)");
        css.Should().MatchRegex(@"\.tm-toolbar:has\(\.tm-toolbar-btn--label-below\)\s+\.tm-toolbar-divider\s*\{[^}]*height:\s*2\.5rem");
    }
    [Fact]
    public void ToolbarStylesheet_HasNoStrayEscapeSequences_AndTheMoreTriggerKeepsItsFullRule()
    {
        // A literal backslash-n in a stylesheet (a scripted edit that did not expand the escape) makes the
        // browser drop the rule that FOLLOWS it - here the More trigger shrank to 16x24 on a phone.
        var css = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Tempo.Blazor", "wwwroot", "css", "components", "_toolbar.css"));
        var bundle = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Tempo.Blazor", "wwwroot", "css", "tempo-blazor.bundled.css"));

        css.Should().NotContain("\\n", "no escape sequence belongs in a stylesheet");
        bundle.Should().NotMatchRegex(@"\\n\s*\.tm-toolbar", "the committed bundle must be rebuilt from the clean source");
        css.Should().MatchRegex(@"\.tm-toolbar-more\s*\{[^}]*min-width:\s*var\(--tm-touch-target\)");
    }
    private static string ToolbarCss()
        => File.ReadAllText(Path.Combine(RepoRoot(), "src", "Tempo.Blazor", "wwwroot", "css", "components", "_toolbar.css"));

    /// <summary>The declaration block of the rule whose selector list is exactly <paramref name="selector"/> (null when absent).</summary>
    private static string? RuleBody(string css, string selector)
    {
        var withoutComments = System.Text.RegularExpressions.Regex.Replace(css, @"/\*.*?\*/", " ", System.Text.RegularExpressions.RegexOptions.Singleline);
        var match = System.Text.RegularExpressions.Regex.Match(
            withoutComments, @"(?:^|[}\s])" + System.Text.RegularExpressions.Regex.Escape(selector) + @"\s*\{([^}]*)\}");
        return match.Success ? match.Groups[1].Value : null;
    }

    [Fact]
    public void ToolbarButtons_NeverShrink_ScopedToTheToolbar_SoTheMeasuredWidthIsTheNaturalWidth()
    {
        // A flex item shrinks to its min-width (the 44px touch target on a coarse pointer) before the
        // row overflows; the fit measurement then sees 44px buttons whose text spills over the next one.
        // H5 (architect M3): ONLY inside a .tm-toolbar - a standalone .tm-toolbar-btn (TmDiagramEditor's ~32
        // buttons, TmModelingDiagramPreview) keeps the pre-F4 flex behaviour the CHANGELOG promises.
        var css = ToolbarCss();

        RuleBody(css, ".tm-toolbar .tm-toolbar-btn").Should().NotBeNull().And.MatchRegex(@"flex-shrink:\s*0");
        RuleBody(css, ".tm-toolbar-btn").Should().NotBeNull().And.NotContain("flex-shrink",
            "the unscoped base rule must not change standalone buttons");
    }

    [Fact]
    public void CoarsePointerTouchTarget_AppliesInsideTheToolbarOnly()
    {
        var css = System.Text.RegularExpressions.Regex.Replace(ToolbarCss(), @"/\*.*?\*/", " ", System.Text.RegularExpressions.RegexOptions.Singleline);
        var media = System.Text.RegularExpressions.Regex.Match(css, @"@media\s*\(pointer:\s*coarse\)\s*\{(.*?)\n\}", System.Text.RegularExpressions.RegexOptions.Singleline);

        media.Success.Should().BeTrue("the coarse-pointer block exists");
        var selectors = media.Groups[1].Value.Split('{')[0].Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        selectors.Should().Contain(".tm-toolbar .tm-toolbar-btn").And.Contain(".tm-toolbar .tm-toolbar-more");
        selectors.Should().NotContain(".tm-toolbar-btn", "a standalone toolbar button is not forced to 44px");
    }

    [Theory]
    [InlineData("TmMobileActionBar.razor", "Actions")]
    [InlineData("TmToolbar.razor", "Toolbar")]
    public void EveryOverflowMenuHost_TintsADangerItem(string razor, string folder)
    {
        // H9: TmActionItem.Danger adds "<ItemClass>--danger"; each host must style it or the flag is silent.
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Tempo.Blazor", "Components", folder, razor));
        var match = System.Text.RegularExpressions.Regex.Match(source, @"ActionOverflowMenu\.ItemClass\),\s*""([^""]+)""");
        match.Success.Should().BeTrue($"{razor} passes an ItemClass to ActionOverflowMenu");
        var itemClass = match.Groups[1].Value;

        var css = string.Concat(Directory.GetFiles(
            Path.Combine(RepoRoot(), "src", "Tempo.Blazor", "wwwroot", "css", "components"), "*.css").Select(File.ReadAllText));
        RuleBody(css, $".{itemClass}--danger").Should().NotBeNull($"the .{itemClass}--danger modifier needs a rule")
            .And.Contain("color: var(--tm-color-danger)");
    }
    [Fact]
    public void DividerMarkedRedundantByTheMeasurement_IsHiddenByVisibility_NeverDisplay()
    {
        // H4 (UX M1): tm-toolbar.js marks a divider that separates nothing (next sibling is another divider, or no
        // visible button on one side). visibility keeps the box, so the measured fixed width cannot oscillate.
        var css = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Tempo.Blazor", "wwwroot", "css", "components", "_toolbar.css"));

        css.Should().MatchRegex(@"\[data-tm-divider-redundant\]\s*\{[^}]*visibility:\s*hidden");
        css.Should().NotMatchRegex(@"\[data-tm-divider-redundant\][^{]*\{[^}]*display:\s*none");
    }
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("TempoBlazor.slnx");
    }
}

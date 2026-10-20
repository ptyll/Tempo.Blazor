using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Toolbar;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Toolbar;

public class TmToolbarButtonTests : LocalizationTestBase
{
    [Fact]
    public void ToolbarButton_RendersText()
    {
        var cut = Render<TmToolbarButton>(p => p.Add(c => c.Text, "Export"));

        cut.Find(".tm-toolbar-btn").TextContent.Trim().Should().Contain("Export");
    }

    [Fact]
    public void ToolbarButton_WithIcon_RendersIcon()
    {
        var cut = Render<TmToolbarButton>(p => p
            .Add(c => c.Text, "Download")
            .Add(c => c.Icon, "download"));

        cut.FindAll(".tm-icon").Should().NotBeEmpty();
    }

    [Fact]
    public void ToolbarButton_IconOnly_NoText()
    {
        var cut = Render<TmToolbarButton>(p => p
            .Add(c => c.Icon, "download")
            .Add(c => c.Tooltip, "Download"));

        cut.FindAll(".tm-toolbar-btn-text").Should().BeEmpty();
    }

    [Fact]
    public void ToolbarButton_Click_FiresCallback()
    {
        var clicked = false;
        var cut = Render<TmToolbarButton>(p => p
            .Add(c => c.Text,    "Click me")
            .Add(c => c.OnClick, () => clicked = true));

        cut.Find(".tm-toolbar-btn").Click();

        clicked.Should().BeTrue();
    }

    [Fact]
    public void ToolbarButton_Disabled_IsDisabled()
    {
        var cut = Render<TmToolbarButton>(p => p
            .Add(c => c.Text,     "Disabled")
            .Add(c => c.Disabled, true));

        cut.Find("button[disabled]").Should().NotBeNull();
    }

    [Fact]
    public void ToolbarButton_Tooltip_RenderedAsTitle()
    {
        var cut = Render<TmToolbarButton>(p => p
            .Add(c => c.Icon,    "info")
            .Add(c => c.Tooltip, "More info"));

        cut.Find(".tm-toolbar-btn").GetAttribute("title").Should().Be("More info");
    }

    // ── C2: the pressed (toggle) state ───────────────────────────────────────────────────────────

    // ── C4-a: PreserveFocus — the editor selection survives a pointer press ──────────────────────

    [Fact]
    public void PreserveFocus_PreventsPointerAndMouseDownDefaults()
    {
        // A formatting toolbar button must not steal the editor's focus/selection on a mouse press:
        // the pointerdown/mousedown defaults are prevented. bUnit exposes the directive as the
        // "blazor:on*:preventDefault" attribute in the rendered markup.
        var cut = Render<TmToolbarButton>(p => p
            .Add(c => c.Text, "Bold")
            .Add(c => c.PreserveFocus, true));

        var button = cut.Find("button");
        button.HasAttribute("blazor:onpointerdown:preventDefault").Should().BeTrue();
        button.HasAttribute("blazor:onmousedown:preventDefault").Should().BeTrue();
    }

    [Fact]
    public void PreserveFocus_False_RendersNoPreventDefaultHooks()
    {
        var cut = Render<TmToolbarButton>(p => p.Add(c => c.Text, "Bold"));

        var button = cut.Find("button");
        button.HasAttribute("blazor:onpointerdown:preventDefault").Should().BeFalse();
        button.HasAttribute("blazor:onmousedown:preventDefault").Should().BeFalse();
    }

    [Fact]
    public void Pressed_Null_RendersNoAriaPressed()
    {
        // A plain button (the default) must not carry aria-pressed at all: a screen reader would
        // announce every existing toolbar button as a toggle.
        var cut = Render<TmToolbarButton>(p => p.Add(c => c.Text, "Export"));

        cut.Find(".tm-toolbar-btn").HasAttribute("aria-pressed").Should().BeFalse();
    }

    [Fact]
    public void Pressed_TrueFalse_RendersAriaPressed()
    {
        var on = Render<TmToolbarButton>(p => p.Add(c => c.Text, "Pan").Add(c => c.Pressed, true));
        var off = Render<TmToolbarButton>(p => p.Add(c => c.Text, "Pan").Add(c => c.Pressed, false));

        on.Find(".tm-toolbar-btn").GetAttribute("aria-pressed").Should().Be("true");
        off.Find(".tm-toolbar-btn").GetAttribute("aria-pressed").Should().Be("false");
    }

    [Fact]
    public void PressedMixed_RendersMixed_OnlyWhenNotPressed()
    {
        // "mixed" models a toggle over a partly-affected selection (Bold over a partly bold run).
        // It only means something for a toggle whose state is OFF (Pressed=false); ignored otherwise.
        var mixed = Render<TmToolbarButton>(p => p.Add(c => c.Text, "Bold").Add(c => c.Pressed, false).Add(c => c.PressedMixed, true));
        var onIgnored = Render<TmToolbarButton>(p => p.Add(c => c.Text, "Bold").Add(c => c.Pressed, true).Add(c => c.PressedMixed, true));
        var nullIgnored = Render<TmToolbarButton>(p => p.Add(c => c.Text, "Bold").Add(c => c.PressedMixed, true));

        mixed.Find(".tm-toolbar-btn").GetAttribute("aria-pressed").Should().Be("mixed");
        onIgnored.Find(".tm-toolbar-btn").GetAttribute("aria-pressed").Should().Be("true");
        nullIgnored.Find(".tm-toolbar-btn").HasAttribute("aria-pressed").Should().BeFalse();
    }

    [Fact]
    public void HostSplatAriaPressed_Wins()
    {
        // A host can already splat aria-pressed through AdditionalAttributes; the splat must keep
        // winning (the attributes land after the parameter attribute in the markup).
        var cut = Render<TmToolbarButton>(p => p
            .Add(c => c.Text, "Pan")
            .Add(c => c.Pressed, false)
            .AddUnmatched("aria-pressed", "true"));

        cut.Find(".tm-toolbar-btn").GetAttribute("aria-pressed").Should().Be("true");
    }

    [Fact]
    public void PressedPaint_TrueFills_MixedIsRingOnly_HoverKeepsState_ForcedColorsMapsToSystemPalette()
    {
        // C2 review: bUnit cannot compute styles, so the paint contract is locked in the stylesheets.
        // "true" paints fill + ink + ring, "mixed" stays ring-only (a partly-on toggle must read
        // apart), :hover must not restyle a pressed/mixed control like a plain one, and under
        // forced colors the pair maps onto the system palette (SelectedItem fill; mixed keeps the
        // ring-only distinction as a Canvas background with the SelectedItem ring).
        var toolbar = ToolbarCss();
        var bar = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "Tempo.Blazor", "wwwroot", "css", "components", "_mobile-action-bar.css"));

        RuleBody(toolbar, ".tm-toolbar-btn[aria-pressed=\"true\"]").Should().NotBeNull()
            .And.Contain("background: var(--tm-color-primary-subtle)")
            .And.Contain("color: var(--tm-color-primary)")
            .And.Contain("box-shadow: inset 0 0 0 2px var(--tm-color-primary-500)");
        RuleBody(toolbar, ".tm-toolbar-btn[aria-pressed=\"mixed\"]").Should().NotBeNull()
            .And.Contain("background: transparent")
            .And.Contain("color: inherit")
            .And.Contain("box-shadow: inset 0 0 0 2px var(--tm-color-primary-500)");
        RuleBody(toolbar, ".tm-toolbar-btn[aria-pressed=\"true\"]:hover:not(:disabled)").Should()
            .NotBeNull("hover must not override the pressed paint").And.Contain("background: var(--tm-color-primary-subtle)");
        RuleBody(toolbar, ".tm-toolbar-btn[aria-pressed=\"mixed\"]:hover:not(:disabled)").Should()
            .NotBeNull("mixed keeps ring-only on hover").And.Contain("background: transparent");

        RuleBody(bar, ".tm-mobile-action-bar__bar .tm-mobile-action-bar__action[aria-pressed=\"true\"]").Should().NotBeNull()
            .And.Contain("background: var(--tm-color-primary-subtle)")
            .And.Contain("color: var(--tm-color-primary)")
            .And.Contain("box-shadow: inset 0 0 0 2px var(--tm-color-primary-500)");
        RuleBody(bar, ".tm-mobile-action-bar__bar .tm-mobile-action-bar__action[aria-pressed=\"mixed\"]").Should().NotBeNull()
            .And.Contain("background: transparent")
            .And.Contain("color: inherit")
            .And.Contain("box-shadow: inset 0 0 0 2px var(--tm-color-primary-500)");
        RuleBody(bar, ".tm-mobile-action-bar__bar .tm-mobile-action-bar__action[aria-pressed=\"true\"]:hover:not(:disabled)").Should()
            .NotBeNull("hover must not override the pressed paint").And.Contain("background: var(--tm-color-primary-subtle)");
        RuleBody(bar, ".tm-mobile-action-bar__bar .tm-mobile-action-bar__action[aria-pressed=\"mixed\"]:hover:not(:disabled)").Should()
            .NotBeNull("mixed keeps ring-only on hover").And.Contain("background: transparent");

        MediaBlock(toolbar).Should().Contain(".tm-toolbar-btn[aria-pressed=\"true\"]")
            .And.Contain("forced-color-adjust: none")
            .And.Contain("background: SelectedItem")
            .And.Contain("color: SelectedItemText")
            .And.Contain(".tm-toolbar-btn[aria-pressed=\"mixed\"]")
            .And.Contain("background: Canvas")
            .And.Contain("box-shadow: inset 0 0 0 2px SelectedItem");
        MediaBlock(bar).Should().Contain(".tm-mobile-action-bar__bar .tm-mobile-action-bar__action[aria-pressed=\"true\"]")
            .And.Contain("forced-color-adjust: none")
            .And.Contain("background: SelectedItem")
            .And.Contain("color: SelectedItemText")
            .And.Contain(".tm-mobile-action-bar__bar .tm-mobile-action-bar__action[aria-pressed=\"mixed\"]")
            .And.Contain("background: Canvas")
            .And.Contain("box-shadow: inset 0 0 0 2px SelectedItem");
    }

    private static string ToolbarCss()
        => File.ReadAllText(Path.Combine(RepoRoot(), "src", "Tempo.Blazor", "wwwroot", "css", "components", "_toolbar.css"));

    /// <summary>The body of the toolbar's forced-colors media block (empty when absent).</summary>
    private static string MediaBlock(string css)
    {
        var withoutComments = System.Text.RegularExpressions.Regex.Replace(
            css, @"/\*.*?\*/", " ", System.Text.RegularExpressions.RegexOptions.Singleline);
        return System.Text.RegularExpressions.Regex.Match(
            withoutComments, @"@media\s*\(forced-colors:\s*active\)\s*\{(.*?)\n\}",
            System.Text.RegularExpressions.RegexOptions.Singleline).Groups[1].Value;
    }

    /// <summary>The declaration block of the rule whose selector list is exactly <paramref name="selector"/> (null when absent).</summary>
    private static string? RuleBody(string css, string selector)
    {
        var withoutComments = System.Text.RegularExpressions.Regex.Replace(css, @"/\*.*?\*/", " ", System.Text.RegularExpressions.RegexOptions.Singleline);
        var match = System.Text.RegularExpressions.Regex.Match(
            withoutComments, @"(?:^|[}\s])" + System.Text.RegularExpressions.Regex.Escape(selector) + @"\s*\{([^}]*)\}");
        return match.Success ? match.Groups[1].Value : null;
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

using System.Globalization;
using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Services;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Layout;

/// <summary>
/// Plan 5d396592 phase 6: the <c>/app-shell</c> demo page actually SHOWS the frame series, so the E2E
/// lane has a real page to drive. The page embeds the live frame in a width-controlled stage (so a
/// desktop, a tablet and a phone presentation can be demonstrated without touching the viewport), and
/// it carries a section per component: the frame itself, the standalone account menu (anchored popover
/// and the forced mobile sheet) and the onboarding checklist in all three layouts.
/// <para>
/// The layout the frame resolves is a function of the STAGE width and of measurement, so these tests
/// assert the page's own controls (width presets, variant switch, work-area toggle) and the component
/// markup the page composes — the resolved desktop/tablet/phone presentation and the resize geometry
/// are pinned by <c>AppShellE2ETests</c> in a real browser.
/// </para>
/// </summary>
public class AppShellDemoPageTests : DemoPageLocalizationTestBase
{
    public AppShellDemoPageTests()
    {
        // The account menu and the frame's built-in top bar resolve the ambient theme service; without
        // it the theme entry and the switch simply do not render (the library never invents a theme).
        Services.AddSingleton<ThemeService>();
    }

    private IRenderedComponent<AppShellPage> RenderPage() => Render<AppShellPage>();

    // ── The live frame ─────────────────────────────────────────────────────────

    [Fact]
    public void Live_Frame_Renders_Inside_A_Width_Controlled_Stage()
    {
        var cut = RenderPage();

        var stage = cut.Find("[data-testid='app-shell-stage']");
        var viewport = cut.Find("[data-testid='app-shell-viewport']");
        var frame = cut.Find("[data-testid='shell-app-shell']");

        stage.Contains(viewport).Should().BeTrue("the stage is what the demo's width controls resize");
        viewport.Contains(frame).Should().BeTrue("the live frame measures the width-controlled viewport");
    }

    [Fact]
    public void Stage_Height_Is_Capped_Against_The_Viewport_By_The_Stylesheet()
    {
        var cut = RenderPage();

        cut.Find("[data-testid='app-shell-stage']").GetAttribute("style").Should().BeNull(
            "a fixed inline 60rem height is taller than any phone viewport, which puts the frame's own " +
            "footer off screen inside a page that already scrolls (nested scrolling)");

        var stage = Block(
            Read("src", "Tempo.Blazor.Demo.SharedUI", "Pages", "AppShellPage.razor.css"),
            ".app-shell-page__stage {");
        stage.Should().Contain("block-size: min(60rem, 100dvh - var(--tm-space-16))",
            "the sizing belongs to the page stylesheet, where it can cap 60rem against the viewport");
    }

    [Fact]
    public void The_Full_Bleed_Override_Does_Not_Use_Viewport_Width_Units()
    {
        // Comments are stripped first: the page explains WHY the override avoids the viewport unit.
        var razor = Regex.Replace(
            Read("src", "Tempo.Blazor.Demo.SharedUI", "Pages", "AppShellPage.razor"),
            @"/\*.*?\*/",
            string.Empty,
            RegexOptions.Singleline);

        razor.Should().NotContain("100vw",
            "100vw includes the vertical scrollbar width, so the frame page grows a ~15px horizontal " +
            "scrollbar on a desktop browser (WCAG 1.4.10 reflow)");
        razor.Should().Contain("width: auto !important");
    }

    [Fact]
    public void The_Viewport_Gives_The_Frame_A_Distinct_Light_Workspace_Tone()
    {
        // TG-008 UX review: with the default light tokens --tm-bg-workspace equals --tm-bg-surface
        // (both white), so Flat and Workspace would render pixel-identical and the redesign look
        // could not be reviewed. The demo stage therefore overrides the token LIGHT-ONLY; the dark
        // roots re-declare --tm-bg-workspace closer to the frame, and the demo re-points its
        // override at the dark page tone so a scoped dark region keeps the dark workspace. The hook
        // lives in the page's inline <style>: the strict token audit forbids a [data-theme="dark"]
        // block in a .css file (the WireframeEditorPage demo hook sets the precedent).
        var razor = System.Text.RegularExpressions.Regex.Replace(
            Read("src", "Tempo.Blazor.Demo.SharedUI", "Pages", "AppShellPage.razor"),
            @"/\*.*?\*/",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);

        razor.Should().Contain(".app-shell-page__viewport { --tm-bg-workspace: var(--tm-color-gray-100); }",
            "the light demo stage gives the frame a workspace tone distinct from the surface");

        var lightAt = razor.IndexOf("--tm-color-gray-100", StringComparison.Ordinal);
        var darkAt = razor.IndexOf("[data-theme=\"dark\"] .app-shell-page__viewport", StringComparison.Ordinal);
        darkAt.Should().BeGreaterThan(lightAt,
            "the dark re-point must come after the light override (source order breaks the tie)");
        razor[darkAt..].Should().Contain("--tm-bg-workspace: var(--tm-bg-page)",
            "a scoped dark region must not inherit the light gray-100 workspace");
    }

    [Fact]
    public void Links_Stay_Relative_So_The_Demo_Base_Href_Applies()
    {
        var cut = RenderPage();

        cut.Find(".app-shell-page__back-link").GetAttribute("href").Should().Be("layout",
            "a root-absolute href ignores <base href> and breaks when the demo is hosted under a sub-path");

        var hrefs = cut.FindAll("[data-testid='shell-sidebar'] a[href]")
            .Select(anchor => anchor.GetAttribute("href"))
            .ToList();
        hrefs.Should().NotBeEmpty();
        hrefs.Should().OnlyContain(href => href == "app-shell#app-shell-live",
            "the navigation entries anchor this page relatively, so they follow <base href> instead of " +
            "resolving against the application root");
    }

    [Fact]
    public void Width_Presets_Resize_The_Viewport_And_Report_Their_State()
    {
        var cut = RenderPage();

        cut.Find("[data-testid='app-shell-viewport']").GetAttribute("style").Should().Contain("100%",
            "the default presentation fits the demo column");
        cut.Find("[data-testid='app-shell-width-fit']").GetAttribute("aria-pressed").Should().Be("true");

        cut.Find("[data-testid='app-shell-width-phone']").Click();
        cut.Find("[data-testid='app-shell-viewport']").GetAttribute("style").Should().Contain("375px");
        cut.Find("[data-testid='app-shell-width-phone']").GetAttribute("aria-pressed").Should().Be("true");
        cut.Find("[data-testid='app-shell-width-fit']").GetAttribute("aria-pressed").Should().Be("false");

        cut.Find("[data-testid='app-shell-width-tablet']").Click();
        cut.Find("[data-testid='app-shell-viewport']").GetAttribute("style").Should().Contain("900px");

        cut.Find("[data-testid='app-shell-width-desktop']").Click();
        cut.Find("[data-testid='app-shell-viewport']").GetAttribute("style").Should().Contain("1280px");

        cut.Find("[data-testid='app-shell-width-fit']").Click();
        cut.Find("[data-testid='app-shell-viewport']").GetAttribute("style").Should().Contain("100%");
    }

    [Fact]
    public void Variant_Switch_Flips_The_Frame_Treatment()
    {
        var cut = RenderPage();

        cut.Find("[data-testid='shell-app-shell']").GetAttribute("data-variant").Should().Be("inset");
        cut.Find("[data-testid='app-shell-variant-inset']").GetAttribute("aria-pressed").Should().Be("true");

        cut.Find("[data-testid='app-shell-variant-flat']").Click();

        cut.Find("[data-testid='shell-app-shell']").GetAttribute("data-variant").Should().Be("flat");
        cut.Find("[data-testid='app-shell-variant-flat']").GetAttribute("aria-pressed").Should().Be("true");
        cut.Find("[data-testid='app-shell-variant-inset']").GetAttribute("aria-pressed").Should().Be("false");

        // TG-008: the third variant drops the content panel — the frame reports it and the pressed
        // state walks on to the workspace button.
        cut.Find("[data-testid='app-shell-variant-workspace']").Click();

        cut.Find("[data-testid='shell-app-shell']").GetAttribute("data-variant").Should().Be("workspace");
        cut.Find("[data-testid='app-shell-variant-workspace']").GetAttribute("aria-pressed").Should().Be("true");
        cut.Find("[data-testid='app-shell-variant-flat']").GetAttribute("aria-pressed").Should().Be("false");
    }

    [Fact]
    public void Built_In_Navigation_Is_Sectioned_And_Named()
    {
        var cut = RenderPage();

        var rail = cut.Find("[data-testid='shell-sidebar']");
        var headings = rail.QuerySelectorAll(".tm-sidebar-section-title")
            .Select(h => h.TextContent.Trim())
            .ToArray();

        headings.Should().Equal(new[] { "Workspace", "Reports" });
        rail.QuerySelector("nav")!.GetAttribute("aria-label").Should().Be("Northwind navigation");
    }

    [Fact]
    public void Work_Area_Is_Closed_Until_The_Sources_Toggle_Opens_It()
    {
        var cut = RenderPage();

        cut.FindAll("[data-testid='app-shell-sources']").Should().BeEmpty(
            "a work area that opens itself covers the phone content it is meant to sit next to");
        var toggle = cut.Find("[data-testid='app-shell-work-area-toggle']");
        toggle.GetAttribute("aria-pressed").Should().Be("false");

        toggle.Click();

        cut.FindAll("[data-testid='app-shell-sources']").Should().ContainSingle(
            "the same Sources content is the docked column on desktop and the sheet on a phone");
        cut.Find("[data-testid='app-shell-work-area-toggle']").GetAttribute("aria-pressed").Should().Be("true");
    }

    [Fact]
    public void Live_Frame_Content_Carries_The_Onboarding_Checklist()
    {
        var cut = RenderPage();

        var frame = cut.Find("[data-testid='shell-app-shell']");
        frame.QuerySelector("[data-testid='live-onboarding-checklist']").Should().NotBeNull(
            "the frame's content demonstrates the checklist in place");
    }

    // ── Standalone sections ────────────────────────────────────────────────────

    [Fact]
    public void Standalone_User_Menu_Shows_The_Profile_Progress_And_The_Theme_Entry()
    {
        var cut = RenderPage();

        cut.Find("[data-testid='standalone-user-menu-trigger']").Click();

        var menu = cut.Find("[data-testid='standalone-user-menu'] [role='menu']");
        menu.TextContent.Should().Contain("Profile 60% complete");
        menu.TextContent.Should().Contain("Profile");
        menu.QuerySelectorAll("[role='menuitemcheckbox']").Should().HaveCount(1,
            "the theme switch is a menu item checkbox, so it stays reachable in menu interaction mode");
    }

    [Fact]
    public void Standalone_User_Menu_Demonstrates_The_Forced_Mobile_Sheet()
    {
        var cut = RenderPage();

        cut.Find("[data-testid='standalone-sheet-user-menu-trigger']").Click();

        cut.FindAll("[data-testid='standalone-sheet-user-menu'] .tm-overlay-panel-sheet").Should().ContainSingle(
            "the second instance forces the mobile bottom-sheet presentation at any viewport");
        cut.Find("[data-testid='standalone-sheet-user-menu'] [role='menu']").Should().NotBeNull();
    }

    [Fact]
    public void Checklist_Section_Renders_Every_Layout()
    {
        var cut = RenderPage();

        cut.Find("[data-testid='checklist-list-onboarding-checklist']").Should().NotBeNull();
        cut.Find("[data-testid='checklist-card-onboarding-checklist']").Should().NotBeNull();
        cut.Find("[data-testid='checklist-grid-onboarding-checklist']").Should().NotBeNull();
        cut.Find("[data-testid='checklist-dismissible-onboarding-checklist']").Should().NotBeNull();
    }

    [Fact]
    public void Checklist_Section_Reports_A_Toggled_Step_Back_To_The_Page()
    {
        var cut = RenderPage();
        var localizer = Services.GetRequiredService<ITmLocalizer>();

        var checkbox = cut.Find("[data-testid='checklist-list-onboarding-checklist'] input[type='checkbox']");
        checkbox.Change(true);

        cut.Find("[data-testid='app-shell-checklist-last']").TextContent.Should()
            .Contain(localizer["TmAppShellDemo_TaskConnect"],
                "the readout shows the step's localized title, which the test resolves through the " +
                "localizer instead of pinning an English literal");
    }

    [Fact]
    public void Checklist_Step_And_Group_Wording_Comes_From_The_Localizer()
    {
        // Every string this localizer returns carries a marker, so a literal that never reached the
        // localizer stands out: in cs/sk/fr such a literal is a language switch in the middle of the
        // guide. The five checklists on the page must therefore show marked titles, descriptions and
        // group names only.
        Services.AddSingleton<ITmLocalizer>(
            new MarkingLocalizer(new DemoTableEnglishLocalizer(BuildEnglishLocalizer())));

        var cut = RenderPage();

        var rendered = cut.FindAll(
            ".tm-onboarding-checklist__task-title, .tm-onboarding-checklist__description, " +
            ".tm-onboarding-checklist__group-title");
        rendered.Should().NotBeEmpty();

        var literals = rendered
            .Select(element => element.TextContent.Trim())
            .Where(text => !text.EndsWith(MarkingLocalizer.Mark, StringComparison.Ordinal))
            .ToList();
        literals.Should().BeEmpty(
            "no checklist wording may bypass ITmLocalizer — these rendered as raw literals:\n{0}",
            string.Join(" | ", literals));
    }

    [Fact]
    public void Account_Action_Readout_Shows_The_Localized_Item_Label()
    {
        var cut = RenderPage();
        var localizer = Services.GetRequiredService<ITmLocalizer>();

        cut.Find("[data-testid='standalone-user-menu-trigger']").Click();
        cut.Find("[data-testid='standalone-user-menu-item-orders']").Click();

        var readout = cut.Find("[data-testid='app-shell-last-action']").TextContent;
        readout.Should().Contain(localizer["TmAppShellDemo_NavOrders"],
            "the sentence reads 'Last account action: <label>', so it needs the localized label");
        readout.Should().NotContain("orders",
            "the raw item id is not user-facing wording (screen readers would read a slug)");
    }

    // ── Documentation and changelog ────────────────────────────────────────────

    private static string Read(params string[] path)
        => File.ReadAllText(Path.Combine(new[] { ThemeCss.RepositoryRoot().FullName }.Concat(path).ToArray()));

    [Fact]
    public void AppShellDoc_DocumentsTheDemoPageAndItsSections()
    {
        var doc = Read("docs", "app-shell.md");

        doc.Should().Contain("/app-shell");
        foreach (var term in new[] { "app-shell-stage", "width", "Inset", "Flat", "Sources", "TmUserMenu", "TmOnboardingChecklist" })
        {
            doc.Should().Contain(term, $"docs/app-shell.md must document the demo page section ({term})");
        }
    }

    [Fact]
    public void Changelog_RecordsTheDemoDocumentationAndE2EPhase()
    {
        var changelog = Read("CHANGELOG.md");
        var unreleased = changelog[..changelog.IndexOf("## 2.9.1 ", StringComparison.Ordinal)];

        foreach (var term in new[]
        {
            "/app-shell", "AppShellE2ETests", "docs/app-shell.md", "MainLayout", "No breaking change",
        })
        {
            unreleased.Should().Contain(term, $"CHANGELOG must record the phase-6 item ({term})");
        }
    }

    /// <summary>
    /// Wraps whichever localizer the demo page would get and appends a marker to EVERY value. A string
    /// that renders without the marker never went through <see cref="ITmLocalizer"/>, i.e. it is a
    /// hardcoded literal that stays English in cs/sk/fr.
    /// </summary>
    private sealed class MarkingLocalizer(ITmLocalizer inner) : ITmLocalizer
    {
        public const string Mark = "\u00abloc\u00bb";

        public CultureInfo Culture => inner.Culture;

        public string this[string key] => inner[key] + Mark;

        public string this[string key, params object[] arguments] => inner[key, arguments] + Mark;

        public bool TryGetString(string key, out string value) => inner.TryGetString(key, out value);
    }

    /// <summary>The declaration block of the first rule whose selector line starts with <paramref name="selector"/>.</summary>
    private static string Block(string css, string selector)
    {
        var start = css.IndexOf(selector, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"{selector} must exist");
        var open = css.IndexOf('{', start);
        var close = css.IndexOf('}', open);
        return css[start..(close + 1)];
    }
}

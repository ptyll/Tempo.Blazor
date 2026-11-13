using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// TG-007 (plan 92e96b25, phase 9): interface density. A host opts the whole subtree into the
/// compact density with <c>data-tm-density="compact"</c> on any ancestor; comfortable (the default,
/// no attribute) must render byte-identical metrics to the pre-TG-007 look. The contract lives in
/// <c>density.css</c> — a separate file so the flat "last declaration wins" token readers keep
/// resolving the comfortable <c>:root</c> value from tokens.css; import order (density.css after
/// tokens.css) is what lets the equal-specificity compact scope win. The compact scope re-declares
/// the density tokens — the control height (<c>--tm-input-height-md</c>), the data-table cell
/// padding (<c>--tm-data-table-cell-py/px</c>), the card paddings and the sidebar item padding —
/// and the component stylesheets read those tokens. The per-component <c>--compact</c> modifiers
/// that predate this phase stay untouched as local overrides.
/// </summary>
public class InterfaceDensityTests
{
    private const string CompactSelector = "[data-tm-density=\"compact\"]";

    private static readonly Regex RuleBlock =
        new(@"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}", RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    private static string TokensCss => File.ReadAllText(ThemeCss.CssPath("tokens.css"));

    private static string DensityCss => File.ReadAllText(ThemeCss.CssPath("density.css"));

    /// <summary>Every <c>:root</c> rule body merged (tokens.css carries several — the main block, the media-query one).</summary>
    private static string RootDeclarations()
    {
        var merged = new System.Text.StringBuilder();
        foreach (Match rule in RuleBlock.Matches(ThemeCss.StripComments(TokensCss)))
        {
            if (ThemeCss.SelectorParts(rule.Groups["selector"].Value).Contains(":root", StringComparer.Ordinal))
            {
                merged.Append(rule.Groups["body"].Value).Append(' ');
            }
        }

        return ThemeCss.Normalise(merged.ToString());
    }

    /// <summary>The <c>:root</c> rule of density.css — the comfortable density defaults.</summary>
    private static string DensityRootDeclarations() => ThemeCss.RuleDeclarations(DensityCss, ":root");

    /// <summary>The declaration body of the compact density block in density.css ("" when missing).</summary>
    private static string CompactBlock() => ThemeCss.RuleDeclarations(DensityCss, CompactSelector);

    // ── Comfortable = today's look ────────────────────────────────────

    [Fact]
    public void RootDeclaresDensityScale_OneByDefault()
    {
        var root = DensityRootDeclarations();

        ThemeCss.Declares(root, "--tm-density-scale").Should().BeTrue(
            "the density scale is the documented host knob — it must be declared on :root");

        root.Should().Contain("--tm-density-scale: 1",
            "comfortable (the default) is exactly 1 — today's metrics, nothing rescaled");
    }

    [Fact]
    public void ComfortableDefaults_MatchPreTg007Metrics()
    {
        var densityRoot = DensityRootDeclarations();
        var tokensRoot = RootDeclarations();

        // Control height and table cells stay declared in tokens.css — the compact scope in
        // density.css re-declares them; comfortable keeps the pre-TG-007 :root values there.
        tokensRoot.Should().Contain("--tm-input-height-md: 2.375rem",
            "comfortable keeps the 38 px control height");
        tokensRoot.Should().Contain("--tm-data-table-cell-py: var(--tm-space-3)",
            "comfortable keeps the 12 px vertical cell padding");
        tokensRoot.Should().Contain("--tm-data-table-cell-px: var(--tm-space-4)",
            "comfortable keeps the 16 px horizontal cell padding");

        // The new density-aware tokens default on density.css :root to the exact values the
        // components used before TG-007.
        densityRoot.Should().Contain("--tm-card-header-padding: var(--tm-space-4) var(--tm-space-5)",
            "comfortable card header padding is the pre-TG-007 space-4/space-5 pair");
        densityRoot.Should().Contain("--tm-card-content-padding: var(--tm-space-5)",
            "comfortable card content padding is the pre-TG-007 space-5");
        densityRoot.Should().Contain("--tm-card-footer-padding: var(--tm-space-3) var(--tm-space-5)",
            "comfortable card footer padding is the pre-TG-007 space-3/space-5 pair");
        densityRoot.Should().Contain("--tm-sidebar-item-padding: var(--tm-space-2) var(--tm-space-3)",
            "comfortable sidebar item padding is the pre-TG-007 space-2/space-3 pair");
    }

    // ── Compact scope ─────────────────────────────────────────────────

    [Fact]
    public void CompactScope_RedeclaresControlHeightsAndTableCells()
    {
        var compact = CompactBlock();

        compact.Should().NotBeEmpty(
            "tokens.css must carry the [data-tm-density=\"compact\"] scope — the attribute is the contract");

        compact.Should().Contain("--tm-density-scale: 0.85",
            "compact is a 0.85× rescale, documented for hosts that derive their own metrics from the scale");
        compact.Should().Contain("--tm-input-height-md: 2rem",
            "compact shrinks the medium control height from 2.375rem (38 px) to 2rem (32 px)");
        compact.Should().Contain("--tm-data-table-cell-py: var(--tm-space-2)",
            "compact shrinks the vertical cell padding from space-3 (12 px) to space-2 (8 px)");
        compact.Should().Contain("--tm-data-table-cell-px: var(--tm-space-3)",
            "compact shrinks the horizontal cell padding from space-4 (16 px) to space-3 (12 px)");
    }

    [Fact]
    public void CompactScope_RedeclaresCardAndSidebarPadding()
    {
        var compact = CompactBlock();

        compact.Should().Contain("--tm-card-header-padding: var(--tm-space-3) var(--tm-space-4)",
            "compact card header padding is one step tighter than comfortable");
        compact.Should().Contain("--tm-card-content-padding: var(--tm-space-4)",
            "compact card content padding is one step tighter than comfortable");
        compact.Should().Contain("--tm-card-footer-padding: var(--tm-space-2) var(--tm-space-4)",
            "compact card footer padding is one step tighter than comfortable");
        compact.Should().Contain("--tm-sidebar-item-padding: var(--tm-space-1-5) var(--tm-space-3)",
            "compact sidebar items keep the inline padding and tighten the block padding");
    }

    [Fact]
    public void CompactScope_ComesAfterTheDensityRootBlock()
    {
        // Within density.css, :root and [data-tm-density="compact"] carry the same (0,1,0)
        // specificity, so ORDER is what lets the attribute scope win. The density :root block
        // precedes the compact block in the file. Comments are stripped first — both names are
        // mentioned in the header comment.
        var text = ThemeCss.StripComments(DensityCss);
        var compactAt = text.IndexOf(CompactSelector, StringComparison.Ordinal);
        compactAt.Should().BeGreaterThan(0, "the compact scope must exist");

        text.IndexOf("--tm-density-scale", StringComparison.Ordinal).Should().BeLessThan(compactAt,
            "the comfortable :root defaults must precede the compact overrides");
    }

    [Fact]
    public void DensityCss_IsImportedAfterTokensCss_TheOrderIsLoadBearing()
    {
        // The compact scope re-declares :root tokens at EQUAL specificity: across files, the
        // import order in tempo-blazor.css is the cascade order. If density.css ever moved
        // before tokens.css, compact would silently lose to :root everywhere.
        var manifest = File.ReadAllText(ThemeCss.CssPath("tempo-blazor.css"));
        var tokensAt = manifest.IndexOf("@import \"tokens.css\"", StringComparison.Ordinal);
        var densityAt = manifest.IndexOf("@import \"density.css\"", StringComparison.Ordinal);

        tokensAt.Should().BeGreaterThanOrEqualTo(0, "the manifest must import tokens.css");
        densityAt.Should().BeGreaterThan(tokensAt,
            "density.css must follow tokens.css — equal-specificity compact overrides win by import order");
    }

    [Fact]
    public void RootTokensDerivedFromInputHeight_AreAllowListed()
    {
        // A :root token computed from --tm-input-height-md keeps its comfortable value under
        // compact (custom properties are evaluated where they are declared, with the :root
        // value — see docs/density.md "Root-level derived reserves"). Every such derivation
        // must be a deliberate, documented allow-list entry: a new one would silently stay
        // comfortable under compact, which is exactly the drift this guard exists to catch.
        var allowList = new[]
        {
            // Declared on :root twice (the base block and the <768px media-query block) —
            // both derivations reserve the comfortable height under compact by design.
            "--tm-form-action-bar-reserve-block-size",
        };

        var root = RootDeclarations();
        var derived = Regex.Matches(root, @"(--tm-[\w-]+)\s*:\s*[^;]*var\(--tm-input-height-md\)")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        derived.Should().NotBeEmpty(
            "tokens.css derives at least one :root reserve from --tm-input-height-md — "
            + "if this fails the derivation moved or the regex no longer matches the calc() form");
        derived.Should().BeEquivalentTo(allowList,
            "every :root token derived from --tm-input-height-md must be an allow-listed, "
            + "documented decision (docs/density.md) — a new derivation stays comfortable under "
            + "compact and would otherwise ship unnoticed");
    }

    // ── Components consume the tokens ─────────────────────────────────

    [Fact]
    public void BaseControls_KeepReadingTheInputHeightToken()
    {
        // The compact scope reaches the base controls THROUGH the token they already read — the
        // stylesheets themselves do not gain a density rule.
        ThemeCss.Property("_button.css", ".tm-btn", "height")
            .Should().Be("var(--tm-input-height-md)", "TmButton reads the density-aware control height");
        ThemeCss.Property("_input.css", ".tm-input", "height")
            .Should().Be("var(--tm-input-height-md)", "TmTextInput reads the density-aware control height");
        ThemeCss.Property("_input.css", ".tm-select", "height")
            .Should().Be("var(--tm-input-height-md)", "TmSelect reads the density-aware control height");
        ThemeCss.Property("_search-input.css", ".tm-search-input", "height")
            .Should().Be("var(--tm-input-height-md)", "TmSearchInput reads the density-aware control height");
    }

    [Fact]
    public void DataTable_KeepsReadingTheCellTokens()
    {
        var body = ThemeCss.Rules("_data-table.css")
            .First(rule => ThemeCss.SelectorParts(rule.Selector).Contains(".tm-data-table td"))
            .Body;

        ThemeCss.Normalise(body).Should().Contain(
            "padding: var(--tm-data-table-cell-py) var(--tm-data-table-cell-px)",
            "the table cell padding flows from the tokens the compact scope re-declares");
    }

    [Fact]
    public void CardCss_ConsumesTheDensityTokens()
    {
        ThemeCss.Property("_card.css", ".tm-card-header", "padding")
            .Should().Be("var(--tm-card-header-padding)");
        ThemeCss.Property("_card.css", ".tm-card-content", "padding")
            .Should().Be("var(--tm-card-content-padding)");
        ThemeCss.Property("_card.css", ".tm-card-footer", "padding")
            .Should().Be("var(--tm-card-footer-padding)");
    }

    [Fact]
    public void SidebarCss_ConsumesTheDensityToken()
    {
        ThemeCss.Property("_sidebar-nav.css", ".tm-sidebar-nav-link", "padding")
            .Should().Be("var(--tm-sidebar-item-padding)",
                "every TmSidebar nav item reads the density-aware padding");
    }

    [Fact]
    public void TabsCss_CompactScope_ReMetricsEveryVariant()
    {
        // The tab row metrics live on .tm-tabs as custom properties, so the compact scope must
        // re-declare them ON .tm-tabs (an inherited :root value would lose to the element's own
        // declaration). The variant chrome (tray, borders, radii) is untouched — only the row
        // padding shrinks, for every variant, like the input-height token does for controls.
        var rule = ThemeCss.Rules("_tabs.css")
            .FirstOrDefault(r => ThemeCss.SelectorParts(r.Selector).Contains($"{CompactSelector} .tm-tabs"));

        rule.Selector.Should().NotBeNullOrEmpty(
            "_tabs.css must re-declare the tab row metrics under the compact scope");

        var body = ThemeCss.Normalise(rule.Body);
        body.Should().Contain("--tm-tab-padding-y: 0.375rem",
            "compact line-tab row: 2×6 px + 20 px line + 2 px baseline = 34 px (comfortable 42 px)");
        body.Should().Contain("--tm-tab-padding-x: 0.875rem");
    }

    // ── The pre-existing per-component compact modifiers stay ──────────

    [Theory]
    [InlineData("_usage-meter.css", ".tm-usage-meter--compact")]
    [InlineData("_approval-card.css", ".tm-approval-card--compact")]
    [InlineData("_calendar-view.css", ".tm-calendar-view--compact")]
    public void PreExistingCompactModifiers_ArePreservedAsLocalOverrides(string stylesheet, string modifier)
    {
        ThemeCss.ComponentCss(stylesheet).Should().Contain(modifier,
            "the global density attribute is additive — the per-component --compact modifiers that "
            + "predate TG-007 stay as local overrides and must not be removed");
    }

    // ── Documentation ratchet ─────────────────────────────────────────

    [Theory]
    [InlineData("--tm-density-scale")]
    [InlineData("--tm-card-header-padding")]
    [InlineData("--tm-card-content-padding")]
    [InlineData("--tm-card-footer-padding")]
    [InlineData("--tm-sidebar-item-padding")]
    public void DensityTokens_AreDocumented_InGettingStarted(string token)
    {
        var path = Path.Combine(ThemeCss.RepositoryRoot().FullName, "JsonDocumentation", "gettingStarted.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var categories = document.RootElement
            .GetProperty("cssFramework").GetProperty("designTokens").GetProperty("categories");

        var found = categories.EnumerateObject().Any(category =>
            category.Value.TryGetProperty("tokens", out var tokens) && tokens.TryGetProperty(token, out _));

        found.Should().BeTrue(
            $"{token} is a published host knob — a consumer cannot map its user density setting "
            + "to a token the documentation does not mention");
    }
}

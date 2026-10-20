using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Guard for the focus-ring migration (cluster-D §1a). The soft glow <c>--tm-shadow-focus</c>
/// measures 1.62:1 light / 2.53:1 dark against the surface — under the 3:1 a focus indicator
/// needs — so no core rule may use it as a focus indicator anymore; the two-layer
/// <c>--tm-focus-ring</c> (5.17:1 / 5.75:1) replaces it 1:1. Under <c>forced-colors: active</c>
/// the UA forces <c>box-shadow: none</c>, so a focus rule that dropped its outline for the ring
/// keeps <c>outline: 2px solid transparent</c> (invisible normally, painted in the system colour
/// under forced colors — the <c>.tm-radio-segment</c> precedent in <c>_radio-group.css</c>).
/// </summary>
public sealed class FocusRingAdoptionTests
{
    private static readonly Regex GlowAsFocusIndicator =
        new(@"var\(\s*--tm-shadow-focus\s*[,)]", RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    /// <summary>
    /// The glow token itself stays defined (the CHANGELOG restore override
    /// <c>--tm-focus-ring: var(--tm-shadow-focus)</c> depends on it); what must reach zero is
    /// its use as a focus indicator in core component stylesheets. The <c>-danger</c> variant
    /// is a persistent invalid-state glow, not a focus indicator, and is deliberately not
    /// matched by the regex.
    /// </summary>
    [Fact]
    public void CoreComponentStylesheets_DoNotUseTheGlowAsAFocusIndicator()
    {
        var offenders = Directory.EnumerateFiles(ThemeCss.CssPath("components"), "*.css")
            .SelectMany(file => RuleMatches(file, Path.GetFileName(file)))
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty(
            "the soft glow measures under 3:1 against the surface in every theme, so it cannot be "
            + "a focus indicator — migrate the rule to var(--tm-focus-ring) instead: {0}",
            string.Join(", ", offenders));
    }

    /// <summary>
    /// Package stylesheets migrate in their own plans (DocumentEditor, EmailTemplates, Notion,
    /// Signing, Spreadsheet). Until then their remaining glow usages are frozen here: the set
    /// may only shrink, never grow, so a new core-adjacent usage fails while a package
    /// migration that removes one is allowed.
    /// </summary>
    [Fact]
    public void PackageStylesheets_GlowUsage_OnlyShrinks()
    {
        var root = ThemeCss.RepositoryRoot().FullName;
        var found = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.css", SearchOption.AllDirectories)
            .Where(IsPackageStylesheet)
            .SelectMany(file =>
            {
                var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                return RuleMatches(file, relative);
            })
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList();

        found.Should().BeSubsetOf(FrozenPackageUsages,
            "a NEW glow usage outside the frozen list must not appear; each frozen row is owned by "
            + "its package plan and may only be removed by it");

        // Belt and braces next to the subset assert: a duplicated usage in a package file must
        // not pass by matching the same frozen row twice.
        found.Count.Should().BeLessThanOrEqualTo(FrozenPackageUsages.Length);
    }

    /// <summary>
    /// In <c>forced-colors: active</c> the UA forces <c>box-shadow: none</c>; a focus rule whose
    /// only fallback is an <c>outline: none</c> from the base state then paints no indicator at
    /// all. Every core rule whose selector is a focus selector and whose body paints the ring
    /// must itself declare an outline that is not <c>none</c>/<c>0</c> — a MISSING declaration
    /// is an offender too, because the base rule's <c>outline: none</c> still applies.
    /// <c>2px solid transparent</c> is invisible normally and painted in the system colour under
    /// forced colors. The <c>-danger</c>/<c>-success</c> variants are persistent state glows, not
    /// focus indicators, and are deliberately not matched.
    /// </summary>
    [Fact]
    public void FocusRingRules_KeepAForcedColorsOutline()
    {
        var offenders = Directory.EnumerateFiles(ThemeCss.CssPath("components"), "*.css")
            .SelectMany(file => ThemeCss.Rules(Path.GetFileName(file))
                .Where(rule => IsFocusSelector(rule.Selector))
                .Where(rule => PaintsTheFocusRing(rule.Body))
                .Where(rule => LacksAForcedColorsOutline(rule.Body))
                .Select(rule => $"{Path.GetFileName(file)}|{rule.Selector}"))
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty(
            "under forced-colors: active box-shadow is forced to none, so a focus rule without its "
            + "own non-none outline paints no focus indicator — declare outline: 2px solid "
            + "transparent in the focus rule itself: {0}",
            string.Join(", ", offenders));
    }

    /// <summary>
    /// The 12px heatmap cell (4px gap) cannot host the 4px ring — it would paint over the
    /// neighbours. Its focus indicator is the outline alone, widened from the 1px border width
    /// to 2px, which also survives forced colors.
    /// </summary>
    [Fact]
    public void HeatmapDayFocus_IsAnOutlineOnly()
    {
        ThemeCss.TryProperty(
                "_calendar-heatmap.css", ".tm-calendar-heatmap__day:focus-visible", "box-shadow")
            .Should().BeNull("a 12px cell with a 4px gap cannot host the 4px ring — the glow is gone");

        ThemeCss.Property(
                "_calendar-heatmap.css", ".tm-calendar-heatmap__day:focus-visible", "outline")
            .Should().StartWith("2px solid",
                "the outline alone is the focus indicator and must be wide enough to read");
    }

    private static bool IsPackageStylesheet(string file)
    {
        var relative = Path.GetRelativePath(ThemeCss.RepositoryRoot().FullName, file)
            .Replace(Path.DirectorySeparatorChar, '/');
        return !relative.StartsWith("src/Tempo.Blazor/", StringComparison.Ordinal)
               && !relative.Contains("/obj/", StringComparison.Ordinal)
               && !relative.Contains("/bin/", StringComparison.Ordinal)
               && !relative.EndsWith(".bundled.css", StringComparison.Ordinal);
    }

    private static IEnumerable<string> RuleMatches(string file, string label)
    {
        var css = ThemeCss.StripComments(File.ReadAllText(file));
        foreach (Match rule in Regex.Matches(
                     css, @"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}",
                     RegexOptions.None, TimeSpan.FromSeconds(5)))
        {
            if (GlowAsFocusIndicator.IsMatch(rule.Groups["body"].Value))
            {
                var selector = ThemeCss.Normalise(rule.Groups["selector"].Value);
                yield return $"{label}|{selector}";
            }
        }
    }

    private static bool IsFocusSelector(string selector) =>
        selector.Contains(":focus", StringComparison.Ordinal)
        || selector.Contains("tm-mde-focused", StringComparison.Ordinal)
        || selector.Contains("tm-rte-focused", StringComparison.Ordinal);

    /// <summary>The exact <c>var(--tm-focus-ring)</c> token — <c>-danger</c>/<c>-success</c> are state
    /// glows, not focus indicators, and a prefix match would sweep them in.</summary>
    private static bool PaintsTheFocusRing(string body) =>
        Regex.IsMatch(body, @"var\(\s*--tm-focus-ring\s*\)", RegexOptions.None, TimeSpan.FromSeconds(5));

    /// <summary>
    /// Whether the body lacks a usable forced-colors outline: no <c>outline</c> declaration at
    /// all (the base rule's <c>outline: none</c> then wins) or one whose value is <c>none</c>/<c>0</c>.
    /// </summary>
    private static bool LacksAForcedColorsOutline(string body)
    {
        foreach (var declaration in body.Split(';'))
        {
            var separator = declaration.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0
                || !string.Equals(declaration[..separator].Trim(), "outline", StringComparison.Ordinal))
            {
                continue;
            }

            var value = declaration[(separator + 1)..].Trim();
            return value is "none" or "0";
        }

        return true;
    }

    /// <summary>
    /// Frozen glow usages in extension packages (cluster-D §1a rules 27-32, 34, 36, 38-45),
    /// each migrating in its own package plan. Kept as literal rows on purpose: a row removed
    /// by the owning plan is a passing change; a row ADDED anywhere fails the subset assert.
    /// </summary>
    private static readonly string[] FrozenPackageUsages =
    [
        "src/Tempo.Blazor.DocumentEditor/wwwroot/css/components/_document-editor-find.css|.tm-document-editor__token-insert-filter:focus-visible",
        "src/Tempo.Blazor.EmailTemplates/Components/TmEmailTemplateCanvas.razor.css|.tm-email-canvas-block.is-selected",
        "src/Tempo.Blazor.NotionEditor/Components/NotionEditor/Blocks/Embed/TmNotionEmbedBlock.razor.css|.tm-notion-embed-block__source-link:focus-visible",
        "src/Tempo.Blazor.NotionEditor/Components/NotionEditor/UI/TmNotionAiMenu.razor.css|.tm-notion-ai__textarea:focus, .tm-notion-ai__btn:focus-visible, .tm-notion-ai__tab:focus-visible, .tm-notion-ai__mode-chip:focus-visible, .tm-notion-ai__icon-btn:focus-visible",
        "src/Tempo.Blazor.NotionEditor/Components/NotionEditor/UI/TmNotionPageSearch.razor.css|.tm-nps__filter-input:focus",
        "src/Tempo.Blazor.NotionEditor/Components/NotionEditor/UI/TmNotionTemplateGallery.razor.css|.tm-ntg__search:focus-within",
        "src/Tempo.Blazor.Signing/wwwroot/css/components/_formula-builder.css|.tm-formula-builder__textarea:focus",
        "src/Tempo.Blazor.Signing/wwwroot/css/components/_pdf-template-designer.css|.tm-pdf-template-designer__palette-item--active",
        "src/Tempo.Blazor.Signing/wwwroot/css/components/_signing-field-overlay.css|.tm-signing-field--selected, .tm-signing-field-overlay--selected",
        "src/Tempo.Blazor.Spreadsheet/Components/Spreadsheet/TmSpreadsheetFormulaBar.razor.css|.tm-spreadsheet-formula-bar__input:focus",
        "src/Tempo.Blazor.Spreadsheet/Components/Spreadsheet/TmSpreadsheetFormulaBar.razor.css|.tm-spreadsheet-formula-bar__ref:focus",
        "src/Tempo.Blazor.Spreadsheet/Components/Spreadsheet/TmSpreadsheetSheetTabs.razor.css|.tm-spreadsheet-sheet-tab__input:focus",
        "src/Tempo.Blazor.Spreadsheet/wwwroot/css/components/_spreadsheet.css|.tm-dvd__select:focus, .tm-dvd__input:focus",
        "src/Tempo.Blazor.Spreadsheet/wwwroot/css/components/_spreadsheet.css|.tm-fcd__input:focus",
        "src/Tempo.Blazor.Spreadsheet/wwwroot/css/components/_spreadsheet.css|.tm-spreadsheet-dialog__input:focus",
        "src/Tempo.Blazor.Spreadsheet/wwwroot/css/components/_spreadsheet.css|.tm-spreadsheet-resize-dialog__input:focus",
    ];
}

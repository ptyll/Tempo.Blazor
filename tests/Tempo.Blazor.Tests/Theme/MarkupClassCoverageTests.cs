using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Replaces <c>OrphanClassCssContractTests</c>: instead of a hand-maintained list of eleven promised
/// classes, the population is DERIVED — every literal <c>tm-*</c> class token component markup emits
/// must have a <c>.class</c> rule in a library stylesheet, or be named in the frozen exception list.
/// <para>
/// Sources of the population:
/// <list type="bullet">
/// <item><c>src/Tempo.Blazor/**/*.razor</c> — literal tokens inside <c>class="…"</c> attributes,
///   Razor and HTML comments stripped (a commented-out class is not emitted);</item>
/// <item><c>src/Tempo.Blazor/**/*.razor.cs</c> — <c>"tm-…"</c> string literals, the way
///   <c>classes.Add("…")</c> builds the same attribute in code.</item>
/// </list>
/// A token ending in <c>-</c> is a dynamic suffix (<c>tm-form-row--cols-{n}</c>) — statically
/// unverifiable and skipped on purpose; the stem it shares with its siblings is usually covered.
/// </para>
/// <para>
/// Coverage means a <c>.name</c> selector in <c>wwwroot/css/**/*.css</c> OR in a scoped
/// <c>*.razor.css</c> — both are real styling, the second ships via <c>{assembly}.styles.css</c>.
/// A class whose rule lives in the shared <c>wwwroot/css</c> tree must additionally appear in
/// <c>tempo-blazor.bundled.css</c>, or a host reading the bundle would never see it.
/// </para>
/// <para>
/// <see cref="UnstyledMarkupClasses"/> is the 156-item inventory of what markup emits without a
/// rule on 2.8.26: semantic modifiers that style nothing by design (<c>tm-modal--center</c> is the
/// DEFAULT position — there is nothing to declare), structural hooks styled through their parent
/// (<c>tm-sidebar-nav-list</c> inside <c>.tm-sidebar-nav</c>), and dead utility references
/// (<c>tm-mb-4</c>, <c>tm-button--ghost</c>) kept on the markup for consumers. Like
/// <c>UnconstrainedClassOwnershipTests.RecordedCollisions</c>, it is an inventory, not a budget:
/// an entry whose markup is removed must be struck off, and a class markup gains without a rule
/// must be added here BEFORE it ships.
/// </para>
/// </summary>
public sealed class MarkupClassCoverageTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex ClassAttribute =
        new(@"class\s*=\s*[""']([^""']*)[""']", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex TmToken =
        new(@"tm-[a-zA-Z][\w-]*", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex QuotedLiteral =
        new(@"""([^""]*tm-[^""]*)""", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex CssClassSelector =
        new(@"\.(tm-[a-zA-Z][\w-]*)", RegexOptions.Compiled, RegexTimeout);

    /// <summary>
    /// The 156 classes markup emits with no rule anywhere in the library's stylesheets on 2.8.26,
    /// each named. None of these is a promise that they SHOULD stay unstyled — they are RECORDED so
    /// the guard can fail on a 157th.
    /// </summary>
    private static readonly string[] UnstyledMarkupClasses =
    [
        "tm-attach-delete-btn",
        "tm-attach-download-link",
        "tm-audit-log__count",
        "tm-audit-log__date-label",
        "tm-audit-log__detail-changes",
        "tm-audit-log__item",
        "tm-autosave",
        "tm-autosave__dot",
        "tm-autosave__text",
        "tm-avatar-image",
        "tm-avatar-overflow",
        "tm-btn-icon",
        "tm-button",
        "tm-button--ghost",
        "tm-button--sm",
        "tm-cal-next",
        "tm-cal-prev",
        "tm-card-header-title",
        "tm-cascading-select",
        "tm-chat__avatar--incoming",
        "tm-chat__reaction-emoji",
        "tm-chat__system-text",
        "tm-chat__thread-input",
        "tm-chip__label",
        "tm-color-gradient-hue-fill",
        "tm-color-neutral-200",
        "tm-color-neutral-500",
        "tm-color-neutral-900",
        "tm-color-primary",
        "tm-comment-cancel-btn",
        "tm-comment-delete-btn",
        "tm-comment-edit-btn",
        "tm-comment-submit-btn",
        "tm-currency-input",
        "tm-currency-input__amount",
        "tm-currency-input__converted",
        "tm-currency-input__currency",
        "tm-currency-input__manual-rate",
        "tm-currency-input__row",
        "tm-currency-input__spinner",
        "tm-dashboard-menu-item--action",
        "tm-data-table__edit-action--cancel",
        "tm-data-table__edit-action--start",
        "tm-data-table__export",
        "tm-date-today-btn",
        "tm-datetime-range-end",
        "tm-datetime-range-start",
        "tm-deadline__protocol-entry",
        "tm-decimal-input",
        "tm-dod-col-author",
        "tm-dod-mode-copy",
        "tm-dod-mode-link",
        "tm-dod-new-folder-confirm",
        "tm-dod-rename-confirm",
        "tm-dropdown-trigger",
        "tm-dropdown-wrapper",
        "tm-entity-picker__recent-item",
        "tm-export-options-section",
        "tm-fab__item-label",
        "tm-file-manager__upload",
        "tm-file-versions__action--compare",
        "tm-file-versions__header",
        "tm-filter-chip-label",
        "tm-filter-chip-value",
        "tm-form-group",
        "tm-gantt-portfolio__project-name",
        "tm-gantt-task-color",
        "tm-gantt-task-panel__attachment-item",
        "tm-gantt-task-panel__attachment-link",
        "tm-gantt-task-panel__attachment-list",
        "tm-gantt-task-panel__color-swatches",
        "tm-gantt-task-panel__dep-confirm-text",
        "tm-gantt-task-panel__description",
        "tm-gantt-task-panel__duration-display",
        "tm-gantt-task-panel__section",
        "tm-gantt-task-panel__section--budget",
        "tm-gantt-task-panel__section--timelog",
        "tm-gantt-task-panel__section-label",
        "tm-gantt__notification-settings",
        "tm-gantt__sidebar-drawer-title",
        "tm-icon-columns",
        "tm-import-wizard__upload-input",
        "tm-input--readonly",
        "tm-keyboard-shortcuts-category",
        "tm-kyc__review",
        "tm-ledger__td--desc",
        "tm-ledger__td--select",
        "tm-mb-4",
        "tm-modal--center",
        "tm-mt-2",
        "tm-multiselect__group",
        "tm-mvl-card-body",
        "tm-mvl-list-avatar",
        "tm-mvl-list-content",
        "tm-mvl-list-subtitle",
        "tm-mvl-list-title",
        "tm-mvl-loading",
        "tm-overlay",
        "tm-pagination-controls",
        "tm-pagination-next",
        "tm-pagination-prev",
        "tm-query-input__overlay-text",
        "tm-rating__star--empty",
        "tm-rte-btn-cancel",
        "tm-rte-btn-insert",
        "tm-rte-emoji-btn",
        "tm-rte-token-loading",
        "tm-scheduler-event",
        "tm-scheduler-event-dot",
        "tm-scheduler-event-time",
        "tm-scheduler-event-title",
        "tm-screening__resolution-note",
        "tm-screening__source",
        "tm-screening__time",
        "tm-sidebar-badge",
        "tm-sidebar-nav-children",
        "tm-sidebar-nav-label",
        "tm-sidebar-nav-link",
        "tm-sidebar-nav-link-child",
        "tm-sidebar-nav-list",
        "tm-signature-capture__preview-svg",
        "tm-signature-capture__previous-preview",
        "tm-signature-capture__remember",
        "tm-spinner--sm",
        "tm-split-button__text",
        "tm-splitter__pane--collapsible",
        "tm-stock-chart__ohlc",
        "tm-text-secondary",
        "tm-time-range-from",
        "tm-time-range-to",
        "tm-time-seg--hours",
        "tm-time-seg--minutes",
        "tm-time-seg--seconds",
        "tm-toggle-section",
        "tm-toggle-section__content",
        "tm-toggle-section__header",
        "tm-toggle-section__input",
        "tm-toggle-section__label",
        "tm-toggle-section__radio",
        "tm-toggle-section__radios",
        "tm-topbar-brand-title",
        "tm-topbar-center",
        "tm-topbar-search-hint",
        "tm-topbar-search-trigger",
        "tm-topbar-user",
        "tm-topbar-user-button",
        "tm-topbar-user-menu",
        "tm-topbar-user-menu-item",
        "tm-topbar-user-menu-logout",
        "tm-topbar-user-name",
        "tm-tree-list-row--root",
        "tm-upload-progress__action--cancel",
        "tm-upload-progress__action--resume",
        "tm-user-picker__status",
        "tm-validated-field",
        "tm-view-error",
    ];

    [Fact]
    public void EveryMarkupClass_HasARule_OrIsRecordedUnstyled()
    {
        var uncovered = MarkupClasses()
            .Where(c => !AllSourceSelectors().Contains(c))
            .Order(StringComparer.Ordinal)
            .ToList();

        uncovered.Should().BeEquivalentTo(
            UnstyledMarkupClasses,
            "každá třída, kterou markup emituje, má pravidlo ve stylesheetu, nebo je jmenovitě " +
            "zapsaná v UnstyledMarkupClasses — nová třída bez pravidla sem patří dřív, než se dostane " +
            "ke konzumentovi. Změna oproti záznamu: {0}",
            string.Join(" | ", uncovered.Except(UnstyledMarkupClasses, StringComparer.Ordinal)));
    }

    /// <summary>
    /// The reverse direction: a recorded exception whose class is no longer emitted must be struck
    /// off — a list that only ever grows stops describing the code.
    /// </summary>
    [Fact]
    public void EveryRecordedUnstyledClass_IsStillEmitted()
    {
        var emitted = MarkupClasses();

        UnstyledMarkupClasses
            .Where(recorded => !emitted.Contains(recorded))
            .Should().BeEmpty(
                "třídu, kterou markup přestal emitovat, ze seznamu škrtni — jinak seznam přestane " +
                "popisovat kód a začne popisovat historii");
    }

    /// <summary>
    /// A class styled by the shared tree must be styled for a bundle host too: the bundle is a
    /// build artifact of the same sources, and a class missing from it is missing from the page.
    /// Scoped razor.css classes are exempt by design — they ship via {assembly}.styles.css.
    /// </summary>
    [Fact]
    public void SharedCssClasses_AreInTheShippedBundle()
    {
        var bundleSelectors = SelectorsIn(File.ReadAllText(Path.Combine(CssRoot(), "tempo-blazor.bundled.css")));
        var sharedSelectors = SharedSourceSelectors();

        var markup = MarkupClasses();
        var missing = markup
            .Where(c => sharedSelectors.Contains(c) && !bundleSelectors.Contains(c))
            .Order(StringComparer.Ordinal)
            .ToList();

        missing.Should().BeEmpty(
            "třída má pravidlo ve zdrojovém CSS, ale bundl ho nenese — host, který čte bundled.css, " +
            "ji nikdy nevidí: {0}",
            string.Join(" | ", missing));
    }

    /// <summary>
    /// The population denominator, asserted: 195 razor + 51 razor.cs files yielding 2 145 literal
    /// classes on 2.8.26. A sweep that silently reads half the tree reports "nothing missing" with
    /// perfect confidence — the count is how you know it looked.
    /// </summary>
    [Fact]
    public void TheSweepReadsTheWholeMarkupPopulation()
    {
        RazorFiles().Should().HaveCountGreaterThanOrEqualTo(
            190,
            "src/Tempo.Blazor má ~195 .razor souborů; menší číslo znamená, že sonda čte jinou složku");
        MarkupClasses().Should().HaveCountGreaterThanOrEqualTo(
            2000,
            "markup emituje ~2 145 literálových tm-* tříd; desetina toho znamená, že se extraktor rozbil");
    }

    /// <summary>
    /// Mutation, both directions: an invented class in markup must be reported uncovered, and a
    /// covered class must not be.
    /// </summary>
    [Fact]
    public void TheExtractor_ReportsAnInventedClassAndNotACoveredOne()
    {
        var emitted = ExtractMarkupClasses(
            ["<div class=\"tm-pagination-size tm-invented-never-styled\"></div>"],
            Enumerable.Empty<string>());

        emitted.Should().Contain("tm-pagination-size").And.Contain("tm-invented-never-styled");
        AllSourceSelectors().Contains("tm-pagination-size").Should().BeTrue();
        AllSourceSelectors().Contains("tm-invented-never-styled").Should().BeFalse();
    }

    /// <summary>
    /// Comments are not markup and a dynamic suffix is not a class: a token inside a Razor or HTML
    /// comment must not be extracted, and <c>tm-form-row--cols-</c> + expression must not become a
    /// phantom class named <c>tm-form-row--cols</c>.
    /// </summary>
    [Fact]
    public void TheExtractor_IgnoresCommentsAndDynamicSuffixes()
    {
        var emitted = ExtractMarkupClasses(
            [
                """
                @* class="tm-commented-out-razor" *@
                <!-- class="tm-commented-out-html" -->
                <div class="tm-form-row tm-form-row--cols-@Cols"></div>
                """
            ],
            Enumerable.Empty<string>());

        emitted.Should().NotContain("tm-commented-out-razor").And.NotContain("tm-commented-out-html");
        emitted.Should().NotContain("tm-form-row--cols",
            "dynamická přípona není třída — tm-form-row--cols-{n} nelze staticky ověřit");
        emitted.Should().Contain("tm-form-row");
    }

    // ── Detail guards carried over from OrphanClassCssContractTests ──
    // The sweep proves a rule EXISTS; these prove what the 2.8.17 orphan fixes put INSIDE it.

    [Fact]
    public void PaginationSize_LaysOutTheLabelAndTheSelect()
    {
        SelectorBlock(SourceFile("components/_data-table.css"), ".tm-pagination-size")
            .Should().Contain("display: flex")
            .And.Contain("align-items:")
            .And.Contain("gap:");
    }

    [Fact]
    public void PaginationDisabled_PaintsAndBlocksThePager()
    {
        var block = SelectorBlock(SourceFile("components/_data-table.css"), ".tm-pagination-disabled");

        block.Should().Contain("opacity:");
        block.Should().Contain("pointer-events: none");
    }

    [Fact]
    public void AvatarFallback_FillsTheAvatar()
    {
        var block = SelectorBlock(SourceFile("components/_avatar.css"), ".tm-avatar-fallback");

        block.Should().Contain("width:");
        block.Should().Contain("height:");
        block.Should().Contain("display:");
    }

    [Fact]
    public void AvatarColorModifiers_SetBackgroundAndInkThroughTokens()
    {
        var css = SourceFile("components/_avatar.css");

        foreach (var color in new[] { "gray", "red", "orange", "green", "blue", "purple", "pink" })
        {
            var block = SelectorBlock(css, $".tm-avatar-{color}");
            block.Should().Contain("background-color:");
            block.Should().Contain("color:");
            block.Should().Contain("var(--tm-", "barva avatara musí jít z tokenu, ne z literálu, který se s motivem nepřeklopí");
        }
    }

    [Fact]
    public void Avatar2xl_MatchesTheXxlBoxTheStylesheetAlreadyHad()
    {
        var css = SourceFile("components/_avatar.css");
        var xxl = SelectorBlock(css, ".tm-avatar-xxl");
        var twoXl = SelectorBlock(css, ".tm-avatar-2xl");

        WidthHeight(twoXl).Should().Be(
            WidthHeight(xxl),
            "komponenta emituje tm-avatar-2xl; xxl už rozměr má — alias, ne druhé číslo");
    }

    [Fact]
    public void InputSearch_OwnsItsChrome()
    {
        var block = SelectorBlock(SourceFile("components/_input.css"), ".tm-input-search");

        block.Should().MatchRegex(@"appearance:\s*none");
    }

    [Fact]
    public void OutlineSecondary_UsesTheControlBorderToken()
    {
        var block = SelectorBlock(SourceFile("components/_button.css"), ".tm-btn-outline-secondary");

        block.Should().Contain("border-color: var(--tm-border-color-control");
        block.Should().NotContain("border-color: var(--tm-border-color);");
    }

    // ── Extraction ────────────────────────────────────────────────

    private static HashSet<string> MarkupClasses() =>
        ExtractMarkupClasses(RazorMarkup(), CodeBehindFiles());

    private static IEnumerable<string> RazorMarkup() =>
        RazorFiles().Select(File.ReadAllText);

    private static IEnumerable<string> CodeBehindFiles() =>
        Directory.EnumerateFiles(ComponentRoot(), "*.razor.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText);

    private static List<string> RazorFiles() =>
        Directory.EnumerateFiles(ComponentRoot(), "*.razor", SearchOption.AllDirectories).ToList();

    private static HashSet<string> ExtractMarkupClasses(
        IEnumerable<string> razorDocuments,
        IEnumerable<string> codeBehindDocuments)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var document in razorDocuments)
        {
            var cleaned = StripComments(document);
            foreach (Match attribute in ClassAttribute.Matches(cleaned))
            {
                AddTokens(attribute.Groups[1].Value, names);
            }
        }

        foreach (var document in codeBehindDocuments)
        {
            foreach (Match literal in QuotedLiteral.Matches(document))
            {
                AddTokens(literal.Groups[1].Value, names);
            }
        }

        return names;
    }

    /// <summary>Records complete tm-* tokens; a token ending in '-' is a dynamic suffix, not a class.</summary>
    private static void AddTokens(string text, HashSet<string> names)
    {
        foreach (Match token in TmToken.Matches(text))
        {
            var value = token.Value;
            if (!value.EndsWith('-'))
            {
                names.Add(value);
            }
        }
    }

    private static string StripComments(string markup) =>
        Regex.Replace(
            Regex.Replace(markup, @"@\*.*?\*@", string.Empty, RegexOptions.Singleline, RegexTimeout),
            @"<!--.*?-->",
            string.Empty,
            RegexOptions.Singleline,
            RegexTimeout);

    // ── CSS selector inventories ──────────────────────────────────

    /// <summary>Selectors in the shared tree + every scoped razor.css — all real styling.</summary>
    private static HashSet<string> AllSourceSelectors()
    {
        var selectors = SharedSourceSelectors();
        foreach (var file in Directory.EnumerateFiles(ComponentRoot(), "*.razor.css", SearchOption.AllDirectories))
        {
            selectors.UnionWith(SelectorsIn(File.ReadAllText(file)));
        }

        return selectors;
    }

    /// <summary>Selectors in the shipped shared tree only — what the bundle is built from.</summary>
    private static HashSet<string> SharedSourceSelectors()
    {
        var selectors = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(CssRoot(), "*.css", SearchOption.AllDirectories))
        {
            selectors.UnionWith(SelectorsIn(File.ReadAllText(file)));
        }

        return selectors;
    }

    private static HashSet<string> SelectorsIn(string css)
    {
        var withoutComments = Regex.Replace(
            css, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline, RegexTimeout);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in CssClassSelector.Matches(withoutComments))
        {
            names.Add(match.Groups[1].Value);
        }

        return names;
    }

    // ── Rule-block helpers (detail tests) ─────────────────────────

    private static (string Width, string Height) WidthHeight(string block)
    {
        var width = Regex.Match(block, @"width:\s*([^;]+);", RegexOptions.None, RegexTimeout);
        var height = Regex.Match(block, @"height:\s*([^;]+);", RegexOptions.None, RegexTimeout);
        width.Success.Should().BeTrue("blok musí nastavovat width");
        height.Success.Should().BeTrue("blok musí nastavovat height");
        return (width.Groups[1].Value.Trim(), height.Groups[1].Value.Trim());
    }

    private static string SelectorBlock(string css, string selector)
    {
        var pattern = Regex.Escape(selector) + @"\s*\{";
        var start = Regex.Match(css, pattern, RegexOptions.None, RegexTimeout);
        start.Success.Should().BeTrue("CSS musí deklarovat {0}", selector);

        var from = start.Index;
        var end = css.IndexOf('}', from);
        end.Should().BeGreaterThan(from);
        return css[from..end];
    }

    // ── Roots ─────────────────────────────────────────────────────

    private static string SourceFile(string relative) =>
        File.ReadAllText(Path.Combine(CssRoot(), relative));

    private static string CssRoot() =>
        Path.Combine(FindRepositoryRoot(), "src", "Tempo.Blazor", "wwwroot", "css");

    private static string ComponentRoot() =>
        Path.Combine(FindRepositoryRoot(), "src", "Tempo.Blazor");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find TempoBlazor.slnx.");
    }
}

using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit.Abstractions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Replaces <c>OrphanClassCssContractTests</c>: instead of a hand-maintained list of eleven promised
/// classes, the population is DERIVED — every literal <c>tm-*</c> class token component markup emits
/// must have a <c>.class</c> rule in a library stylesheet, or be named in the frozen exception list.
/// <para>
/// Sources of the population — one SCOPE per shipped component package:
/// <list type="bullet">
/// <item><c>src/Tempo.Blazor/**/*.razor</c> — literal tokens inside <c>class="…"</c> attributes,
///   Razor and HTML comments stripped (a commented-out class is not emitted);</item>
/// <item><c>src/Tempo.Blazor/**/*.razor.cs</c> — <c>"tm-…"</c> string literals, the way
///   <c>classes.Add("…")</c> builds the same attribute in code;</item>
/// <item>the same two sources under <c>src/Tempo.Blazor.Signing</c> and
///   <c>src/Tempo.Blazor.NotionEditor</c> — sibling packages whose markup a host renders with the
///   core stylesheet loaded.</item>
/// </list>
/// A token ending in <c>-</c>, or one immediately followed by <c>@</c> or <c>{</c>, is a dynamic
/// suffix (<c>tm-form-row--cols-{n}</c>, <c>tm-notion-heading--h{level}</c>) — statically
/// unverifiable, so it is not coverage-checked, but it is not dropped either: each is an
/// <c>unmeasurable:interpolated-class</c> stem that is collected and COUNTED per scope
/// (<see cref="TheSweep_CountsEveryUnmeasurableInterpolatedStem"/>). A token preceded by
/// <c>-</c> is not a class at all:
/// <c>"var(--tm-color-primary)"</c> and <c>"--tm-gantt-task-color: {x}"</c> literals produced five
/// phantom "classes" in the first version of this sweep, which is why the check exists.
/// </para>
/// <para>
/// Coverage means a <c>.name</c> selector in the scope's <c>wwwroot/css/**/*.css</c>, in a scoped
/// <c>*.razor.css</c> (ships via <c>{assembly}.styles.css</c>), or — for the two satellite packages,
/// which project-reference the core — in the CORE <c>wwwroot/css</c> tree: a Signing markup class
/// like <c>tm-btn</c> is styled by the stylesheet Signing's package depends on. A class whose rule
/// lives in the core shared tree must additionally appear in <c>tempo-blazor.bundled.css</c>, or a
/// host reading the bundle would never see it.
/// </para>
/// <para>
/// <see cref="UnstyledMarkupClasses"/> (and its Signing/NotionEditor counterparts) is the inventory
/// of what markup emits without a rule on 2.8.26 — 148 in core, 52 in Signing, 44 in NotionEditor:
/// semantic modifiers that style nothing by design (<c>tm-modal--center</c> is the DEFAULT position
/// — there is nothing to declare), structural hooks styled through their parent
/// (<c>tm-sidebar-nav-list</c> inside <c>.tm-sidebar-nav</c>), and dead utility references
/// (<c>tm-mb-4</c>) kept on the markup for consumers. The recording also carried
/// <c>tm-button</c>/<c>tm-button--ghost</c>/<c>tm-button--sm</c>, which turned out to be not dead
/// references at all but one live defect — the TmGantt import label, the only visible upload
/// element in its dialog, emitted classes no stylesheet declares; it now emits
/// <c>tm-btn tm-btn-ghost tm-btn-sm</c> and the three entries are struck off, which is exactly the
/// shrink direction <see cref="EveryRecordedUnstyledClass_IsStillEmitted"/> exists to force. Like
/// <c>UnconstrainedClassOwnershipTests.RecordedCollisions</c>, each is an inventory, not a budget:
/// an entry whose markup is removed must be struck off, and a class markup gains without a rule
/// must be added here BEFORE it ships.
/// </para>
/// </summary>
public sealed class MarkupClassCoverageTests
{
    private readonly ITestOutputHelper _output;

    public MarkupClassCoverageTests(ITestOutputHelper output) => _output = output;

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
    /// The 148 classes core markup emits with no rule anywhere in the library's stylesheets on
    /// 2.8.26, each named. None of these is a promise that they SHOULD stay unstyled — they are
    /// RECORDED so the guard can fail on a 149th.
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

    /// <summary>
    /// The 52 classes Signing markup emits with no rule in Signing's stylesheets OR the core's on
    /// 2.8.26 — Signing project-references the core package, so a core-owned class
    /// (<c>tm-btn</c>) is covered, and only what remains unstyled after BOTH trees are read lands
    /// here.
    /// </summary>
    private static readonly string[] UnstyledSigningMarkupClasses =
    [
        "tm-audit-trail-viewer__localization",
        "tm-comment-composer__button--secondary",
        "tm-comment-composer__mention-name",
        "tm-condition-builder--invalid",
        "tm-document-comments-panel__thread-mention",
        "tm-document-page-viewer--with-toolbar",
        "tm-document-page-viewer__comment-toggle",
        "tm-document-page-viewer__fit-page",
        "tm-document-page-viewer__fit-width",
        "tm-document-page-viewer__next-page",
        "tm-document-page-viewer__previous-page",
        "tm-document-page-viewer__zoom-in",
        "tm-document-page-viewer__zoom-out",
        "tm-pdf-template-designer__bulk-actions",
        "tm-pdf-template-designer__continuous",
        "tm-pdf-template-designer__copy-field",
        "tm-pdf-template-designer__copy-selection",
        "tm-pdf-template-designer__delete-field",
        "tm-pdf-template-designer__delete-selection",
        "tm-pdf-template-designer__detect-menu",
        "tm-pdf-template-designer__fit-page",
        "tm-pdf-template-designer__fit-width",
        "tm-pdf-template-designer__next-page",
        "tm-pdf-template-designer__paste-field",
        "tm-pdf-template-designer__previous-page",
        "tm-pdf-template-designer__single-page",
        "tm-pdf-template-designer__zoom-in",
        "tm-pdf-template-designer__zoom-out",
        "tm-recipient-role-editor--invalid",
        "tm-signing-attachment-step",
        "tm-signing-choice-step",
        "tm-signing-choice-step__checkbox",
        "tm-signing-choice-step__option--single",
        "tm-signing-choice-step__radio",
        "tm-signing-date-step",
        "tm-signing-external-step",
        "tm-signing-field",
        "tm-signing-field-editor-panel--empty",
        "tm-signing-field-editor-panel__localization",
        "tm-signing-field-editor-panel__options",
        "tm-signing-field-editor-panel__prefillable",
        "tm-signing-field-editor-panel__readonly",
        "tm-signing-field-editor-panel__required",
        "tm-signing-field-editor-panel__signature-id",
        "tm-signing-field-editor-panel__stamp-logo",
        "tm-signing-form-runner--accessibility",
        "tm-signing-form-runner__mobile-panel--collapsed",
        "tm-signing-number-step",
        "tm-signing-phone-step",
        "tm-signing-step-shell--invalid",
        "tm-signing-step-shell--required",
        "tm-signing-text-step",
    ];

    /// <summary>
    /// The 44 classes NotionEditor markup emits with no rule in NotionEditor's stylesheets (shared
    /// or scoped) OR the core's on 2.8.26. The <c>tm-dbc*</c> database-cell family and
    /// <c>tm-notification-bell*</c> are deliberately absent — they are core-owned and core-styled.
    /// </summary>
    private static readonly string[] UnstyledNotionMarkupClasses =
    [
        "tm-cbl__field--compact",
        "tm-cbl__state--prompt",
        "tm-child-page__icon",
        "tm-db__panel--fields",
        "tm-ndv__block--after",
        "tm-ndv__empty",
        "tm-ndv__pane--after",
        "tm-notion-analytics-panel",
        "tm-notion-blog-panel",
        "tm-notion-diagram-edit-modal__missing",
        "tm-notion-editable--readonly",
        "tm-notion-editor--single-page",
        "tm-notion-inline-toolbar__btn--color",
        "tm-notion-inline-toolbar__turn-icon",
        "tm-notion-media-upload-zone--audio",
        "tm-notion-media-upload-zone--diagram",
        "tm-notion-media-upload-zone--file",
        "tm-notion-media-upload-zone--image",
        "tm-notion-media-upload-zone--pdf",
        "tm-notion-media-upload-zone--spreadsheet",
        "tm-notion-media-upload-zone--video",
        "tm-notion-media-upload-zone--wireframe",
        "tm-notion-notifications",
        "tm-notion-presentation-toggle",
        "tm-notion-reading-exit",
        "tm-notion-reading-toggle",
        "tm-notion-shell-panel__close",
        "tm-notion-skeleton--rect",
        "tm-notion-spreadsheet-block__missing",
        "tm-notion-spreadsheet-edit-modal__btn--discard",
        "tm-notion-spreadsheet-edit-modal__missing",
        "tm-notion-topbar__shortcuts",
        "tm-notion-wireframe-edit-modal__missing",
        "tm-npsd__status--disabled",
        "tm-nsf",
        "tm-nsr",
        "tm-page-info__created",
        "tm-page-info__last-edited",
        "tm-page-info__reading-time",
        "tm-page-info__views",
        "tm-page-info__words",
        "tm-page-reactions__pill-count",
        "tm-work-item-picker__label",
        "tm-work-item__meta-item",
    ];

    /// <summary>
    /// One shipped component package as the sweep sees it: where its markup lives, which frozen
    /// inventory records its unstyled classes, and the population counts that prove the sweep read
    /// the whole tree. <paramref name="InheritsCoreCss"/> marks packages that project-reference
    /// <c>Tempo.Blazor</c>: their markup is rendered by a host that has the core stylesheet loaded,
    /// so a core-owned class is coverage, not a gap. <paramref name="ExpectedUnmeasurableStems"/>
    /// is the fail-closed count of dynamic-suffix stems the scan must find — the
    /// <c>unmeasurable:interpolated-class</c> population is checked by exact cardinality.
    /// </summary>
    private sealed record Scope(
        string Name,
        string ProjectDirectory,
        string[] UnstyledClasses,
        int MinRazorFiles,
        int MinMarkupClasses,
        bool InheritsCoreCss,
        int ExpectedUnmeasurableStems);

    private static readonly Scope[] Scopes =
    [
        new Scope("Tempo.Blazor", "Tempo.Blazor", UnstyledMarkupClasses,
            MinRazorFiles: 190, MinMarkupClasses: 2000, InheritsCoreCss: false,
            ExpectedUnmeasurableStems: 34),
        new Scope("Signing", "Tempo.Blazor.Signing", UnstyledSigningMarkupClasses,
            MinRazorFiles: 28, MinMarkupClasses: 400, InheritsCoreCss: true,
            ExpectedUnmeasurableStems: 6),
        new Scope("NotionEditor", "Tempo.Blazor.NotionEditor", UnstyledNotionMarkupClasses,
            MinRazorFiles: 125, MinMarkupClasses: 1900, InheritsCoreCss: true,
            ExpectedUnmeasurableStems: 17),
    ];

    [Fact]
    public void EveryCoreMarkupClass_HasARule_OrIsRecordedUnstyled() =>
        AssertScopeCovered(Scopes[0]);

    [Fact]
    public void EverySigningMarkupClass_HasARule_OrIsRecordedUnstyled() =>
        AssertScopeCovered(Scopes[1]);

    [Fact]
    public void EveryNotionEditorMarkupClass_HasARule_OrIsRecordedUnstyled() =>
        AssertScopeCovered(Scopes[2]);

    private static void AssertScopeCovered(Scope scope)
    {
        var selectors = AllSourceSelectors(scope);
        var uncovered = MarkupClasses(scope)
            .Where(c => !selectors.Contains(c))
            .Order(StringComparer.Ordinal)
            .ToList();

        uncovered.Should().BeEquivalentTo(
            scope.UnstyledClasses,
            "každá třída, kterou markup balíčku {0} emituje, má pravidlo ve stylesheetu, nebo je " +
            "jmenovitě zapsaná v jeho inventáři — nová třída bez pravidla tam patří dřív, než se " +
            "dostane ke konzumentovi. Změna oproti záznamu: {1}",
            scope.Name,
            string.Join(" | ", uncovered.Except(scope.UnstyledClasses, StringComparer.Ordinal)));
    }

    /// <summary>
    /// The reverse direction, per scope: a recorded exception whose class is no longer emitted must
    /// be struck off — a list that only ever grows stops describing the code.
    /// </summary>
    [Fact]
    public void EveryRecordedUnstyledClass_IsStillEmitted()
    {
        foreach (var scope in Scopes)
        {
            var emitted = MarkupClasses(scope);

            scope.UnstyledClasses
                .Where(recorded => !emitted.Contains(recorded))
                .Should().BeEmpty(
                    "třídu, kterou markup balíčku {0} přestal emitovat, ze seznamu škrtni — jinak " +
                    "seznam přestane popisovat kód a začne popisovat historii",
                    scope.Name);
        }
    }

    /// <summary>
    /// A class styled by the shared tree must be styled for a bundle host too: the bundle is a
    /// build artifact of the same sources, and a class missing from it is missing from the page.
    /// Scoped razor.css classes are exempt by design — they ship via {assembly}.styles.css.
    /// Only the CORE package ships a committed bundle; the satellites load their manifests.
    /// </summary>
    [Fact]
    public void SharedCssClasses_AreInTheShippedBundle()
    {
        var bundleSelectors = SelectorsIn(File.ReadAllText(Path.Combine(CssRoot(), "tempo-blazor.bundled.css")));
        var sharedSelectors = SharedSourceSelectors();

        var markup = MarkupClasses(Scopes[0]);
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
    /// The population denominators, asserted per scope: 195 razor + 51 razor.cs files yielding
    /// ~2 134 literal classes in core, 30 razor files / ~430 classes in Signing, 130 razor /
    /// ~1 995 classes in NotionEditor on 2.8.26. A sweep that silently reads half a tree reports
    /// "nothing missing" with perfect confidence — the count is how you know it looked.
    /// </summary>
    [Fact]
    public void TheSweepReadsTheWholeMarkupPopulation()
    {
        foreach (var scope in Scopes)
        {
            RazorFiles(scope).Should().HaveCountGreaterThanOrEqualTo(
                scope.MinRazorFiles,
                "{0} má ~{1} .razor souborů; menší číslo znamená, že sonda čte jinou složku",
                scope.Name, scope.MinRazorFiles);
            MarkupClasses(scope).Should().HaveCountGreaterThanOrEqualTo(
                scope.MinMarkupClasses,
                "markup balíčku {0} emituje ~{1} literálových tm-* tříd; desetina toho znamená, " +
                "že se extraktor rozbil",
                scope.Name, scope.MinMarkupClasses);
        }
    }

    /// <summary>
    /// What the sweep cannot measure it still COUNTS. A stem an expression extends
    /// (<c>tm-form-row--cols-{n}</c>, <c>tm-toc__item--level{…}</c>, <c>tm-notion-heading--h@(…)</c>)
    /// is an <c>unmeasurable:interpolated-class</c>: its emitted name is not statically known, so it
    /// is exempt from the coverage assertion — but per spec the stems are collected into their own
    /// set and counted, not dropped silently. The declared per-scope count must equal the scanned
    /// one exactly (fail-closed in BOTH directions): a markup change that adds or loses a stem
    /// shifts the denominator the coverage tests reason about, and a count that can only be
    /// "at least" would let a real addition pass unremarked.
    /// </summary>
    [Fact]
    public void TheSweep_CountsEveryUnmeasurableInterpolatedStem()
    {
        foreach (var scope in Scopes)
        {
            var stems = UnmeasurableStems(scope);

            _output.WriteLine(
                $"[markup-sweep] {scope.Name}: {stems.Count} unmeasurable:interpolated-class "
                + $"stem(s) — {string.Join(", ", stems.Order(StringComparer.Ordinal))}");

            stems.Should().HaveCount(
                scope.ExpectedUnmeasurableStems,
                "každý interpolovaný stem se počítá — deklarovaný počet pro {0} musí odpovídat "
                + "naskenovanému přesně, jinak se populace, nad kterou coverage testy usuzují, "
                + "změnila bez povšimnutí. Naskenované stemmy: {1}",
                scope.Name,
                string.Join(" | ", stems.Order(StringComparer.Ordinal)));
        }
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
            Enumerable.Empty<string>()).Classes;

        emitted.Should().Contain("tm-pagination-size").And.Contain("tm-invented-never-styled");
        AllSourceSelectors(Scopes[0]).Contains("tm-pagination-size").Should().BeTrue();
        AllSourceSelectors(Scopes[0]).Contains("tm-invented-never-styled").Should().BeFalse();
    }

    /// <summary>
    /// Comments are not markup and a dynamic suffix is not a class: a token inside a Razor or HTML
    /// comment must not be extracted, and <c>tm-form-row--cols-</c> + expression must not become a
    /// phantom class named <c>tm-form-row--cols</c>. The same for the other two dynamic shapes:
    /// <c>tm-notion-heading--h@(Level)</c> ends in a letter, and <c>tm-toc__item--level{n}</c> in
    /// interpolated code sits directly before a <c>{</c>. All three stems are nonetheless COUNTED —
    /// they land in <see cref="Extraction.UnmeasurableStems"/>, not nowhere.
    /// </summary>
    [Fact]
    public void TheExtractor_IgnoresCommentsAndDynamicSuffixes()
    {
        var extraction = ExtractMarkupClasses(
            [
                """
                @* class="tm-commented-out-razor" *@
                <!-- class="tm-commented-out-html" -->
                <div class="tm-form-row tm-form-row--cols-@Cols"></div>
                <h2 class="tm-notion-heading tm-notion-heading--h@(Level)"></h2>
                """
            ],
            ["return $\"tm-toc__item tm-toc__item--level{entry.Level}\";"]);

        var emitted = extraction.Classes;
        emitted.Should().NotContain("tm-commented-out-razor").And.NotContain("tm-commented-out-html");
        emitted.Should().NotContain("tm-form-row--cols",
            "dynamická přípona není třída — tm-form-row--cols-{n} nelze staticky ověřit");
        emitted.Should().NotContain("tm-notion-heading--h",
            "tm-notion-heading--h@(Level) emituje h1..h6 — stem bez čísla není třída");
        emitted.Should().NotContain("tm-toc__item--level");
        emitted.Should().Contain("tm-form-row").And.Contain("tm-notion-heading").And.Contain("tm-toc__item");

        extraction.UnmeasurableStems.Should().BeEquivalentTo(
            ["tm-form-row--cols-", "tm-notion-heading--h", "tm-toc__item--level"],
            "interpolované stemmy nejsou třídy, ale počítají se — nesmí zmizet z evidence");
    }

    /// <summary>
    /// A CSS variable reference is not a class: <c>"var(--tm-color-primary)"</c> and
    /// <c>"--tm-gantt-task-color: {x}"</c> in code-behind produced five phantom "classes" in the
    /// first version of this sweep — tokens whose name starts after a <c>-</c> are variables, not
    /// markup output.
    /// </summary>
    [Fact]
    public void TheExtractor_IgnoresVariableReferences()
    {
        var extraction = ExtractMarkupClasses(
            Enumerable.Empty<string>(),
            [
                """
                list.Add(new LegendItem(label, "var(--tm-color-primary)"));
                return $"--tm-gantt-task-color: {color};";
                classes.Add("tm-real-class");
                """
            ]);

        extraction.Classes.Should().NotContain("tm-color-primary").And.NotContain("tm-gantt-task-color");
        extraction.Classes.Should().Contain("tm-real-class");
        extraction.UnmeasurableStems.Should().BeEmpty(
            "reference na proměnnou není ani stem — '--tm-…' se nepočítá vůbec");
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

    /// <summary>
    /// What one extraction pass yields: <paramref name="Classes"/> are the literal
    /// <c>tm-*</c> tokens markup provably emits (the coverage population), and
    /// <paramref name="UnmeasurableStems"/> are the dynamic-suffix stems an expression extends —
    /// <c>unmeasurable:interpolated-class</c>, counted but not coverage-checked.
    /// </summary>
    private sealed record Extraction(HashSet<string> Classes, HashSet<string> UnmeasurableStems);

    private static HashSet<string> MarkupClasses(Scope scope) =>
        ExtractMarkupClasses(RazorMarkup(scope), CodeBehindFiles(scope)).Classes;

    private static HashSet<string> UnmeasurableStems(Scope scope) =>
        ExtractMarkupClasses(RazorMarkup(scope), CodeBehindFiles(scope)).UnmeasurableStems;

    private static IEnumerable<string> RazorMarkup(Scope scope) =>
        RazorFiles(scope).Select(File.ReadAllText);

    private static IEnumerable<string> CodeBehindFiles(Scope scope) =>
        Directory.EnumerateFiles(ProjectRoot(scope), "*.razor.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText);

    private static List<string> RazorFiles(Scope scope) =>
        Directory.EnumerateFiles(ProjectRoot(scope), "*.razor", SearchOption.AllDirectories).ToList();

    private static Extraction ExtractMarkupClasses(
        IEnumerable<string> razorDocuments,
        IEnumerable<string> codeBehindDocuments)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var unmeasurableStems = new HashSet<string>(StringComparer.Ordinal);

        foreach (var document in razorDocuments)
        {
            var cleaned = StripComments(document);
            foreach (Match attribute in ClassAttribute.Matches(cleaned))
            {
                AddTokens(attribute.Groups[1].Value, names, unmeasurableStems);
            }
        }

        foreach (var document in codeBehindDocuments)
        {
            foreach (Match literal in QuotedLiteral.Matches(document))
            {
                AddTokens(literal.Groups[1].Value, names, unmeasurableStems);
            }
        }

        return new Extraction(names, unmeasurableStems);
    }

    /// <summary>
    /// Records complete tm-* tokens into <paramref name="names"/>, and dynamic-suffix stems into
    /// <paramref name="unmeasurableStems"/>. Three shapes are NOT classes: a token ending in
    /// <c>-</c> or immediately followed by <c>@</c>/<c>{</c> is a dynamic-suffix stem
    /// (<c>tm-form-row--cols-</c>, <c>tm-notion-heading--h@(Level)</c>,
    /// <c>tm-toc__item--level{n}</c>) — unmeasurable as a class but still COUNTED; a token preceded
    /// by <c>-</c> is a CSS variable name inside a literal (<c>"var(--tm-color-primary)"</c>),
    /// never a class attribute, and lands in neither set.
    /// </summary>
    private static void AddTokens(string text, HashSet<string> names, HashSet<string> unmeasurableStems)
    {
        foreach (Match token in TmToken.Matches(text))
        {
            var value = token.Value;
            if (token.Index > 0 && text[token.Index - 1] == '-')
            {
                continue; // "--tm-…" is a custom property, not a class
            }

            var after = token.Index + value.Length;
            if (value.EndsWith('-')
                || (after < text.Length && (text[after] == '@' || text[after] == '{')))
            {
                // an expression extends the stem — the emitted name is not statically known,
                // so the stem is counted as unmeasurable:interpolated-class instead of dropped
                unmeasurableStems.Add(value);
                continue;
            }

            names.Add(value);
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

    /// <summary>
    /// Selectors that can reach the scope's markup: its shared tree + its scoped razor.css — and,
    /// for the satellite packages that project-reference the core, the CORE tree and core scoped
    /// styles too (a Signing host loads Tempo.Blazor.styles.css through the app's scoped bundle).
    /// </summary>
    private static HashSet<string> AllSourceSelectors(Scope scope)
    {
        var selectors = SharedSourceSelectors(scope);
        CollectScopedStyles(ProjectRoot(scope), selectors);

        if (scope.InheritsCoreCss)
        {
            foreach (var file in Directory.EnumerateFiles(CssRoot(), "*.css", SearchOption.AllDirectories))
            {
                selectors.UnionWith(SelectorsIn(File.ReadAllText(file)));
            }

            CollectScopedStyles(
                Path.Combine(FindRepositoryRoot(), "src", "Tempo.Blazor"), selectors);
        }

        return selectors;
    }

    private static void CollectScopedStyles(string projectRoot, HashSet<string> selectors)
    {
        foreach (var file in Directory.EnumerateFiles(projectRoot, "*.razor.css", SearchOption.AllDirectories))
        {
            selectors.UnionWith(SelectorsIn(File.ReadAllText(file)));
        }
    }

    /// <summary>Selectors in the scope's shipped shared tree — what a bundle or manifest is built from.</summary>
    private static HashSet<string> SharedSourceSelectors(Scope? scope = null)
    {
        var selectors = new HashSet<string>(StringComparer.Ordinal);
        var root = scope is null
            ? CssRoot()
            : Path.Combine(FindRepositoryRoot(), "src", scope.ProjectDirectory, "wwwroot", "css");
        foreach (var file in Directory.EnumerateFiles(root, "*.css", SearchOption.AllDirectories))
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

    private static string ProjectRoot(Scope scope) =>
        Path.Combine(FindRepositoryRoot(), "src", scope.ProjectDirectory);

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

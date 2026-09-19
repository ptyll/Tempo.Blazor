using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Sweeps the CLASS of defect that <c>PivotButtonScopeTests</c> only sweeps for buttons: a class
/// declared UNCONSTRAINED (a bare <c>.foo</c>, no ancestor, no state) in more than one component
/// stylesheet, where the two declarations set the same property to different values. Which one a user
/// sees is then decided by the import order of the manifest, not by anyone's intent — exactly the
/// mechanism that hid the <c>.tm-btn</c> border for two releases.
/// <para>
/// IT EXISTS BECAUSE THE OLD DENOMINATOR WAS THE WRONG POPULATION, not because it was unread. The
/// button sweep answers "does anything else own <c>.tm-btn*</c>" and its answer is complete. Nobody
/// was asking the same question about every other shared class, and the 2.8.22 changelog claimed the
/// button sweep covered it. It did not: fifteen further pairs were sitting in the same directory —
/// all fifteen fixed in 2.8.26, so <see cref="RecordedCollisions"/> now stands empty.
/// </para>
/// <para>
/// EVERY KNOWN PAIR HAS ITS OWN ROW. A single number ("15 known collisions") would become a threshold
/// under which individual pairs disappear — <c>DEC-EXCEPTION-REGISTER-ONE-MODEL</c> names that as the
/// anti-pattern. The frozen list below is therefore an inventory, not a budget: a pair that is fixed
/// must be deleted from it, and a pair that appears must fail here before anyone can add it.
/// </para>
/// <para>
/// DESCENDANT SELECTORS, IN SCOPE SINCE 2.9.0: a rule <c>A B</c> or <c>A &gt; B</c> whose last
/// compound is a class or an element counts as an ownership claim OF B — the mechanism the "dead
/// duplicate" removals of 2.8.26 proved live, when <c>_dashboard.css</c>'s <c>.tm-modal-header h3</c>
/// and <c>_data-table.css</c>'s <c>.tm-filter-chip button</c> painted real pixels through other
/// files' components. Two claims on the same B from DIFFERENT files collide when their ancestor
/// regions can be the same one — either claim without ancestors fights everything, and two
/// ancestor chains fight when they share at least one compound — and they declare the same
/// property differently. Equal specificity is a TIE the manifest order decides; unequal is an
/// OVERRIDE in which the losing file's declaration never paints. Both are findings, and the
/// recorded list below names each one: the <c>.tm-rte-form-group label</c> residual the old
/// paragraph already disclosed (margin-bottom <c>--tm-space-2</c> vs <c>--tm-space-1</c>, image
/// value wins in all five RTE dialogs — UX review of 2.8.26, ~4px, cosmetic, owner ptyll) plus the
/// pairs the extended model surfaced for the first time.
/// </para>
/// <para>
/// SCOPE LIMIT, STILL DECLARED: a selector carrying a combinator other than descendant/child
/// (<c>+</c>, <c>~</c>), an attribute, an id, a <c>*</c>, or a last compound that is not a pure
/// class/element (a state like <c>:hover</c> is a CONDITION, not an ownership claim) stays outside
/// the model — as does an ancestor compound the normaliser cannot read (<c>[data-theme]</c>
/// scoping is deliberately themed, not ownership).
/// </para>
/// </summary>
public class UnconstrainedClassOwnershipTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// One selector part as an ownership claim: its last compound (the key it claims), the ancestor
    /// compounds that scope it, and the specificity the full selector wins or loses at.
    /// </summary>
    private sealed record Claim(
        string Selector,
        IReadOnlySet<string> Ancestors,
        (int Id, int Class, int Type) Specificity,
        string? Media,
        Dictionary<string, string> Props);

    /// <summary>The tie/override the two claims resolve to, named so a finding says which it is.</summary>
    private enum Contest
    {
        /// <summary>Equal specificity — the manifest's import order picks the winner.</summary>
        Tie,

        /// <summary>Unequal specificity — one file's declaration never paints at all.</summary>
        Override,
    }

    /// <summary>
    /// Whether the regions two claims style can be the same element's ancestry: an unconditional
    /// claim fights everything, and two scoped claims fight when they share at least one ancestor
    /// compound that names a CLASS — the element can sit inside that shared component region and
    /// satisfy both. A bare ELEMENT ancestor (<c>tbody</c>, <c>tr</c>, <c>li</c>) marks no
    /// component: <c>.tm-data-table tbody tr td</c> and <c>.tm-data-import__table tbody tr td</c>
    /// share <c>tbody</c>/<c>tr</c> yet style disjoint markup, and counting them is how the sweep
    /// cried wolf over every table in the library. Two claims through DISJOINT non-empty ancestor
    /// sets never contest the same box, and reporting them is how a sweep cries wolf over
    /// components that legitimately style their own markup.
    /// </summary>
    private static bool RegionsCanFight(IReadOnlySet<string> first, IReadOnlySet<string> second) =>
        first.Count == 0 || second.Count == 0
        || first.Intersect(second).Any(compound => compound.Contains('.'));

    /// <summary>
    /// The shared <c>wwwroot/css/components</c> directories the sweep reads — the core library plus
    /// Signing and NotionEditor, whose packages a host loads side by side. A bare class declared in
    /// a Signing stylesheet and again in the core one is the same order-dependent defect, just across
    /// a package boundary. Scoped <c>*.razor.css</c> files are deliberately absent: their selectors
    /// compile to <c>.x[b-hash]</c> and cannot collide.
    /// </summary>
    private static readonly string[] ComponentCssProjects =
    [
        "Tempo.Blazor",
        "Tempo.Blazor.Signing",
        "Tempo.Blazor.NotionEditor",
    ];

    /// <summary>One last-compound owned by two stylesheets, with the properties they disagree about.</summary>
    private sealed record Collision(
        string Subject,
        string First,
        string Second,
        IReadOnlyList<string> Properties,
        Contest Contest)
    {
        public string Key => string.Create(CultureInfo.InvariantCulture, $"{Subject} {First}|{Second}");

        public override string ToString() =>
            string.Create(CultureInfo.InvariantCulture,
                $"{Subject}: {First} vs {Second} ({Contest}) — {string.Join(", ", Properties)}");
    }

    /// <summary>
    /// What the sweep found on 2.8.23 was fifteen pairs, each named — none approved, all RECORDED so
    /// the guard could fail on a sixteenth. 2.8.26 fixed every one of them (filter-chip, form-field,
    /// six tm-modal*, four tm-rte-*, three tm-timeline-*), so the list stood EMPTY for a release —
    /// until the descendant model of 2.9.0 surfaced the pairs below. Each row is the same honest
    /// inventory the bare-class list was: the subject the two files both claim, the two
    /// stylesheets in ordinal order, and — for the reader — the contest in the comment, not in the
    /// key, so a fix that merely re-orders declarations does not mint a "new" collision.
    /// </summary>
    private static readonly string[] RecordedCollisions =
    [
        // The two RTE editor skins style the same markup skeleton and disagree on it — the
        // descendant claims in _rich-editor-full/_rich-editor-simple mostly OVERRIDE the bare
        // rules, while their equal-specificity pairs are TIES the manifest order decides. Every
        // one is a real import-order dependency; the visual difference between the two skins is
        // intentional, the mechanism that produces it is the defect class this sweep exists for.
        ".tm-mention Tempo.Blazor/_rich-editor-full.css|Tempo.Blazor/_rich-editor-simple.css",
        ".tm-task-checkbox Tempo.Blazor/_rich-editor-full.css|Tempo.Blazor/_rich-editor-simple.css",
        ".tm-task-checked Tempo.Blazor/_rich-editor-full.css|Tempo.Blazor/_rich-editor-simple.css",
        ".tm-task-item Tempo.Blazor/_rich-editor-full.css|Tempo.Blazor/_rich-editor-simple.css",
        ".tm-token Tempo.Blazor/_rich-editor-full.css|Tempo.Blazor/_rich-editor-simple.css",
        ".tm-token-chip-icon Tempo.Blazor/_rich-editor-full.css|Tempo.Blazor/_rich-editor-simple.css",
        ".tm-token.token-number Tempo.Blazor/_rich-editor-full.css|Tempo.Blazor/_rich-editor-simple.css",
        ".tm-token.token-secret Tempo.Blazor/_rich-editor-full.css|Tempo.Blazor/_rich-editor-simple.css",
        ".tm-token.token-url Tempo.Blazor/_rich-editor-full.css|Tempo.Blazor/_rich-editor-simple.css",
        "a Tempo.Blazor/_rich-editor-full.css|Tempo.Blazor/_rich-editor-simple.css",
        "b Tempo.Blazor/_rich-editor-full.css|Tempo.Blazor/_rich-editor-simple.css",
        "em Tempo.Blazor/_rich-editor-full.css|Tempo.Blazor/_rich-editor-simple.css",
        "i Tempo.Blazor/_rich-editor-full.css|Tempo.Blazor/_rich-editor-simple.css",
        "li Tempo.Blazor/_rich-editor-full.css|Tempo.Blazor/_rich-editor-simple.css",
        "ol Tempo.Blazor/_rich-editor-full.css|Tempo.Blazor/_rich-editor-simple.css",
        "strong Tempo.Blazor/_rich-editor-full.css|Tempo.Blazor/_rich-editor-simple.css",
        "u Tempo.Blazor/_rich-editor-full.css|Tempo.Blazor/_rich-editor-simple.css",
        "ul Tempo.Blazor/_rich-editor-full.css|Tempo.Blazor/_rich-editor-simple.css",

        // The RTE family beyond the two skins: the toolbar's own stylesheet out-ranks the bare
        // .tm-rte-btn of _rich-text-editor.css (Override — the bare declarations are dead inside
        // the only region .tm-rte-btn renders), and the placeholder text is claimed by the base
        // stylesheet and both skins.
        ".tm-rte-btn Tempo.Blazor/_editor-toolbar.css|Tempo.Blazor/_rich-text-editor.css",
        ".tm-rte-form-group Tempo.Blazor/_image-dialog.css|Tempo.Blazor/_link-dialog.css",
        ".tm-rte-placeholder Tempo.Blazor/_rich-editor-full.css|Tempo.Blazor/_rich-text-editor.css",
        ".tm-rte-placeholder Tempo.Blazor/_rich-editor-simple.css|Tempo.Blazor/_rich-text-editor.css",

        // The residual the old scope-limit paragraph already disclosed: .tm-rte-form-group label
        // is claimed by both dialog stylesheets at equal specificity, and the manifest order makes
        // the image-dialog value (margin-bottom --tm-space-1, 4px) win in all five RTE dialogs
        // including the link one (UX review of 2.8.26; ~4px, cosmetic, owner ptyll).
        "label Tempo.Blazor/_image-dialog.css|Tempo.Blazor/_link-dialog.css",

        // The calendar view reskins the shared day/grid classes inside its own day-cells — an
        // intentional Override the bare _calendar-grid.css declarations always lose there.
        ".tm-cal-day Tempo.Blazor/_calendar-grid.css|Tempo.Blazor/_calendar-view.css",
        ".tm-cal-day--disabled Tempo.Blazor/_calendar-grid.css|Tempo.Blazor/_calendar-view.css",
        ".tm-cal-day--other-month Tempo.Blazor/_calendar-grid.css|Tempo.Blazor/_calendar-view.css",
        ".tm-cal-day--selected Tempo.Blazor/_calendar-grid.css|Tempo.Blazor/_calendar-view.css",
        ".tm-cal-grid Tempo.Blazor/_calendar-grid.css|Tempo.Blazor/_calendar-view.css",

        // TmTimeline and TmActivityTimeline share class names on purpose — _activity-timeline.css
        // documents that itself — and its .tm-timeline-item descendants always out-rank the bare
        // _timeline.css rules inside activity items (Override; the line-height difference is the
        // undisclosed change the 2.9.0 changelog now names).
        ".tm-timeline-author Tempo.Blazor/_activity-timeline.css|Tempo.Blazor/_timeline.css",
        ".tm-timeline-content Tempo.Blazor/_activity-timeline.css|Tempo.Blazor/_timeline.css",

        // The colour-picker's action row restyles the shared .tm-btn (white-space, Override);
        // Signing's field-editor panel reaches into the condition-builder classes it composes
        // (all Overrides), and the pdf-template-designer re-anchors the shared context-menu
        // wrapper (position, Override).
        ".tm-btn Tempo.Blazor/_button.css|Tempo.Blazor/_color-picker.css",
        ".tm-condition-builder__field-group Tempo.Blazor.Signing/_condition-builder.css|Tempo.Blazor.Signing/_signing-field-editor-panel.css",
        ".tm-condition-builder__field-group--operation Tempo.Blazor.Signing/_condition-builder.css|Tempo.Blazor.Signing/_signing-field-editor-panel.css",
        ".tm-condition-builder__field-group--value Tempo.Blazor.Signing/_condition-builder.css|Tempo.Blazor.Signing/_signing-field-editor-panel.css",
        ".tm-condition-builder__remove Tempo.Blazor.Signing/_condition-builder.css|Tempo.Blazor.Signing/_signing-field-editor-panel.css",
        ".tm-condition-builder__row Tempo.Blazor.Signing/_condition-builder.css|Tempo.Blazor.Signing/_signing-field-editor-panel.css",
        ".tm-context-menu-wrapper Tempo.Blazor.Signing/_pdf-template-designer.css|Tempo.Blazor/_context-menu.css",

        // NotionEditor's dark skin restyles the typography file's blocks — background Overrides on
        // the callout/code-block classes and on code itself, all inside the notion regions.
        ".tm-notion-callout Tempo.Blazor.NotionEditor/_notion-dark.css|Tempo.Blazor.NotionEditor/_notion-typography.css",
        ".tm-notion-code-block Tempo.Blazor.NotionEditor/_notion-dark.css|Tempo.Blazor.NotionEditor/_notion-typography.css",
        "code Tempo.Blazor.NotionEditor/_notion-dark.css|Tempo.Blazor.NotionEditor/_notion-typography.css",
    ];

    [Fact]
    public void NoNewClassIsOwnedByTwoStylesheets()
    {
        var found = Collisions();

        found.Select(collision => collision.Key).Should().BeSubsetOf(
            RecordedCollisions,
            "třída deklarovaná neomezeně ve dvou souborech nechává o vzhledu rozhodnout pořadí importů " +
            "v manifestu; nová taková dvojice se musí objevit tady dřív, než se dostane ke konzumentovi. " +
            "Nalezeno: {0}",
            string.Join(" | ", found.Select(collision => collision.ToString())));
    }

    /// <summary>
    /// The other direction: a recorded pair that no longer exists must be DELETED from the list, not
    /// left as a permanently satisfied entry. A list that only ever grows stops describing the code.
    /// </summary>
    [Fact]
    public void EveryRecordedCollisionStillExists()
    {
        var keys = Collisions().Select(collision => collision.Key).ToHashSet(StringComparer.Ordinal);

        RecordedCollisions.Where(recorded => !keys.Contains(recorded)).Should().BeEmpty(
            "opravená dvojice se ze seznamu škrtá — jinak seznam přestane popisovat kód a začne " +
            "popisovat historii");
    }

    /// <summary>
    /// The population, asserted separately. The 2.8.22 changelog claimed the button sweep read the whole
    /// directory; the number it was written from was 61 and the directory holds 139. A denominator that
    /// nobody checks is how a sweep reports "nothing found" without having looked.
    /// </summary>
    [Fact]
    public void TheSweepReadsTheWholeComponentDirectory()
    {
        Directory.EnumerateFiles(
                Path.Combine(ThemeCss.RepositoryRoot().FullName,
                    "src", "Tempo.Blazor", "wwwroot", "css", "components"), "*.css")
            .Should().HaveCountGreaterThanOrEqualTo(
                139,
                "components/ neslo nikdy pod 139 souborů; menší číslo znamená, že sonda čte jinou složku");
        Directory.EnumerateFiles(
                Path.Combine(ThemeCss.RepositoryRoot().FullName,
                    "src", "Tempo.Blazor.Signing", "wwwroot", "css", "components"), "*.css")
            .Should().HaveCountGreaterThanOrEqualTo(
                15,
                "Signing/components má 15 souborů; menší číslo znamená, že se složka ztratila ze sweepu");
        Directory.EnumerateFiles(
                Path.Combine(ThemeCss.RepositoryRoot().FullName,
                    "src", "Tempo.Blazor.NotionEditor", "wwwroot", "css", "components"), "*.css")
            .Should().HaveCountGreaterThanOrEqualTo(
                4,
                "NotionEditor/components má 4 soubory; menší číslo znamená, že se složka ztratila ze sweepu");

        // The positive control lives in EveryRecordedCollisionStillExists: the recorded pairs being
        // found is what proves the probe can read, and a pair that stops existing is a red there.
    }

    /// <summary>
    /// Mutation, both directions: an invented duplicate must be seen, and a state-constrained
    /// duplicate must NOT be — a state is a condition on the same owner, not a competing claim.
    /// </summary>
    [Fact]
    public void TheSweepSeesADuplicateAndIgnoresAStateConstrainedOne()
    {
        var declarations = NewDeclarations();
        Collect("a.css", ".tm-widget { color: red; }", declarations);
        Collect("b.css", ".tm-widget { color: blue; }", declarations);
        Pairs(declarations).Should().ContainSingle().Which.Properties.Should().Equal("color");

        declarations.Clear();
        Collect("a.css", ".tm-widget { color: red; }", declarations);
        Collect("b.css", ".tm-widget:hover { color: green; }", declarations);
        Pairs(declarations).Should().BeEmpty(
            "pravidlo omezené stavem je záměr, ne nárok — :hover je podmínka na téže třídě, " +
            "ne vlastnictví nového kompozitu");

        declarations.Clear();
        Collect("a.css", ".tm-widget { color: red; }", declarations);
        Collect("b.css", ".tm-widget { margin: 0; }", declarations);
        Pairs(declarations).Should().BeEmpty("dva soubory, dvě různé vlastnosti — nikdo o nic nesoupeří");
    }

    /// <summary>
    /// The descendant half of the mutation, in every direction the model promises: a rule
    /// <c>A B</c> or <c>A &gt; B</c> in one file collides with another file's claim on the same B —
    /// an OVERRIDE when its specificity is higher, a TIE when it is equal — while the same B
    /// through DISJOINT ancestor regions is two components styling their own markup, not a contest.
    /// This is the exact shape of the 2.8.26 regression: <c>.tm-filter-chip button</c> lived in
    /// <c>_data-table.css</c> and beat the remove-button's own class in <c>_filter-chip.css</c>.
    /// </summary>
    [Fact]
    public void TheSweepSeesADescendantClaimBeatingAnotherFilesOwner()
    {
        // The 2.8.26 shape: a bare class owned by one file, out-ranked by a descendant in another.
        var declarations = NewDeclarations();
        Collect("owner.css", ".tm-widget { color: red; padding: 4px; }", declarations);
        Collect("foreign.css", ".tm-panel .tm-widget { color: blue; }", declarations);
        var override_ = Pairs(declarations).Should().ContainSingle(
            "potomek cizího souboru (0,2,0) přebíjí vlastníka (0,1,0) — ta deklarace nikdy nemaluje").Which;
        override_.Contest.Should().Be(Contest.Override);
        override_.Properties.Should().Equal("color");

        // The child combinator claims the same way — > is a descendant of depth one.
        declarations.Clear();
        Collect("owner.css", ".tm-widget { color: red; }", declarations);
        Collect("foreign.css", ".tm-panel > .tm-widget { color: blue; }", declarations);
        Pairs(declarations).Should().ContainSingle("A > B je stejný nárok jako A B");

        // A TIE: same subject claimed at equal specificity — the manifest's import order decides.
        declarations.Clear();
        Collect("a.css", ".tm-panel .tm-widget { color: red; }", declarations);
        Collect("b.css", ".tm-panel .tm-widget { color: blue; }", declarations);
        Pairs(declarations).Should().ContainSingle().Which.Contest.Should().Be(Contest.Tie,
            "stejný selektor ve dvou souborech — o barvě rozhoduje pořadí importů, ne autor");

        // An ELEMENT as the last compound is claimed identically: .tm-x button vs button.
        declarations.Clear();
        Collect("a.css", ".tm-panel button { color: red; }", declarations);
        Collect("b.css", "button { color: blue; }", declarations);
        Pairs(declarations).Should().ContainSingle(
            "prvek jako poslední kompozit je nárok stejně jako třída — remíza či přebití platí i pro něj");

        // Disjoint ancestor regions never fight: two components styling their own markup.
        declarations.Clear();
        Collect("a.css", ".tm-panel .tm-widget { color: red; }", declarations);
        Collect("b.css", ".tm-zone .tm-widget { color: blue; }", declarations);
        Pairs(declarations).Should().BeEmpty(
            ".tm-panel a .tm-zone jsou disjunktní oblasti — prvek v jedné nemůže být v druhé současně");

        // A state on the last compound is still a condition, not a claim — unchanged by descent.
        declarations.Clear();
        Collect("a.css", ".tm-widget { color: red; }", declarations);
        Collect("b.css", ".tm-panel .tm-widget:hover { color: blue; }", declarations);
        Pairs(declarations).Should().BeEmpty(":hover na posledním kompozitu je podmínka, ne nárok");
    }

    /// <summary>
    /// Media-awareness, both directions: a rule inside <c>@media</c> still collides with an
    /// unconditional one (both apply whenever the condition holds — the condition narrows WHEN, not
    /// WHETHER, they fight), while two rules behind mutually exclusive conditions cannot compete and
    /// must NOT be reported — a flattened reader calls it a collision that no user can ever see.
    /// </summary>
    [Fact]
    public void TheSweepRespectsMediaConditions()
    {
        var declarations = NewDeclarations();
        Collect("a.css", ".tm-widget { color: red; }", declarations);
        Collect("b.css", "@media (max-width: 768px) { .tm-widget { color: blue; } }", declarations);
        Pairs(declarations).Should().ContainSingle(
            "podmíněné pravidlo s nepodmíněným soupeří vždy, když podmínka platí");

        declarations.Clear();
        Collect("a.css", "@media (max-width: 768px) { .tm-widget { color: red; } }", declarations);
        Collect("b.css", "@media (min-width: 1024px) { .tm-widget { color: blue; } }", declarations);
        Pairs(declarations).Should().BeEmpty(
            "viewport pod 768 px a nad 1024 px nikdy neexistuje současně — ta dvojice nesoupeří");
    }

    /// <summary>Per claimed subject (normalised last compound) → per stylesheet → its claims.</summary>
    private static Dictionary<string, Dictionary<string, List<Claim>>> NewDeclarations() =>
        new(StringComparer.Ordinal);

    private static IReadOnlyList<Collision> Collisions()
    {
        var declarations = NewDeclarations();
        foreach (var project in ComponentCssProjects)
        {
            var dir = Path.Combine(
                ThemeCss.RepositoryRoot().FullName, "src", project, "wwwroot", "css", "components");
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.css").Order(StringComparer.Ordinal))
            {
                Collect($"{project}/{Path.GetFileName(file)}", File.ReadAllText(file), declarations);
            }
        }

        return Pairs(declarations);
    }

    private static readonly Regex PureCompound =
        new(@"^([a-zA-Z][\w-]*)?(\.[a-zA-Z][\w-]*)*$", RegexOptions.Compiled, Timeout);

    /// <summary>
    /// The ownership claim one selector part makes, or null when the selector is outside the model
    /// (a combinator other than descendant/child, an attribute, an id, a <c>*</c>, or a last
    /// compound that is not a pure class/element — a <c>:hover</c> there is a condition, not a
    /// claim). A compound is normalised to <c>tag + its classes sorted</c>, so <c>.tm-b.tm-a</c>
    /// and <c>.tm-a.tm-b</c> claim the same subject.
    /// </summary>
    private static (string Subject, Claim Claim)? TryClaim(
        string selector,
        string? media,
        Dictionary<string, string> props)
    {
        if (selector.IndexOfAny(['+', '~', '#', '[', '*']) >= 0)
        {
            return null;
        }

        var compounds = selector.Split([' ', '>'], StringSplitOptions.RemoveEmptyEntries);
        if (compounds.Length == 0)
        {
            return null;
        }

        var subject = NormaliseCompound(compounds[^1], stripPseudo: false);
        if (subject is null)
        {
            return null;
        }

        var ancestors = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < compounds.Length - 1; i++)
        {
            var ancestor = NormaliseCompound(compounds[i], stripPseudo: true);
            if (ancestor is null)
            {
                return null;
            }

            ancestors.Add(ancestor);
        }

        // Specificity over the RAW compounds, the way CssCascade counts it: every '.' and every
        // ':' contributes the class column, a compound starting with neither contributes the type
        // column. Ids were rejected above, so the id column stays 0.
        var classCount = compounds.Sum(c => c.Count(ch => ch == '.')) +
                         compounds.Sum(c => c.Count(ch => ch == ':'));
        var typeCount = compounds.Count(c => !c.StartsWith('.') && !c.StartsWith(':'));
        return (subject, new Claim(selector, ancestors, (0, classCount, typeCount), media, props));
    }

    /// <summary>
    /// A compound as a canonical key: its tag plus its classes in sort order. With
    /// <paramref name="stripPseudo"/> the pseudo tail is dropped first (an ancestor
    /// <c>.tm-x:hover</c> still scopes the element inside <c>.tm-x</c>); without it a pseudo makes
    /// the compound un-claimable, because a state on the SUBJECT is a condition, not an ownership.
    /// </summary>
    private static string? NormaliseCompound(string compound, bool stripPseudo)
    {
        var colon = compound.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0)
        {
            if (!stripPseudo)
            {
                return null;
            }

            compound = compound[..colon];
        }

        if (!PureCompound.IsMatch(compound) || compound.Length == 0)
        {
            return null;
        }

        var tagEnd = compound.IndexOf('.', StringComparison.Ordinal);
        var tag = tagEnd < 0 ? compound : compound[..tagEnd];

        // Splitting "tag.a.b" on '.' yields ["tag","a","b"], ".a.b" yields ["a","b"] — so the
        // classes are everything after the tag, or everything when the compound is class-only.
        var parts = compound.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var classParts = tag.Length > 0 ? parts.Skip(1) : parts;
        var normalised = tag + string.Concat(classParts.Order(StringComparer.Ordinal).Select(c => "." + c));
        return normalised.Length > 0 ? normalised : null;
    }

    /// <summary>Records every ownership claim of one stylesheet, keeping the media condition it sits under.</summary>
    private static void Collect(
        string stylesheet,
        string css,
        Dictionary<string, Dictionary<string, List<Claim>>> declarations)
    {
        foreach (var rule in CssCascade.ParseRules(ThemeCss.StripComments(css)))
        {
            var properties = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var declaration in rule.Body.Split(';'))
            {
                var separator = declaration.IndexOf(':', StringComparison.Ordinal);
                if (separator > 0)
                {
                    properties[declaration[..separator].Trim()] = ThemeCss.Normalise(declaration[(separator + 1)..]);
                }
            }

            foreach (var part in ThemeCss.SelectorParts(rule.Selector))
            {
                var claimed = TryClaim(part, rule.MediaCondition, properties);
                if (claimed is null)
                {
                    continue;
                }

                var perFile = declarations.TryGetValue(claimed.Value.Subject, out var existing)
                    ? existing
                    : declarations[claimed.Value.Subject] = new(StringComparer.Ordinal);
                var instances = perFile.TryGetValue(stylesheet, out var owned)
                    ? owned
                    : perFile[stylesheet] = [];
                instances.Add(claimed.Value.Claim);
            }
        }
    }

    private static IReadOnlyList<Collision> Pairs(
        Dictionary<string, Dictionary<string, List<Claim>>> declarations)
    {
        var collisions = new List<Collision>();
        foreach (var (subject, perFile) in declarations.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var files = perFile.Keys.Order(StringComparer.Ordinal).ToList();
            for (var i = 0; i < files.Count; i++)
            {
                for (var j = i + 1; j < files.Count; j++)
                {
                    var contested = perFile[files[i]]
                        .SelectMany(a => perFile[files[j]]
                            .Where(b => RegionsCanFight(a.Ancestors, b.Ancestors)
                                        && CssCascade.MediaCanOverlap(a.Media, b.Media))
                            .Select(b => (
                                Props: a.Props.Keys.Where(b.Props.ContainsKey),
                                Tie: a.Specificity == b.Specificity)))
                        .ToList();

                    var shared = contested
                        .SelectMany(pair => pair.Props)
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToList();

                    if (shared.Count > 0)
                    {
                        collisions.Add(new Collision(
                            subject,
                            files[i],
                            files[j],
                            shared,
                            contested.All(pair => pair.Tie) ? Contest.Tie : Contest.Override));
                    }
                }
            }
        }

        return collisions;
    }
}

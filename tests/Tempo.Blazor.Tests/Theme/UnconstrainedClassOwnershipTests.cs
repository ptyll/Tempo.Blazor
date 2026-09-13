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
/// </summary>
public class UnconstrainedClassOwnershipTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static readonly Regex BareClass =
        new(@"^\.[a-zA-Z][\w-]*$", RegexOptions.Compiled, Timeout);

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

    /// <summary>One class owned by two stylesheets, with the properties they disagree about.</summary>
    private sealed record Collision(string Class, string First, string Second, IReadOnlyList<string> Properties)
    {
        public string Key => string.Create(CultureInfo.InvariantCulture, $"{Class} {First}|{Second}");

        public override string ToString() =>
            string.Create(CultureInfo.InvariantCulture,
                $"{Class}: {First} vs {Second} — {string.Join(", ", Properties)}");
    }

    /// <summary>
    /// What the sweep found on 2.8.23 was fifteen pairs, each named — none approved, all RECORDED so
    /// the guard could fail on a sixteenth. 2.8.26 fixed every one of them (filter-chip, form-field,
    /// six tm-modal*, four tm-rte-*, three tm-timeline-*), so the list is EMPTY: the guard now fails
    /// on the first new pair that appears, with no grandfathered residue to hide behind.
    /// </summary>
    private static readonly string[] RecordedCollisions =
    [
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

        // The positive control moved: while a recorded pair existed, finding it proved the probe
        // could read. With the list empty, "found nothing" is the correct answer — the probe's
        // ability to read is proven synthetically by TheSweepSeesADuplicateAndIgnoresAConstrainedOne.
        Collisions().Should().BeEmpty(
            "patnáct zaznamenaných dvojic je opraveno — jakékoli další nalezení je nová kolize, " +
            "která patří do NoNewClassIsOwnedByTwoStylesheets: {0}",
            string.Join(" | ", Collisions().Select(collision => collision.ToString())));
    }

    /// <summary>
    /// Mutation, both directions: an invented duplicate must be seen, and a constrained duplicate
    /// (an ancestor or a state) must NOT be — those are legitimate and the whole library is built of them.
    /// </summary>
    [Fact]
    public void TheSweepSeesADuplicateAndIgnoresAConstrainedOne()
    {
        var declarations = NewDeclarations();
        Collect("a.css", ".tm-widget { color: red; }", declarations);
        Collect("b.css", ".tm-widget { color: blue; }", declarations);
        Pairs(declarations).Should().ContainSingle().Which.Properties.Should().Equal("color");

        declarations.Clear();
        Collect("a.css", ".tm-widget { color: red; }", declarations);
        Collect("b.css", ".tm-panel .tm-widget { color: blue; } .tm-widget:hover { color: green; }", declarations);
        Pairs(declarations).Should().BeEmpty(
            "pravidlo omezené předkem nebo stavem je záměr, ne remíza — knihovna je z nich postavená");

        declarations.Clear();
        Collect("a.css", ".tm-widget { color: red; }", declarations);
        Collect("b.css", ".tm-widget { margin: 0; }", declarations);
        Pairs(declarations).Should().BeEmpty("dva soubory, dvě různé vlastnosti — nikdo o nic nesoupeří");
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

    /// <summary>Per class name → per stylesheet → every rule instance (media context + its declarations).</summary>
    private static Dictionary<string, Dictionary<string, List<(string? Media, Dictionary<string, string> Props)>>>
        NewDeclarations() => new(StringComparer.Ordinal);

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

    /// <summary>Records every BARE class rule of one stylesheet, keeping the media condition it sits under.</summary>
    private static void Collect(
        string stylesheet,
        string css,
        Dictionary<string, Dictionary<string, List<(string? Media, Dictionary<string, string> Props)>>> declarations)
    {
        foreach (var rule in CssCascade.ParseRules(ThemeCss.StripComments(css)))
        {
            foreach (var part in ThemeCss.SelectorParts(rule.Selector))
            {
                if (!BareClass.IsMatch(part))
                {
                    continue;
                }

                var perFile = declarations.TryGetValue(part, out var existing)
                    ? existing
                    : declarations[part] = new(StringComparer.Ordinal);
                var instances = perFile.TryGetValue(stylesheet, out var owned)
                    ? owned
                    : perFile[stylesheet] = [];

                var properties = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var declaration in rule.Body.Split(';'))
                {
                    var separator = declaration.IndexOf(':', StringComparison.Ordinal);
                    if (separator > 0)
                    {
                        properties[declaration[..separator].Trim()] = ThemeCss.Normalise(declaration[(separator + 1)..]);
                    }
                }

                instances.Add((rule.MediaCondition, properties));
            }
        }
    }

    private static IReadOnlyList<Collision> Pairs(
        Dictionary<string, Dictionary<string, List<(string? Media, Dictionary<string, string> Props)>>> declarations)
    {
        var collisions = new List<Collision>();
        foreach (var (className, perFile) in declarations.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var files = perFile.Keys.Order(StringComparer.Ordinal).ToList();
            for (var i = 0; i < files.Count; i++)
            {
                for (var j = i + 1; j < files.Count; j++)
                {
                    var shared = perFile[files[i]]
                        .SelectMany(a => perFile[files[j]]
                            .Where(b => CssCascade.MediaCanOverlap(a.Media, b.Media))
                            .SelectMany(b => a.Props.Keys.Where(b.Props.ContainsKey)))
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToList();

                    if (shared.Count > 0)
                    {
                        collisions.Add(new Collision(className, files[i], files[j], shared));
                    }
                }
            }
        }

        return collisions;
    }
}

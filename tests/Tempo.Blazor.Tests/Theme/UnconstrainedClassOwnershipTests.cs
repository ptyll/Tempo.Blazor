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
/// button sweep covered it. It did not: fifteen further pairs were sitting in the same directory.
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

    private static readonly Regex RuleBlock =
        new(@"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}", RegexOptions.Compiled, Timeout);

    private static readonly Regex BareClass =
        new(@"^\.[a-zA-Z][\w-]*$", RegexOptions.Compiled, Timeout);

    /// <summary>One class owned by two stylesheets, with the properties they disagree about.</summary>
    private sealed record Collision(string Class, string First, string Second, IReadOnlyList<string> Properties)
    {
        public string Key => string.Create(CultureInfo.InvariantCulture, $"{Class} {First}|{Second}");

        public override string ToString() =>
            string.Create(CultureInfo.InvariantCulture,
                $"{Class}: {First} vs {Second} — {string.Join(", ", Properties)}");
    }

    /// <summary>
    /// What the sweep finds on 2.8.23, each pair named. None of these is approved; they are RECORDED so
    /// the guard can fail on a sixteenth. Their owner and the reason they were not fixed inside a patch
    /// release are in the plan's remaining tasks — fixing them all is a broad visual change across
    /// modal, scheduler, timeline, rich-text and form layout, which is not a thing to bundle into a
    /// release whose subject is three named defects.
    /// </summary>
    private static readonly string[] RecordedCollisions =
    [
        ".tm-filter-chip _data-table.css|_filter-chip.css",
        ".tm-form-field _form-field.css|_form-layout.css",
        ".tm-modal _dashboard.css|_modal.css",
        ".tm-modal-body _dashboard.css|_modal.css",
        ".tm-modal-close _dashboard.css|_modal.css",
        ".tm-modal-footer _dashboard.css|_modal.css",
        ".tm-modal-header _dashboard.css|_modal.css",
        ".tm-modal-overlay _dashboard.css|_modal.css",
        ".tm-rte-form-group _image-dialog.css|_link-dialog.css",
        ".tm-rte-mention-avatar _mention-autocomplete.css|_rich-text-editor.css",
        ".tm-rte-mention-dropdown _mention-autocomplete.css|_rich-text-editor.css",
        ".tm-rte-toolbar _editor-toolbar.css|_rich-text-editor.css",
        ".tm-timeline-author _activity-timeline.css|_timeline.css",
        ".tm-timeline-content _activity-timeline.css|_timeline.css",
        ".tm-timeline-empty _activity-timeline.css|_timeline.css",
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
        var files = Directory.EnumerateFiles(ThemeCss.CssPath("components"), "*.css").ToList();

        files.Should().HaveCountGreaterThanOrEqualTo(
            139,
            "components/ neslo nikdy pod 139 souborů; menší číslo znamená, že sonda čte jinou složku");
        Collisions().Should().NotBeEmpty(
            "sonda, která dnes nenajde nic, by byla zelená i kdyby neuměla číst — dokud existuje " +
            "zaznamenaná dvojice, musí ji vidět");
    }

    /// <summary>
    /// Mutation, both directions: an invented duplicate must be seen, and a constrained duplicate
    /// (an ancestor or a state) must NOT be — those are legitimate and the whole library is built of them.
    /// </summary>
    [Fact]
    public void TheSweepSeesADuplicateAndIgnoresAConstrainedOne()
    {
        var declarations = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>(StringComparer.Ordinal);
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

    private static IReadOnlyList<Collision> Collisions()
    {
        var declarations = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(ThemeCss.CssPath("components"), "*.css").Order(StringComparer.Ordinal))
        {
            Collect(Path.GetFileName(file), File.ReadAllText(file), declarations);
        }

        return Pairs(declarations);
    }

    /// <summary>Records every BARE class rule of one stylesheet, later declarations winning.</summary>
    private static void Collect(
        string stylesheet,
        string css,
        Dictionary<string, Dictionary<string, Dictionary<string, string>>> declarations)
    {
        foreach (Match rule in RuleBlock.Matches(ThemeCss.StripComments(css)))
        {
            foreach (var part in ThemeCss.SelectorParts(rule.Groups["selector"].Value))
            {
                if (!BareClass.IsMatch(part))
                {
                    continue;
                }

                var perFile = declarations.TryGetValue(part, out var existing)
                    ? existing
                    : declarations[part] = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
                var properties = perFile.TryGetValue(stylesheet, out var owned)
                    ? owned
                    : perFile[stylesheet] = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (var declaration in rule.Groups["body"].Value.Split(';'))
                {
                    var separator = declaration.IndexOf(':', StringComparison.Ordinal);
                    if (separator > 0)
                    {
                        properties[declaration[..separator].Trim()] = ThemeCss.Normalise(declaration[(separator + 1)..]);
                    }
                }
            }
        }
    }

    private static IReadOnlyList<Collision> Pairs(
        Dictionary<string, Dictionary<string, Dictionary<string, string>>> declarations)
    {
        var collisions = new List<Collision>();
        foreach (var (className, perFile) in declarations.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var files = perFile.Keys.Order(StringComparer.Ordinal).ToList();
            for (var i = 0; i < files.Count; i++)
            {
                for (var j = i + 1; j < files.Count; j++)
                {
                    var shared = perFile[files[i]].Keys
                        .Where(perFile[files[j]].ContainsKey)
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

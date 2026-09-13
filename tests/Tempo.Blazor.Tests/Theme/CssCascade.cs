using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Answers "which declaration of <paramref name="property"/> ACTUALLY applies to this element", read out
/// of the CSS that ships, with specificity and source order resolved the way a browser resolves them.
/// <para>
/// It exists because two separate defects of 2.8.21 were invisible to every markup assertion: the class
/// was on the element, the rule was in the file, and a LATER rule of equal specificity threw the value
/// away. <c>_pivot-table.css</c> redefines the GLOBAL <c>.tm-btn</c> with the shorthand
/// <c>border: 1px solid transparent</c> and the manifest imports it AFTER <c>_button.css</c>, so
/// <c>.tm-btn-outline-secondary { border-color: … }</c> — same specificity, earlier in the bundle —
/// never reaches the screen. The same shape hides the sorted column: <c>.tm-data-table th</c> is
/// (0,1,1) and <c>.tm-col-sorted-asc</c> is (0,1,0), so the header colour that announces the sort
/// loses to the generic header colour no matter where it sits in the file.
/// </para>
/// <para>
/// FAIL-CLOSED. A selector this model cannot express is NOT silently treated as "does not match": if its
/// rightmost compound could match the element under test, it is reported as UNMODELLED and the caller's
/// assertion fails. What a probe cannot read has to be counted as unmeasurable, never as fine.
/// </para>
/// <para>
/// This is the SECOND implementation of this shape in the suite; the first was the private
/// <c>DataTableCascade</c> inside <c>TmDataTableAlignmentTests</c>, which answered one property of one
/// file. That test now delegates here, so the extraction is proved by the guards that already existed
/// rather than by a fresh assertion written next to the new code.
/// </para>
/// </summary>
internal static class CssCascade
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The medium a resolution runs against: viewport width, the reduced-motion preference, and the
    /// output medium. A rule wrapped in <c>@media (max-width: 768px)</c> is not "a rule that is
    /// sometimes true" — it is a rule that does not exist at 1440 px, and a resolver that flattens
    /// it to unconditional reports a winner no user ever sees.
    /// </summary>
    /// <param name="WidthPx">
    /// Viewport width. Null means "the caller did not say", which makes every width-conditioned rule
    /// UNDECIDABLE rather than silently on or off — fail-closed, the way pseudo-classes already work.
    /// </param>
    public sealed record MediaContext(double? WidthPx = null, bool ReducedMotion = false, string Medium = "screen")
    {
        /// <summary>1440 px, screen, full motion — the plain desktop reading every caller got before media was modelled.</summary>
        public static readonly MediaContext Desktop = new(WidthPx: 1440);

        /// <summary>The default context of <see cref="Resolve"/>: the resting desktop reading.</summary>
        public static readonly MediaContext Default = Desktop;
    }

    /// <summary>Applies / does not apply / the model cannot tell — the third value is never "assume false".</summary>
    internal enum MediaVerdict
    {
        Applies,
        DoesNotApply,
        Undecidable,
    }

    /// <summary>
    /// One parsed rule: its selector list, its declaration body, and the media condition it sits
    /// under (<c>null</c> when unconditional, the joined conditions when nested).
    /// </summary>
    internal sealed record ParsedRule(string Selector, string Body, string? MediaCondition);

    /// <summary>
    /// One element of the modelled tree: its tag, the classes it carries, and — for the last element of
    /// a chain — an optional pseudo-element.
    /// </summary>
    /// <remarks>
    /// The pseudo-element is not decoration. <c>opacity</c> on a <c>::after</c> MULTIPLIES with the
    /// opacity of every ancestor, so a guard that cannot address the pseudo-element cannot answer
    /// "what is this glyph actually painted at" — which is the question the sort indicator turned on.
    /// </remarks>
    public sealed record Element(string Tag, IReadOnlySet<string> Classes, string? PseudoElement = null)
    {
        public Element(string tag, params string[] classes)
            : this(tag, new HashSet<string>(classes, StringComparer.Ordinal))
        {
        }

        public Element With(string pseudoElement) => this with { PseudoElement = pseudoElement };
    }

    /// <summary>The winning declaration, plus everything the model could not read.</summary>
    /// <param name="Value">The value that applies, or null when no modelled rule declares the property.</param>
    /// <param name="Source">The selector the winning value came from — named so a failure says WHY.</param>
    /// <param name="Unmodelled">
    /// Selectors that declare the property and could match the element, but sit outside the model.
    /// Non-empty means the answer is NOT trustworthy.
    /// </param>
    public sealed record Winner(string? Value, string? Source, IReadOnlyList<string> Unmodelled);

    /// <summary>
    /// Longhands that a shorthand also sets. Only the ones this suite measures are listed: a shorthand
    /// map that guesses is a fail-open dressed as completeness.
    /// </summary>
    private static readonly Dictionary<string, (string Shorthand, Func<string, string?> Extract)> ShorthandOf =
        new(StringComparer.Ordinal)
        {
            ["border-color"] = ("border", BorderColourFromShorthand),
            ["border-width"] = ("border", value => value.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()),
        };

    /// <summary>
    /// The value of <paramref name="property"/> that applies to the last element of
    /// <paramref name="chain"/> (outermost ancestor first), resolved over <paramref name="css"/> in
    /// source order.
    /// </summary>
    /// <param name="activeStates">
    /// Pseudo-classes to treat as true — <c>":hover"</c>, <c>":focus-visible"</c>. Everything not listed
    /// is false, which is what "resting state" means. Passing states explicitly is what keeps a hover
    /// rule from being mistaken for the resting colour, which is exactly how the sort indicator was
    /// mis-measured in Fáze 14.
    /// </param>
    /// <param name="media">
    /// The medium the resolution runs against; defaults to <see cref="MediaContext.Desktop"/>. A rule
    /// inside <c>@media (max-width: 768px)</c> does not apply there — before this parameter existed the
    /// parser could not even SEE the condition, so a mobile-only override could silently win a desktop
    /// measurement. A condition the model cannot decide is reported through <c>Unmodelled</c>, never
    /// assumed false.
    /// </param>
    public static Winner Resolve(
        string css,
        IReadOnlyList<Element> chain,
        string property,
        IReadOnlySet<string>? activeStates = null,
        MediaContext? media = null)
    {
        activeStates ??= new HashSet<string>(StringComparer.Ordinal);
        media ??= MediaContext.Default;

        var target = ShorthandOf.TryGetValue(property, out var mapping) ? mapping : default;
        var unmodelled = new List<string>();

        string? winner = null;
        string? source = null;
        var best = (Id: -1, Class: -1, Type: -1);

        foreach (var rule in ParseRules(ThemeCss.StripComments(css)))
        {
            var mediaVerdict = rule.MediaCondition is null
                ? MediaVerdict.Applies
                : EvaluateMedia(rule.MediaCondition, media);
            if (mediaVerdict == MediaVerdict.DoesNotApply)
            {
                continue;
            }

            var declared = DeclarationValue(rule.Body, property);
            if (declared is null && target.Shorthand is not null)
            {
                var shorthand = DeclarationValue(rule.Body, target.Shorthand);
                declared = shorthand is null ? null : target.Extract(shorthand);
            }

            if (declared is null)
            {
                continue;
            }

            foreach (var part in ThemeCss.SelectorParts(rule.Selector))
            {
                var verdict = Match(part, chain, activeStates);
                if (verdict.Unmodelled
                    || (mediaVerdict == MediaVerdict.Undecidable && verdict.Specificity is not null))
                {
                    // A matching selector under an undecidable condition is a MAYBE-winner — the
                    // answer is untrustworthy, and "probably fine" is how a flattened probe ships a hole.
                    unmodelled.Add(
                        rule.MediaCondition is null ? part : $"{part}  [@media {rule.MediaCondition}]");
                    continue;
                }

                if (verdict.Specificity is null)
                {
                    continue;
                }

                // Source order breaks a tie, and the loop walks the file top to bottom — so ">=".
                if (verdict.Specificity.Value.CompareTo(best) >= 0)
                {
                    best = verdict.Specificity.Value;
                    winner = declared;
                    source = part;
                }
            }
        }

        return new Winner(winner, source, unmodelled);
    }

    /// <summary>
    /// <see cref="Resolve"/> with the fail-closed check already asserted, for the common case where the
    /// caller wants a value and not a report.
    /// </summary>
    public static string Winning(
        string css,
        IReadOnlyList<Element> chain,
        string property,
        IReadOnlySet<string>? activeStates = null,
        MediaContext? media = null)
    {
        var resolved = Resolve(css, chain, property, activeStates, media);

        resolved.Unmodelled.Should().BeEmpty(
            "selektor, který sonda neumí přečíst, je NEMĚŘITELNÝ — nesmí se počítat mezi „nematchuje“");
        resolved.Value.Should().NotBeNull(
            "CSS musí pro tenhle prvek deklarovat {0}, jinak strážce netvrdí nic", property);

        return resolved.Value!;
    }

    /// <summary>The declared value of one longhand in a declaration body, or null.</summary>
    private static string? DeclarationValue(string body, string property)
    {
        string? found = null;
        foreach (var declaration in body.Split(';'))
        {
            var separator = declaration.IndexOf(':', StringComparison.Ordinal);
            if (separator < 0)
            {
                continue;
            }

            if (string.Equals(declaration[..separator].Trim(), property, StringComparison.Ordinal))
            {
                // A body may declare the same longhand twice; the last one wins, as in a browser.
                found = ThemeCss.Normalise(declaration[(separator + 1)..]);
            }
        }

        return found;
    }

    /// <summary>
    /// The colour component of a <c>border</c> shorthand. The shorthand ALWAYS sets border-color, even
    /// when it names only a width and a style — the omitted component resets to its initial value — so
    /// a missing colour is reported as <c>currentcolor</c>, not as "the shorthand said nothing".
    /// </summary>
    private static string BorderColourFromShorthand(string value)
    {
        var tokens = SplitTopLevel(value);
        foreach (var token in tokens)
        {
            if (IsBorderWidth(token) || IsBorderStyle(token))
            {
                continue;
            }

            return token;
        }

        return "currentcolor";
    }

    private static bool IsBorderWidth(string token) =>
        token is "thin" or "medium" or "thick"
        || Regex.IsMatch(token, @"^[\d.]+(px|rem|em|pt|%)?$", RegexOptions.None, Timeout)
        || token.StartsWith("var(--tm-border-width", StringComparison.Ordinal)
        || token.StartsWith("calc(", StringComparison.Ordinal);

    private static bool IsBorderStyle(string token) =>
        token is "none" or "hidden" or "dotted" or "dashed" or "solid" or "double"
            or "groove" or "ridge" or "inset" or "outset";

    /// <summary>Space-separated tokens of a value, with <c>var(…)</c> and <c>calc(…)</c> kept whole.</summary>
    private static List<string> SplitTopLevel(string value)
    {
        var tokens = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '(')
            {
                depth++;
            }
            else if (value[i] == ')')
            {
                depth--;
            }
            else if (value[i] == ' ' && depth == 0)
            {
                if (i > start)
                {
                    tokens.Add(value[start..i]);
                }

                start = i + 1;
            }
        }

        if (start < value.Length)
        {
            tokens.Add(value[start..]);
        }

        return tokens;
    }

    private readonly record struct Verdict((int Id, int Class, int Type)? Specificity, bool Unmodelled)
    {
        public static Verdict NoMatch => new(null, false);

        public static Verdict Unreadable => new(null, true);
    }

    /// <summary>
    /// Whether a selector matches the chain, and at what specificity. Descendant combinators of
    /// type/class compounds are modelled, plus pseudo-classes that the caller declared active.
    /// </summary>
    private static Verdict Match(
        string selector,
        IReadOnlyList<Element> chain,
        IReadOnlySet<string> activeStates)
    {
        if (selector.Length == 0)
        {
            return Verdict.NoMatch;
        }

        // A pseudo-ELEMENT is a box of its own, with its own colour and its own opacity. It matches only
        // when the caller asked about that box by name; asking about the element itself must not pick up
        // its ::after, and vice versa.
        var pseudoElement = PseudoElementOf(selector);
        if (!string.Equals(pseudoElement, chain[^1].PseudoElement, StringComparison.Ordinal))
        {
            return Verdict.NoMatch;
        }

        if (pseudoElement is not null)
        {
            selector = selector[..selector.IndexOf("::", StringComparison.Ordinal)];
        }

        var compounds = selector.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var rightmost = compounds[^1];

        // An id or an attribute ON THE SUBJECT is a decided non-match: the elements this model describes
        // carry a tag and classes and nothing else, so `#foo` and `td[colspan]` cannot select them.
        if (rightmost.IndexOfAny(['[', '#']) >= 0)
        {
            return Verdict.NoMatch;
        }

        // Anywhere ELSE in the selector the same syntax is NOT decidable, and treating it as a
        // non-match is how this reader shipped a hole. `[data-theme="dark"] .tm-sort-icon` names an
        // ANCESTOR the model does not represent — the theme is applied by the caller through the token
        // graph, not by an element in the chain — so whether it matches depends on something this
        // model cannot see. The bundle already uses that idiom twice, and a mutation adding a third
        // stayed GREEN while the docstring promised fail-closed. It is now reported as unreadable
        // whenever the subject could be this element.
        if (selector.IndexOfAny(['>', '+', '~', '[', '#']) >= 0)
        {
            return CouldBeSubject(rightmost, chain[^1], activeStates) ? Verdict.Unreadable : Verdict.NoMatch;
        }

        if (!CompoundMatches(rightmost, chain[^1], activeStates))
        {
            return Verdict.NoMatch;
        }

        var ancestorIndex = chain.Count - 2;
        for (var i = compounds.Length - 2; i >= 0; i--)
        {
            while (ancestorIndex >= 0 && !CompoundMatches(compounds[i], chain[ancestorIndex], activeStates))
            {
                ancestorIndex--;
            }

            if (ancestorIndex < 0)
            {
                return Verdict.NoMatch;
            }

            ancestorIndex--;
        }

        var classCount = compounds.Sum(compound => compound.Count(character => character == '.'))
                         + compounds.Sum(CountPseudoClasses);
        var typeCount = compounds.Count(compound => !compound.StartsWith('.') && !compound.StartsWith(':'));
        return new Verdict((0, classCount, typeCount), Unmodelled: false);
    }

    private static int CountPseudoClasses(string compound) =>
        compound.Count(character => character == ':');

    /// <summary>The <c>::name</c> of a selector, or null when it addresses an element rather than a box.</summary>
    private static string? PseudoElementOf(string selector)
    {
        var marker = selector.IndexOf("::", StringComparison.Ordinal);
        if (marker < 0)
        {
            return null;
        }

        var name = selector[(marker + 2)..];
        var end = name.IndexOfAny([' ', ':', '.', '[', '(']);
        return end < 0 ? name : name[..end];
    }

    /// <summary>
    /// The opacity a box is ACTUALLY painted at: the product of the winning <c>opacity</c> of every
    /// element from the root of the chain down to the subject, pseudo-element included.
    /// </summary>
    /// <remarks>
    /// Nested opacity multiplies — that is the whole reason <c>opacity: 1</c> on
    /// <c>.tm-sort-icon.tm-sort-asc::after</c> did nothing inside a span at <c>opacity: .4</c>. A guard
    /// that reads only the rules naming the element itself is blind to an ancestor: adding
    /// <c>.tm-data-table thead th { opacity: .4 }</c> restores the original defect one level up, and
    /// such a mutation stayed GREEN until this existed.
    /// </remarks>
    public static double EffectiveOpacity(
        string css,
        IReadOnlyList<Element> chain,
        IReadOnlySet<string>? activeStates = null,
        MediaContext? media = null)
    {
        // The BOXES an opacity can sit on, outermost first: every ancestor, then the element itself, and
        // only then its pseudo-element. Walking the chain as given would skip the element's own opacity
        // whenever the subject is a ::after, which is precisely the rule that started all of this.
        var boxes = new List<IReadOnlyList<Element>>();
        var bare = chain[^1] with { PseudoElement = null };
        for (var depth = 1; depth <= chain.Count; depth++)
        {
            var prefix = chain.Take(depth).ToList();
            prefix[^1] = depth == chain.Count ? bare : prefix[^1] with { PseudoElement = null };
            boxes.Add(prefix);
        }

        if (chain[^1].PseudoElement is not null)
        {
            boxes.Add([.. chain.Take(chain.Count - 1), chain[^1]]);
        }

        var product = 1.0;
        foreach (var box in boxes)
        {
            var resolved = Resolve(css, box, "opacity", activeStates, media);

            resolved.Unmodelled.Should().BeEmpty(
                "průhlednost prvku, kterou sonda neumí přečíst, je NEMĚŘITELNÁ, ne 1");

            if (resolved.Value is null)
            {
                continue;
            }

            double.TryParse(resolved.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                .Should().BeTrue("opacity '{0}' z pravidla '{1}' musí být číslo", resolved.Value, resolved.Source);
            product *= value;
        }

        return product;
    }

    /// <summary>
    /// Whether the subject of a selector the model cannot express could still be this element. A bare
    /// <c>*</c> can, so an empty remainder answers YES — the opposite of what "nothing left to compare"
    /// would suggest, and the difference between reporting an unknown and hiding one.
    /// </summary>
    private static bool CouldBeSubject(string compound, Element element, IReadOnlySet<string> activeStates)
    {
        var stripped = compound.Replace("*", string.Empty, StringComparison.Ordinal);
        return stripped.Length == 0 || CompoundMatches(stripped, element, activeStates);
    }

    private static bool CompoundMatches(string compound, Element element, IReadOnlySet<string> activeStates)
    {
        var pseudoStart = compound.IndexOf(':', StringComparison.Ordinal);
        var pseudos = new List<string>();
        if (pseudoStart >= 0)
        {
            foreach (var pseudo in compound[pseudoStart..].Split(':', StringSplitOptions.RemoveEmptyEntries))
            {
                pseudos.Add(":" + pseudo);
            }

            compound = compound[..pseudoStart];
        }

        if (pseudos.Exists(pseudo => !activeStates.Contains(pseudo)))
        {
            return false;
        }

        var parts = compound.Split('.');
        if (parts[0].Length > 0 && !parts[0].Equals(element.Tag, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return parts.Skip(1).All(element.Classes.Contains);
    }

    // ── Structural parsing ────────────────────────────────────────
    // A regex like [^{}]+\{[^{}]*\} flattens @media: it cannot see that a rule sits inside a
    // condition, so a mobile-only override is read as unconditional — and wins desktop
    // measurements it should never reach. The walk below tracks the at-rule stack instead.

    /// <summary>
    /// Every style rule of a stylesheet in source order, with the media condition it sits under.
    /// <c>@media</c> blocks are entered and their condition recorded; <c>@supports</c> blocks are
    /// entered and recorded as undecidable; <c>@keyframes</c>, <c>@font-face</c> and other at-rules
    /// whose inner blocks are not selectors are skipped entirely.
    /// </summary>
    internal static IReadOnlyList<ParsedRule> ParseRules(string css)
    {
        var rules = new List<ParsedRule>();
        ParseInto(css, media: null, rules);
        return rules;
    }

    private static void ParseInto(string css, string? media, List<ParsedRule> rules)
    {
        var cursor = 0;
        while (cursor < css.Length)
        {
            var open = css.IndexOf('{', cursor);
            if (open < 0)
            {
                return;
            }

            var header = css[cursor..open].Trim();
            var close = MatchingBrace(css, open);
            var body = css[(open + 1)..close];

            if (header.Length == 0)
            {
                // A stray "{…}" with no header is not a rule; skip it rather than invent a selector.
            }
            else if (header.StartsWith('@'))
            {
                if (header.StartsWith("@media", StringComparison.OrdinalIgnoreCase))
                {
                    var condition = header["@media".Length..].Trim();
                    ParseInto(body, media is null ? condition : media + " and " + condition, rules);
                }
                else if (header.StartsWith("@supports", StringComparison.OrdinalIgnoreCase))
                {
                    // Inner blocks ARE selectors, gated on a feature the model cannot evaluate —
                    // recorded as their own undecidable condition rather than dropped.
                    ParseInto(body, media is null ? header : media + " and " + header, rules);
                }

                // @keyframes, @font-face, @page, @charset, …: inner blocks are not element rules.
            }
            else
            {
                rules.Add(new ParsedRule(header, body, media));
            }

            cursor = close + 1;
        }
    }

    private static int MatchingBrace(string css, int open)
    {
        var depth = 0;
        for (var i = open; i < css.Length; i++)
        {
            if (css[i] == '{')
            {
                depth++;
            }
            else if (css[i] == '}' && --depth == 0)
            {
                return i;
            }
        }

        return css.Length; // unbalanced input — consume the rest, the selector check will fail loudly
    }

    // ── Media conditions ──────────────────────────────────────────

    /// <summary>The structured form of one media alternative: ranges and prefs a context answers.</summary>
    private sealed record MediaQuery(
        double MinWidth,
        double MaxWidth,
        string? Medium,
        bool? ReducedMotion,
        bool Undecidable)
    {
        public static readonly MediaQuery Any = new(0, double.MaxValue, null, null, false);
    }

    /// <summary>
    /// Whether a media condition holds in <paramref name="context"/>. Any clause the model cannot
    /// parse or the context cannot answer makes the whole condition UNDECIDABLE — never assumed
    /// false, so a rule behind it is reported unmodelled rather than silently skipped.
    /// </summary>
    internal static MediaVerdict EvaluateMedia(string condition, MediaContext context)
    {
        var alternatives = ParseMedia(condition);
        if (alternatives.Count == 0)
        {
            return MediaVerdict.Undecidable;
        }

        var sawUndecidable = false;
        foreach (var alternative in alternatives)
        {
            var verdict = EvaluateAlternative(alternative, context);
            if (verdict == MediaVerdict.Applies)
            {
                return MediaVerdict.Applies;
            }

            sawUndecidable |= verdict == MediaVerdict.Undecidable;
        }

        return sawUndecidable ? MediaVerdict.Undecidable : MediaVerdict.DoesNotApply;
    }

    /// <summary>
    /// Whether two media conditions can hold AT THE SAME TIME — the question the class-ownership
    /// sweep asks when two bare-class rules disagree. Conditions that cannot be proven disjoint
    /// (an unparseable clause, an unmodelled feature) are reported overlapping: a missed collision
    /// is the failure this sweep exists to prevent.
    /// </summary>
    internal static bool MediaCanOverlap(string? first, string? second)
    {
        if (first is null || second is null)
        {
            return true; // an unconditional rule competes with everything
        }

        var a = ParseMedia(first);
        var b = ParseMedia(second);
        if (a.Any(q => q.Undecidable) || b.Any(q => q.Undecidable) || a.Count == 0 || b.Count == 0)
        {
            return true;
        }

        return a.Any(qa => b.Any(qb => AlternativesOverlap(qa, qb)));
    }

    private static bool AlternativesOverlap(MediaQuery a, MediaQuery b)
    {
        var mediumsCompatible =
            a.Medium is null || b.Medium is null || string.Equals(a.Medium, b.Medium, StringComparison.Ordinal);
        var widthsOverlap = Math.Max(a.MinWidth, b.MinWidth) <= Math.Min(a.MaxWidth, b.MaxWidth);
        var motionCompatible = a.ReducedMotion is null || b.ReducedMotion is null || a.ReducedMotion == b.ReducedMotion;
        return mediumsCompatible && widthsOverlap && motionCompatible;
    }

    private static MediaVerdict EvaluateAlternative(MediaQuery query, MediaContext context)
    {
        if (query.Undecidable)
        {
            return MediaVerdict.Undecidable;
        }

        if (query.Medium is not null && !string.Equals(query.Medium, context.Medium, StringComparison.Ordinal))
        {
            return MediaVerdict.DoesNotApply;
        }

        if (query.MinWidth > 0 || query.MaxWidth < double.MaxValue)
        {
            if (context.WidthPx is null)
            {
                return MediaVerdict.Undecidable;
            }

            if (context.WidthPx < query.MinWidth || context.WidthPx > query.MaxWidth)
            {
                return MediaVerdict.DoesNotApply;
            }
        }

        if (query.ReducedMotion is not null && query.ReducedMotion != context.ReducedMotion)
        {
            return MediaVerdict.DoesNotApply;
        }

        return MediaVerdict.Applies;
    }

    /// <summary>
    /// Parses a media condition into one <see cref="MediaQuery"/> per comma-separated alternative.
    /// Modelled clauses: <c>screen</c>/<c>print</c>/<c>all</c>, <c>(min-width: Npx)</c>,
    /// <c>(max-width: Npx)</c>, <c>(prefers-reduced-motion: …)</c>, joined by <c>and</c>. Anything
    /// else — <c>not</c>, range syntax, unmodelled features — yields an Undecidable alternative.
    /// </summary>
    private static IReadOnlyList<MediaQuery> ParseMedia(string condition)
    {
        var queries = new List<MediaQuery>();
        foreach (var raw in condition.Split(','))
        {
            queries.Add(ParseAlternative(raw.Trim()));
        }

        return queries;
    }

    private static MediaQuery ParseAlternative(string alternative)
    {
        if (alternative.StartsWith("not ", StringComparison.OrdinalIgnoreCase))
        {
            return MediaQuery.Any with { Undecidable = true }; // negation is not modelled
        }

        var query = MediaQuery.Any;
        foreach (var rawClause in Regex.Split(
                     alternative, @"\band\b", RegexOptions.IgnoreCase, Timeout))
        {
            var clause = rawClause.Trim();
            if (clause.StartsWith("only ", StringComparison.OrdinalIgnoreCase))
            {
                clause = clause["only ".Length..].Trim();
            }

            if (clause.Length == 0)
            {
                continue;
            }

            if (!clause.StartsWith('('))
            {
                // A bare medium name: "screen", "print", "all". Anything else is not a medium this
                // model can place, which is undecidable rather than a silent no-match.
                query = clause.ToLowerInvariant() switch
                {
                    "all" => query,
                    "screen" or "print" => query with { Medium = clause.ToLowerInvariant() },
                    _ => query with { Undecidable = true },
                };
                continue;
            }

            var inner = clause.Trim('(', ')');
            var colon = inner.IndexOf(':', StringComparison.Ordinal);
            var feature = (colon < 0 ? inner : inner[..colon]).Trim().ToLowerInvariant();
            var value = colon < 0 ? string.Empty : inner[(colon + 1)..].Trim();

            switch (feature)
            {
                case "max-width":
                case "min-width":
                    if (!double.TryParse(value.TrimEnd('p', 'x').Trim(), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out var px)
                        || !value.EndsWith("px", StringComparison.Ordinal))
                    {
                        query = query with { Undecidable = true };
                    }
                    else if (feature == "max-width")
                    {
                        query = query with { MaxWidth = Math.Min(query.MaxWidth, px) };
                    }
                    else
                    {
                        query = query with { MinWidth = Math.Max(query.MinWidth, px) };
                    }

                    break;
                case "prefers-reduced-motion":
                    query = value.Equals("reduce", StringComparison.OrdinalIgnoreCase)
                        ? query with { ReducedMotion = true }
                        : value.Equals("no-preference", StringComparison.OrdinalIgnoreCase)
                            ? query with { ReducedMotion = false }
                            : query with { Undecidable = true };
                    break;
                default:
                    query = query with { Undecidable = true };
                    break;
            }
        }

        return query;
    }
}

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
    /// One parsed rule: its selector list, its declaration body, the media condition it sits under
    /// (<c>null</c> when unconditional, the joined conditions when nested), and the cascade layer
    /// it was declared in (<c>null</c> when unlayered — the rank that always wins).
    /// </summary>
    /// <param name="LayerRank">
    /// Position of the rule's <c>@layer</c> in the stylesheet's layer order (0 = first declared).
    /// Unlayered rules out-rank EVERY layered rule regardless of specificity, so the resolver
    /// compares this column first.
    /// </param>
    internal sealed record ParsedRule(
        string Selector,
        string Body,
        string? MediaCondition,
        int? LayerRank = null,
        string? LayerName = null);

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
    /// <param name="importResolver">
    /// Maps an <c>@import</c> target (<c>"tokens.css"</c>, <c>url(…)</c>) to the imported text, so a
    /// caller can feed the manifest and get the same rules the bundle flattens. An import the
    /// resolver cannot read — or any import when no resolver is given — is reported through
    /// <c>Unmodelled</c>, never silently dropped.
    /// </param>
    public static Winner Resolve(
        string css,
        IReadOnlyList<Element> chain,
        string property,
        IReadOnlySet<string>? activeStates = null,
        MediaContext? media = null,
        Func<string, string?>? importResolver = null)
    {
        activeStates ??= new HashSet<string>(StringComparer.Ordinal);
        media ??= MediaContext.Default;

        var target = ShorthandOf.TryGetValue(property, out var mapping) ? mapping : default;
        var unmodelled = new List<string>();

        var outcome = ParseStylesheet(ThemeCss.StripComments(css), importResolver);
        unmodelled.AddRange(outcome.Unmodelled);

        string? winner = null;
        string? source = null;
        // Cascade order, compared left to right: an UNLAYERED rule out-ranks every layered rule
        // (int.MaxValue wins); between two layered rules the LATER-declared layer wins regardless
        // of specificity — that is what @layer exists to say. Specificity and source order only
        // ever arbitrate inside one layer.
        var best = (Layer: -1, Id: -1, Class: -1, Type: -1);

        foreach (var rule in outcome.Rules)
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

            var layerKey = rule.LayerRank ?? int.MaxValue;
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
                var candidate = (layerKey, verdict.Specificity.Value.Id,
                    verdict.Specificity.Value.Class, verdict.Specificity.Value.Type);
                if (candidate.CompareTo(best) >= 0)
                {
                    best = candidate;
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
        MediaContext? media = null,
        Func<string, string?>? importResolver = null)
    {
        var resolved = Resolve(css, chain, property, activeStates, media, importResolver);

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
        MediaContext? media = null,
        Func<string, string?>? importResolver = null)
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
            var resolved = Resolve(css, box, "opacity", activeStates, media, importResolver);

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
    //
    // Two more failure shapes the flat "{…}" walk shipped, both fail-OPEN:
    //  • a STATEMENT-form at-rule (@import "x";, @layer a,b;, @charset) carries no block of its
    //    own, so the text between ';' and the next '{' became the next rule's "header" — and the
    //    real header following it was swallowed into a skipped @-header. One stray statement ate
    //    a whole style rule, silently.
    //  • a rule body containing NESTED blocks (@media or a nested selector inside .a { … }) was
    //    read as one declaration run — the nested override could neither apply nor be reported.
    // The scope walker below splits ';'-terminated statements (paren/quote aware) before reading
    // a header, enters gated at-rules recursively, expands nested selectors against their parent,
    // and keeps the emission order of declarations and nested blocks exactly where the text put
    // them — because source order IS the cascade's last tie-breaker.

    /// <summary>What a stylesheet parse produced: the rules it could read, and the constructs it could not.</summary>
    internal sealed record ParseOutcome(IReadOnlyList<ParsedRule> Rules, IReadOnlyList<string> Unmodelled);

    /// <summary>
    /// Every style rule of a stylesheet in source order, with the media condition and cascade
    /// layer it sits under. <c>@media</c> blocks are entered and their condition recorded;
    /// <c>@supports</c> and <c>@container</c> blocks are entered and recorded as undecidable
    /// conditions; <c>@layer</c> blocks register layer order and tag their rules;
    /// <c>@keyframes</c>, <c>@font-face</c> and other at-rules whose inner blocks are not element
    /// rules are skipped entirely.
    /// </summary>
    internal static IReadOnlyList<ParsedRule> ParseRules(string css)
        => ParseStylesheet(css).Rules;

    /// <summary>
    /// <see cref="ParseRules"/> plus the constructs the parse could not read: unresolved
    /// <c>@import</c> targets (or every import when no <paramref name="importResolver"/> is
    /// given). A probe that cannot see behind an import must say so — skipping it is how a
    /// manifest-wide hole gets reported as "no collisions found".
    /// </summary>
    internal static ParseOutcome ParseStylesheet(string css, Func<string, string?>? importResolver = null)
    {
        var state = new ParseState { ImportResolver = importResolver };
        ParseScope(css, media: null, layerRank: null, layerName: null, parentSelector: null, state);
        return new ParseOutcome(state.Rules, state.Unmodelled);
    }

    /// <summary>Mutable context shared by one stylesheet's recursive parse.</summary>
    private sealed class ParseState
    {
        public List<ParsedRule> Rules { get; } = [];
        public List<string> Unmodelled { get; } = [];
        public Dictionary<string, int> LayerOrder { get; } = new(StringComparer.Ordinal);
        public HashSet<string> ResolvedImports { get; } = new(StringComparer.Ordinal);
        public Func<string, string?>? ImportResolver { get; init; }
        public int NextLayerRank;
    }

    /// <summary>
    /// Walks one scope of stylesheet text. A scope is either the top level
    /// (<paramref name="parentSelector"/> null — <c>;</c>-terminated chunks are at-rule statements)
    /// or the body of a style rule (<paramref name="parentSelector"/> set — the same chunks are
    /// declarations). Nested <c>{…}</c> blocks are entered recursively: gated at-rules re-gate
    /// and keep the selector context, nested selectors expand against their parent.
    /// </summary>
    private static void ParseScope(
        string css,
        string? media,
        int? layerRank,
        string? layerName,
        string? parentSelector,
        ParseState state)
    {
        var cursor = 0;
        var declarations = new System.Text.StringBuilder();

        void FlushDeclarations()
        {
            if (parentSelector is not null && declarations.Length > 0)
            {
                state.Rules.Add(new ParsedRule(
                    parentSelector, declarations.ToString(), media, layerRank, layerName));
            }

            declarations.Clear();
        }

        while (cursor < css.Length)
        {
            var boundary = NextBoundary(css, cursor);
            if (boundary.Kind == BoundaryKind.End)
            {
                // The chunk after the last boundary: the final declaration of a rule body, which
                // CSS does not require to end in ';'. Dropping it would read "{ color: red }" as
                // an empty rule — the same class of hole as the swallowed statement above.
                var tail = css[cursor..].Trim();
                if (parentSelector is not null && tail.Length > 0)
                {
                    if (tail.StartsWith('@'))
                    {
                        state.Unmodelled.Add($"{parentSelector} — trailing at-rule '{tail}' inside a rule body");
                    }
                    else
                    {
                        if (declarations.Length > 0)
                        {
                            declarations.Append(';');
                        }

                        declarations.Append(tail);
                    }
                }

                break;
            }

            if (boundary.Kind == BoundaryKind.Statement)
            {
                var statement = css[cursor..boundary.Index].Trim();
                if (parentSelector is null)
                {
                    ProcessStatement(statement, media, layerName, state);
                }
                else if (statement.Length > 0 && !statement.StartsWith('@'))
                {
                    if (declarations.Length > 0)
                    {
                        declarations.Append(';');
                    }

                    declarations.Append(statement);
                }
                else if (statement.Length > 0)
                {
                    // A '@…;' statement inside a rule body is outside the model — reported, not
                    // dropped into a fabricated declaration.
                    state.Unmodelled.Add($"{parentSelector} — '{statement};' inside a rule body");
                }

                cursor = boundary.Index + 1;
                continue;
            }

            // An opening brace: the text since the last boundary is this block's header.
            var open = boundary.Index;
            var header = css[cursor..open].Trim();
            var close = MatchingBrace(css, open);
            var body = css[(open + 1)..close];

            if (header.Length > 0)
            {
                // A nested block interrupts the enclosing rule's declaration run — emit the run
                // first so emission order stays textual (order IS the last cascade tie-breaker).
                FlushDeclarations();
            }

            if (header.Length == 0)
            {
                // A stray "{…}" with no header is not a rule; skip it rather than invent a selector.
            }
            else if (header.StartsWith('@'))
            {
                if (header.StartsWith("@media", StringComparison.OrdinalIgnoreCase))
                {
                    var condition = header["@media".Length..].Trim();
                    ParseScope(body, JoinMedia(media, condition), layerRank, layerName,
                        parentSelector, state);
                }
                else if (header.StartsWith("@supports", StringComparison.OrdinalIgnoreCase)
                         || header.StartsWith("@container", StringComparison.OrdinalIgnoreCase))
                {
                    // Inner blocks ARE selectors, gated on a feature/size the model cannot
                    // evaluate — recorded as their own undecidable condition rather than dropped.
                    ParseScope(body, JoinMedia(media, header), layerRank, layerName,
                        parentSelector, state);
                }
                else if (header.StartsWith("@layer", StringComparison.OrdinalIgnoreCase))
                {
                    var (rank, fullName) = EnterLayer(header["@layer".Length..].Trim(), layerName, state);
                    ParseScope(body, media, rank, fullName, parentSelector, state);
                }

                // @keyframes, @font-face, @page, @charset, …: inner blocks are not element rules.
            }
            else
            {
                var effective = parentSelector is null
                    ? header
                    : ExpandNestedSelector(parentSelector, header);
                ParseScope(body, media, layerRank, layerName, effective, state);
            }

            cursor = close + 1;
        }

        FlushDeclarations();
    }

    private enum BoundaryKind
    {
        Statement,
        OpenBrace,
        End,
    }

    /// <summary>
    /// The next structural boundary of a scope: a top-level <c>;</c> (statement end), a top-level
    /// <c>{</c> (block start), or the end of text. Parentheses and quoted strings are skipped —
    /// a <c>;</c> inside <c>url("a;b")</c> or a <c>{</c> inside <c>content: "{"</c> is data, not
    /// structure, and counting it is how the flat walk mis-sliced bodies.
    /// </summary>
    private static (BoundaryKind Kind, int Index) NextBoundary(string css, int start)
    {
        var depth = 0;
        var quote = '\0';
        for (var i = start; i < css.Length; i++)
        {
            var c = css[i];
            if (quote != '\0')
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            switch (c)
            {
                case '"' or '\'':
                    quote = c;
                    break;
                case '(':
                    depth++;
                    break;
                case ')':
                    depth = Math.Max(0, depth - 1);
                    break;
                case ';' when depth == 0:
                    return (BoundaryKind.Statement, i);
                case '{' when depth == 0:
                    return (BoundaryKind.OpenBrace, i);
            }
        }

        return (BoundaryKind.End, css.Length);
    }

    /// <summary>
    /// A top-level <c>;</c>-terminated statement: <c>@import</c> (resolved through the caller's
    /// resolver, or reported unmodelled when unreadable), a <c>@layer</c> order statement, or a
    /// statement the cascade does not carry (<c>@charset</c>, <c>@namespace</c>).
    /// </summary>
    private static void ProcessStatement(string statement, string? media, string? layerName, ParseState state)
    {
        if (statement.StartsWith("@import", StringComparison.OrdinalIgnoreCase))
        {
            ProcessImport(statement, media, state);
            return;
        }

        if (statement.StartsWith("@layer", StringComparison.OrdinalIgnoreCase))
        {
            // The order statement: @layer a, b, c; — registers positions without declaring rules.
            foreach (var name in statement["@layer".Length..]
                         .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                RegisterLayer(name, layerName, state);
            }
        }
    }

    /// <summary>
    /// <c>@import &lt;target&gt; [layer|layer(name)] [conditions];</c> — the imported text is parsed
    /// at this position (import order is cascade order), under the import's own layer clause and
    /// media tail when present. An import the resolver cannot read is reported, never skipped.
    /// </summary>
    private static void ProcessImport(string statement, string? media, ParseState state)
    {
        var rest = statement["@import".Length..].Trim();
        var (target, tail) = ImportTarget(rest);
        if (target is null)
        {
            state.Unmodelled.Add($"{statement} — @import target the model cannot read");
            return;
        }

        var (layerName, importMedia) = ImportClauses(tail);
        var mediaCondition = importMedia is null ? media : JoinMedia(media, importMedia);

        if (state.ImportResolver is null)
        {
            state.Unmodelled.Add($"{statement} — @import with no resolver cannot be seen through");
            return;
        }

        if (!state.ResolvedImports.Add(target))
        {
            return; // already inlined once — an import cycle would otherwise recurse forever
        }

        var imported = state.ImportResolver(target);
        if (imported is null)
        {
            state.Unmodelled.Add($"{statement} — resolver found no stylesheet for '{target}'");
            return;
        }

        var (rank, fullName) = layerName is null
            ? ((int?)null, (string?)null)
            : EnterLayer(layerName, null, state);
        ParseScope(imported, mediaCondition, rank, fullName, parentSelector: null, state);
    }

    /// <summary>The url/quoted target of an <c>@import</c> plus the clause tail after it.</summary>
    private static (string? Target, string Tail) ImportTarget(string rest)
    {
        if (rest.Length == 0)
        {
            return (null, string.Empty);
        }

        if (rest[0] is '"' or '\'')
        {
            var quote = rest[0];
            var end = rest.IndexOf(quote, 1);
            return end < 0
                ? (null, string.Empty)
                : (rest[1..end], rest[(end + 1)..].Trim());
        }

        if (rest.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
        {
            var close = rest.IndexOf(')');
            if (close < 0)
            {
                return (null, string.Empty);
            }

            var inner = rest[4..close].Trim().Trim('"', '\'');
            return (inner, rest[(close + 1)..].Trim());
        }

        var space = rest.IndexOfAny([' ', '\t']);
        return space < 0 ? (rest, string.Empty) : (rest[..space], rest[(space + 1)..].Trim());
    }

    /// <summary>
    /// The trailing clauses of an <c>@import</c>: an optional <c>layer(name)</c> or bare
    /// <c>layer</c>, then whatever media/supports condition remains — kept verbatim so
    /// <see cref="EvaluateMedia"/> decides it (a clause it cannot parse is undecidable, not false).
    /// </summary>
    private static (string? LayerName, string? MediaTail) ImportClauses(string tail)
    {
        string? layerName = null;
        if (tail.StartsWith("layer", StringComparison.OrdinalIgnoreCase))
        {
            var after = tail["layer".Length..].TrimStart();
            if (after.StartsWith('('))
            {
                var close = after.IndexOf(')', StringComparison.Ordinal);
                layerName = close < 0 ? after[1..].Trim() : after[1..close].Trim();
                tail = close < 0 ? string.Empty : after[(close + 1)..].Trim();
            }
            else
            {
                layerName = string.Empty; // bare `layer` — an anonymous layer at this position
                tail = after;
            }
        }

        return (layerName, tail.Length == 0 ? null : tail);
    }

    private static string JoinMedia(string? media, string condition)
        => media is null ? condition : media + " and " + condition;

    /// <summary>
    /// A layer's rank in declaration order: names register on first use (nested blocks qualify
    /// their names, <c>@layer a { @layer b {} }</c> is <c>a.b</c>), anonymous layers take the next
    /// rank where they stand. Higher rank beats lower; every rank loses to unlayered.
    /// </summary>
    private static (int Rank, string FullName) EnterLayer(string name, string? parentName, ParseState state)
    {
        if (name.Length == 0)
        {
            // Anonymous layer (@layer { … }) — ordered where declared, never revisited by name.
            var full = parentName is null
                ? $"#anonymous-{state.NextLayerRank}"
                : $"{parentName}.#anonymous-{state.NextLayerRank}";
            var anonRank = state.NextLayerRank++;
            state.LayerOrder[full] = anonRank;
            return (anonRank, full);
        }

        var qualified = parentName is null ? name : $"{parentName}.{name}";
        return (RegisterLayer(qualified, null, state), qualified);
    }

    private static int RegisterLayer(string fullName, string? parentName, ParseState state)
    {
        var key = parentName is null ? fullName : $"{parentName}.{fullName}";
        if (!state.LayerOrder.TryGetValue(key, out var rank))
        {
            rank = state.NextLayerRank++;
            state.LayerOrder[key] = rank;
        }

        return rank;
    }

    /// <summary>
    /// CSS nesting: a selector inside a rule body addresses descendants or the parent itself —
    /// <c>&amp;</c> stands for the parent selector, a bare nested selector descends from it.
    /// Comma lists expand as the cross product (<c>.a, .b</c> containing <c>.c</c> compiles to
    /// <c>.a .c, .b .c</c>), the way the browser compiles them.
    /// </summary>
    private static string ExpandNestedSelector(string parent, string nested)
    {
        var expanded = new List<string>();
        foreach (var parentPart in ThemeCss.SelectorParts(parent))
        {
            foreach (var part in ThemeCss.SelectorParts(nested))
            {
                expanded.Add(part.IndexOf('&', StringComparison.Ordinal) >= 0
                    ? part.Replace("&", parentPart, StringComparison.Ordinal)
                    : parentPart + " " + part);
            }
        }

        return string.Join(", ", expanded);
    }

    /// <summary>
    /// The <c>}</c> matching an opening brace, with quoted strings skipped — a brace inside
    /// <c>content: "}"</c> is data. Unbalanced input consumes the rest; the selector check then
    /// fails loudly downstream.
    /// </summary>
    private static int MatchingBrace(string css, int open)
    {
        var depth = 0;
        var quote = '\0';
        for (var i = open; i < css.Length; i++)
        {
            var c = css[i];
            if (quote != '\0')
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '{')
            {
                depth++;
            }
            else if (c == '}' && --depth == 0)
            {
                return i;
            }
        }

        return css.Length;
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

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

    private static readonly Regex RuleBlock =
        new(@"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}", RegexOptions.Compiled, Timeout);

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
    public static Winner Resolve(
        string css,
        IReadOnlyList<Element> chain,
        string property,
        IReadOnlySet<string>? activeStates = null)
    {
        activeStates ??= new HashSet<string>(StringComparer.Ordinal);

        var target = ShorthandOf.TryGetValue(property, out var mapping) ? mapping : default;
        var unmodelled = new List<string>();

        string? winner = null;
        string? source = null;
        var best = (Id: -1, Class: -1, Type: -1);

        foreach (Match rule in RuleBlock.Matches(ThemeCss.StripComments(css)))
        {
            var body = rule.Groups["body"].Value;
            var declared = DeclarationValue(body, property);
            if (declared is null && target.Shorthand is not null)
            {
                var shorthand = DeclarationValue(body, target.Shorthand);
                declared = shorthand is null ? null : target.Extract(shorthand);
            }

            if (declared is null)
            {
                continue;
            }

            foreach (var part in ThemeCss.SelectorParts(rule.Groups["selector"].Value))
            {
                var verdict = Match(part, chain, activeStates);
                if (verdict.Unmodelled)
                {
                    unmodelled.Add(part);
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
        IReadOnlySet<string>? activeStates = null)
    {
        var resolved = Resolve(css, chain, property, activeStates);

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
        IReadOnlySet<string>? activeStates = null)
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
            var resolved = Resolve(css, box, "opacity", activeStates);

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
}

using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.DataTable;

/// <summary>
/// Guards the column pin toggle — the one element that caused two separate defects at once.
/// <para>
/// The pin is a real <c>&lt;button&gt;</c> rendered inside every visible column header whenever
/// <c>ShowColumnMenu</c> is on, which it is by default. In 2.8.9 the only rule that made it visible was
/// <c>.tm-data-table th:hover .tm-col-pin-btn</c>. Two consequences followed, and neither is visible in
/// the markup:
/// </para>
/// <list type="number">
/// <item>
/// <b>Invisible focus stops.</b> The button has no <c>tabindex="-1"</c>, so it is in the keyboard path
/// for every column. A five-column header is nine Tab stops of which five painted nothing — a keyboard
/// user tabs into emptiness (WCAG 2.4.7 Focus Visible). Nobody could reach it before 2.8.9 made the
/// header itself focusable, so the accessibility fix is what exposed the older defect.
/// </item>
/// <item>
/// <b>Alignment.</b> <c>opacity: 0</c> paints nothing but still occupies its width, so the button
/// reserved 1.1rem + 0.25rem = 21.6px of inline space after the label. On a column with
/// <c>Align=Right</c>, <c>text-align: right</c> then right-aligns "label + invisible button", leaving the
/// label short of the content edge its own cells sit flush against.
/// </item>
/// </list>
/// <para>
/// A markup assertion cannot see either one — the button was always in the DOM with the same class. These
/// tests therefore resolve the cascade out of the shipped stylesheet, the same way
/// <see cref="TmDataTableAlignmentTests"/> does, and assert the value that actually applies in a given
/// interaction state. Each fix is asserted in <b>both</b> directions: the state that must show the button
/// and the state that must still hide it, because "always visible" would be a regression dressed up as a
/// fix.
/// </para>
/// </summary>
public class TmDataTablePinFocusTests
{
    // ── Alignment: the pin must not reserve inline space ──────────

    /// <summary>
    /// Taking the button out of flow is what un-breaks <c>Align=Right</c>: an absolutely positioned box
    /// contributes nothing to the inline run its siblings are aligned within.
    /// </summary>
    [Fact]
    public void PinButton_IsPositionedOutOfFlow()
        => PinCascade.Winning("position", PinCascade.HeaderStates.None, PinCascade.PinStates.None)
            .Should().Be(
                "absolute",
                "in flow the button reserves its width after the label, so text-align: right aligns " +
                "\"label + invisible button\" and the label lands short of the cell content edge");

    /// <summary>
    /// The counterpart to the rule above: nothing may put the reserved gap back. A stray
    /// <c>margin-left</c> on an absolutely positioned box is harmless, but a <c>position: static</c>
    /// anywhere in the file would silently restore the whole defect.
    /// </summary>
    [Fact]
    public void PinButton_IsNeverReturnedToTheFlow()
    {
        foreach ((string selector, string body) in ThemeCss.Rules("_data-table.css"))
        {
            if (!ThemeCss.SelectorParts(selector).Any(part => part.Contains("tm-col-pin-btn", StringComparison.Ordinal)))
            {
                continue;
            }

            if (!ThemeCss.Declares(body, "position"))
            {
                continue;
            }

            ThemeCss.Normalise(body).Should().NotContain(
                "position: static",
                "a rule that returns the pin to the flow brings the 21.6px alignment defect back");
        }
    }

    // ── Focus visibility: both directions ─────────────────────────

    /// <summary>
    /// The fix. Keyboard focus anywhere in the header has to reveal the pin, exactly as hovering does.
    /// </summary>
    /// <remarks>
    /// The state deliberately sets <b>only</b> the header's <c>:focus-within</c> and leaves the button
    /// itself unfocused, so the assertion can be satisfied by exactly one selector. The first version of
    /// this test set both states at once and stayed green when the <c>th:focus-within</c> rule was deleted
    /// — <c>.tm-col-pin-btn:focus-visible</c> was quietly carrying it. A test that cannot fail on the
    /// defect it names is worth nothing, so the two rules are isolated instead.
    /// </remarks>
    [Fact]
    public void PinButton_IsVisible_WhenTheHeaderItselfHasKeyboardFocus()
        => PinCascade.Winning("opacity", PinCascade.HeaderStates.FocusWithin, PinCascade.PinStates.None)
            .Should().Be(
                "1",
                "a Tab stop that paints nothing is a WCAG 2.4.7 failure — and with ShowColumnMenu " +
                "defaulting to true, every consumer inherits it on every column");

    /// <summary>
    /// The defensive half of the same fix, isolated the same way.
    /// </summary>
    /// <remarks>
    /// The state is deliberately artificial: in a browser a focused pin always implies
    /// <c>th:focus-within</c>, so this combination cannot occur. That is the point — it makes the
    /// assertion depend on <c>.tm-col-pin-btn:focus-visible</c> and nothing else, which is what keeps the
    /// fallback rule from being deleted as "redundant" by someone who only reads the happy path.
    /// </remarks>
    [Fact]
    public void PinButton_IsVisible_WhenThePinItselfHasKeyboardFocus()
        => PinCascade.Winning("opacity", PinCascade.HeaderStates.None, PinCascade.PinStates.FocusVisible)
            .Should().Be("1");

    /// <summary>
    /// The other direction, and the reason this file is not just one assertion: making the button
    /// permanently visible would also satisfy the test above, while cluttering every header of every
    /// table with a control almost nobody uses. Hidden-until-asked-for is the behaviour being kept.
    /// </summary>
    [Fact]
    public void PinButton_StaysHidden_WhenNothingIsHoveringOrFocusingTheHeader()
        => PinCascade.Winning("opacity", PinCascade.HeaderStates.None, PinCascade.PinStates.None)
            .Should().Be(
                "0",
                "the pin is revealed on demand; a fix that simply shows it always is a different, " +
                "worse component");

    /// <summary>Hovering must keep working — the new selector is added next to it, not instead of it.</summary>
    [Fact]
    public void PinButton_IsVisible_WhenTheHeaderIsHovered()
        => PinCascade.Winning("opacity", PinCascade.HeaderStates.Hover, PinCascade.PinStates.None)
            .Should().Be("1");

    /// <summary>A pinned column shows its pin permanently, focus or no focus — unchanged behaviour.</summary>
    [Fact]
    public void PinButton_IsVisible_WhenTheColumnIsPinned()
        => PinCascade.Winning("opacity", PinCascade.HeaderStates.None, PinCascade.PinStates.Active)
            .Should().Be("1");

    // ── The library owns its own focus ring ───────────────────────

    /// <summary>
    /// 2.8.9 turned the sortable header into a control but shipped no focus style for it, so how a
    /// focused header looked depended on whether the consuming application happened to declare a global
    /// <c>*:focus-visible</c> outline. Ours does; that is luck, not a contract.
    /// </summary>
    [Theory]
    [InlineData(".tm-data-table th:focus-visible")]
    [InlineData(".tm-data-table tbody tr:focus-visible")]
    [InlineData(".tm-col-pin-btn:focus-visible")]
    [InlineData(".tm-th-sort:focus-visible")]
    public void EveryElementTheTableMakesFocusable_BringsItsOwnFocusRing(string selector)
        => ThemeCss.TryProperty("_data-table.css", selector, "outline")
            .Should().NotBeNull(
                "a component that makes an element focusable owns that element's focus indicator; " +
                "borrowing the application's global rule means the ring disappears in any app that " +
                "does not happen to declare one — and measuring a real page showed the borrowed rule " +
                "not reaching the header even in an app that does declare it");

    /// <summary>
    /// The cascade of one property on the pin button, resolved out of the shipped stylesheet for a given
    /// interaction state.
    /// <para>
    /// Modelled: descendant combinators over compounds of type, class and pseudo-class. A selector that
    /// reaches for anything else is treated as not matching, which is correct for the element modelled
    /// here. Pseudo-classes count towards the class column of the specificity, as CSS specifies.
    /// </para>
    /// </summary>
    private static class PinCascade
    {
        [Flags]
        public enum HeaderStates
        {
            None = 0,
            Hover = 1,
            FocusWithin = 2
        }

        [Flags]
        public enum PinStates
        {
            None = 0,
            Hover = 1,
            FocusVisible = 2,
            Active = 4
        }

        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

        private static readonly Regex RuleBlock =
            new(@"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}", RegexOptions.Compiled, Timeout);

        private sealed record Element(string Tag, HashSet<string> Tokens);

        private static IReadOnlyList<Element> Chain(HeaderStates header, PinStates pin)
        {
            HashSet<string> headerTokens = [];
            if (header.HasFlag(HeaderStates.Hover)) headerTokens.Add(":hover");
            if (header.HasFlag(HeaderStates.FocusWithin)) headerTokens.Add(":focus-within");

            HashSet<string> pinTokens = [".tm-col-pin-btn"];
            if (pin.HasFlag(PinStates.Hover)) pinTokens.Add(":hover");
            if (pin.HasFlag(PinStates.FocusVisible)) pinTokens.Add(":focus-visible");
            if (pin.HasFlag(PinStates.Active)) pinTokens.Add(".tm-col-pin-btn--active");

            return
            [
                new Element("table", [".tm-data-table"]),
                new Element("thead", []),
                new Element("tr", []),
                new Element("th", headerTokens),
                new Element("button", pinTokens)
            ];
        }

        public static string Winning(string property, HeaderStates header, PinStates pin)
        {
            IReadOnlyList<Element> chain = Chain(header, pin);
            string css = ThemeCss.ComponentCss("_data-table.css");

            string? winner = null;
            (int Id, int Class, int Type) best = (-1, -1, -1);

            foreach (Match rule in RuleBlock.Matches(css))
            {
                string? declared = ValueOf(rule.Groups["body"].Value, property);
                if (declared is null) continue;

                foreach (string part in ThemeCss.SelectorParts(rule.Groups["selector"].Value))
                {
                    (int, int, int)? specificity = SpecificityIfMatches(part, chain);
                    if (specificity is null) continue;

                    // Later source order wins a tie; the loop walks the file top to bottom.
                    if (specificity.Value.CompareTo(best) >= 0)
                    {
                        best = specificity.Value;
                        winner = declared;
                    }
                }
            }

            winner.Should().NotBeNull(
                $"_data-table.css must declare '{property}' for the pin button in state " +
                $"header={header}, pin={pin}");
            return winner!;
        }

        private static string? ValueOf(string body, string property)
        {
            foreach (string declaration in body.Split(';'))
            {
                int separator = declaration.IndexOf(':', StringComparison.Ordinal);
                if (separator < 0) continue;
                if (string.Equals(declaration[..separator].Trim(), property, StringComparison.Ordinal))
                {
                    return ThemeCss.Normalise(declaration[(separator + 1)..]);
                }
            }

            return null;
        }

        private static (int Id, int Class, int Type)? SpecificityIfMatches(
            string selector,
            IReadOnlyList<Element> chain)
        {
            if (selector.Length == 0) return null;
            if (selector.IndexOfAny(['>', '+', '~', '[', '#', '*', '@']) >= 0) return null;
            if (selector.Contains("::", StringComparison.Ordinal)) return null;

            string[] compounds = selector.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (!CompoundMatches(compounds[^1], chain[^1])) return null;

            int ancestorIndex = chain.Count - 2;
            for (int i = compounds.Length - 2; i >= 0; i--)
            {
                while (ancestorIndex >= 0 && !CompoundMatches(compounds[i], chain[ancestorIndex]))
                {
                    ancestorIndex--;
                }

                if (ancestorIndex < 0) return null;
                ancestorIndex--;
            }

            int classCount = compounds.Sum(compound => Tokens(compound).Count);
            int typeCount = compounds.Count(compound => TypeOf(compound).Length > 0);
            return (0, classCount, typeCount);
        }

        private static bool CompoundMatches(string compound, Element element)
        {
            string tag = TypeOf(compound);
            if (tag.Length > 0 && !tag.Equals(element.Tag, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return Tokens(compound).All(element.Tokens.Contains);
        }

        /// <summary>The type part of a compound — everything before the first <c>.</c> or <c>:</c>.</summary>
        private static string TypeOf(string compound)
        {
            int end = compound.IndexOfAny(['.', ':']);
            return end < 0 ? compound : compound[..end];
        }

        /// <summary>The class and pseudo-class tokens of a compound, each keeping its leading marker.</summary>
        private static List<string> Tokens(string compound)
        {
            List<string> tokens = [];
            int index = compound.IndexOfAny(['.', ':']);
            while (index >= 0 && index < compound.Length)
            {
                int next = compound.IndexOfAny(['.', ':'], index + 1);
                tokens.Add(next < 0 ? compound[index..] : compound[index..next]);
                index = next;
            }

            return tokens;
        }
    }
}

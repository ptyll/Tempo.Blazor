using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Scans a stylesheet for ADDITIVE OPERATORS inside <c>calc(…)</c> and reports the ones that do not
/// carry whitespace on both sides.
/// <para>
/// css-values-3 §8.1 requires whitespace around <c>+</c> and <c>-</c> inside a math function, because
/// the CSS tokenizer would otherwise swallow them into the neighbouring token — <c>100%+8px</c> is not
/// "100% plus 8px", it is a parse error, and a parse error inside a declaration drops the WHOLE
/// declaration. <c>*</c> and <c>/</c> have no such rule and are deliberately not checked.
/// </para>
/// <para>
/// A naive <c>[^\s][+-][^\s]</c> regex cannot be used: it fires on <c>--tm-space-1-5</c>,
/// <c>env(safe-area-inset-bottom)</c>, <c>calc(-1 * …)</c> and <c>1e-5</c>, none of which are
/// operators. The scanner therefore tokenises the expression instead of pattern-matching it, and
/// distinguishes three non-operator shapes: an IDENTIFIER hyphen, a UNARY sign, and a number's
/// EXPONENT sign.
/// </para>
/// </summary>
internal static class CalcWhitespaceScanner
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex Comment =
        new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline, RegexTimeout);

    /// <summary>
    /// Every math function css-values-4 §10 puts the whitespace rule on, not just <c>calc()</c>:
    /// <c>min(100% + 2px, 40rem)</c> is governed by exactly the same clause, so a minifier that
    /// strips the space breaks it identically. Matching only <c>calc(</c> would have let that
    /// through in silence.
    /// <para>
    /// Case-insensitive because CSS function names are — <c>CALC(100%+2px)</c> is the same
    /// declaration and the same defect. The lookbehind keeps <c>-webkit-calc(</c> and
    /// <c>xcalc(</c> from matching.
    /// </para>
    /// </summary>
    private static readonly Regex MathFunctionOpening =
        new(@"(?<![-\w])(calc|min|max|clamp|round|mod|rem|abs|hypot)\(",
            RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexTimeout);

    /// <summary>A quoted string, escapes included — its contents are data, not an expression.</summary>
    private static readonly Regex QuotedString =
        new("\"(\\\\.|[^\"\\\\\n])*\"|'(\\\\.|[^'\\\\\n])*'", RegexOptions.Compiled, RegexTimeout);

    /// <summary>One <c>+</c> or <c>-</c> that the scanner classified as an arithmetic operator.</summary>
    internal sealed record AdditiveOperator(int Index, char Symbol, bool Spaced, string Context)
    {
        public override string ToString() =>
            string.Create(CultureInfo.InvariantCulture, $"'{Symbol}' at offset {Index}: …{Context}…");
    }

    /// <summary>
    /// What the scanner classifies a preceding token as. Only <see cref="Value"/> can stand to the left
    /// of a BINARY operator; after an opening paren, a comma or another operator a <c>+</c>/<c>-</c> is
    /// a unary sign and needs no space.
    /// </summary>
    private enum Preceding
    {
        Boundary,
        Value,
        Operator,
    }

    /// <summary>Comments are stripped the way the bundler strips them, so a commented-out
    /// <c>calc(a+b)</c> in a source file is not reported as a defect.</summary>
    public static string StripComments(string css) =>
        Comment.Replace(css, match => new string(' ', match.Length));

    /// <summary>
    /// Blanks the contents of quoted strings. <c>[data-x="calc(1%+2px)"]</c> and
    /// <c>content: "1+2"</c> are text, not arithmetic, and reporting them would be a false
    /// positive — the kind that gets a guard's threshold relaxed until it guards nothing. Replaced
    /// with spaces rather than removed so every reported offset still points into the real file.
    /// </summary>
    public static string StripStrings(string css) =>
        QuotedString.Replace(css, match => new string(' ', match.Length));

    /// <summary>Every additive operator of every math function in <paramref name="css"/>.</summary>
    public static IReadOnlyList<AdditiveOperator> AdditiveOperators(string css)
    {
        var text = StripStrings(StripComments(css));
        var operators = new List<AdditiveOperator>();
        var scannedUpTo = 0;

        foreach (Match opening in MathFunctionOpening.Matches(text))
        {
            // A math function nested inside an already scanned one is part of that expression's
            // token stream — scanning it again would count its operators twice.
            if (opening.Index < scannedUpTo)
            {
                continue;
            }

            var start = opening.Index + opening.Length;
            var end = MatchingParen(text, start);
            scannedUpTo = end;
            ScanExpression(text, start, end, operators);
        }

        return operators;
    }

    /// <summary>The operators that are MISSING a space on at least one side — the defect.</summary>
    public static IReadOnlyList<AdditiveOperator> Violations(string css) =>
        AdditiveOperators(css).Where(op => !op.Spaced).ToList();

    private static int MatchingParen(string text, int afterOpening)
    {
        var depth = 1;
        for (var i = afterOpening; i < text.Length; i++)
        {
            if (text[i] == '(')
            {
                depth++;
            }
            else if (text[i] == ')' && --depth == 0)
            {
                return i;
            }
        }

        return text.Length;
    }

    private static void ScanExpression(string text, int start, int end, List<AdditiveOperator> operators)
    {
        var preceding = Preceding.Boundary;
        var i = start;

        while (i < end)
        {
            var c = text[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < end && char.IsAsciiDigit(text[i + 1])))
            {
                i = SkipNumber(text, i, end);
                preceding = Preceding.Value;
                continue;
            }

            // A custom property (`--tm-space-1-5`) takes hyphens AND digits, so its internal hyphens
            // must be consumed as part of the name rather than met by the operator branch.
            if (c == '-' && i + 1 < end && text[i + 1] == '-')
            {
                i += 2;
                while (i < end && (char.IsAsciiLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '-'))
                {
                    i++;
                }

                preceding = Preceding.Value;
                continue;
            }

            if (char.IsAsciiLetter(c) || c == '_')
            {
                i = SkipIdentifier(text, i, end);
                preceding = Preceding.Value;
                continue;
            }

            switch (c)
            {
                case '(':
                    preceding = Preceding.Boundary;
                    i++;
                    continue;
                case ')':
                    preceding = Preceding.Value;
                    i++;
                    continue;
                case ',':
                    preceding = Preceding.Boundary;
                    i++;
                    continue;
                case '*':
                case '/':
                    preceding = Preceding.Operator;
                    i++;
                    continue;
            }

            if (c is '+' or '-')
            {
                if (preceding is Preceding.Value)
                {
                    var spacedLeft = i > 0 && char.IsWhiteSpace(text[i - 1]);
                    var spacedRight = i + 1 < text.Length && char.IsWhiteSpace(text[i + 1]);
                    operators.Add(new AdditiveOperator(
                        i,
                        c,
                        spacedLeft && spacedRight,
                        text[Math.Max(start, i - 34)..Math.Min(end, i + 34)].Trim()));
                }

                // Otherwise it is a unary sign: `calc(-1 * x)`, `calc(1px + -2px)`.
                preceding = Preceding.Operator;
                i++;
                continue;
            }

            // `%` and anything else closes a value.
            preceding = Preceding.Value;
            i++;
        }
    }

    /// <summary>A number with an optional exponent, followed by its unit — so <c>1e-5</c> never
    /// reaches the operator branch, while the <c>-</c> of <c>100px-3em</c> does.</summary>
    private static int SkipNumber(string text, int i, int end)
    {
        while (i < end && (char.IsAsciiDigit(text[i]) || text[i] == '.'))
        {
            i++;
        }

        if (i < end && (text[i] == 'e' || text[i] == 'E'))
        {
            var afterExponent = i + 1;
            if (afterExponent < end && (text[afterExponent] == '+' || text[afterExponent] == '-'))
            {
                afterExponent++;
            }

            if (afterExponent < end && char.IsAsciiDigit(text[afterExponent]))
            {
                while (afterExponent < end && char.IsAsciiDigit(text[afterExponent]))
                {
                    afterExponent++;
                }

                i = afterExponent;
            }
        }

        if (i < end && text[i] == '%')
        {
            return i + 1;
        }

        return i < end && (char.IsAsciiLetter(text[i]) || text[i] == '_') ? SkipIdentifier(text, i, end) : i;
    }

    /// <summary>
    /// An identifier such as <c>px</c>, <c>var</c> or <c>safe-area-inset-bottom</c>. A hyphen continues
    /// the identifier ONLY when a letter follows it; that is what keeps <c>100px-3em</c> reporting —
    /// the unit ends at <c>px</c> and the hyphen falls through to the operator branch.
    /// </summary>
    private static int SkipIdentifier(string text, int i, int end)
    {
        while (i < end)
        {
            if (char.IsAsciiLetterOrDigit(text[i]) || text[i] == '_')
            {
                i++;
            }
            else if (text[i] == '-' && i + 1 < end && (char.IsAsciiLetter(text[i + 1]) || text[i + 1] == '_'))
            {
                i += 2;
            }
            else
            {
                break;
            }
        }

        return i;
    }
}

/// <summary>
/// Guards the SHIPPED CSS bundle against additive operators that lost their whitespace.
/// <para>
/// The defect this locks down was introduced by the bundler itself, not by any source file: the
/// minifier in <c>Tempo.Blazor.csproj</c> collapsed the space around every <c>+</c> (a sibling
/// combinator in a selector — but arithmetic inside <c>calc()</c>), which produced 30 invalid
/// operators across 29 <c>calc()</c> expressions while all 975 <c>calc()</c> expressions in the
/// SOURCES were fine. Asserting on the sources would therefore have measured a permanently green
/// population and never seen it.
/// </para>
/// <para>
/// It also went unseen from the other end: the demo app links the SOURCE stylesheet through its
/// <c>@import</c> graph, so no e2e run ever loaded the bundle. The bundle is what
/// <c>tempo-blazor-documentation.json</c> and <c>docs/nuget-package-split-migration.md</c> tell
/// consumers to reference, which made every consumer the first party to execute it.
/// </para>
/// <para>
/// The file read here is the COMMITTED bundle. The <c>BundleCssFiles</c> target of the project this
/// test references can rewrite it during a build, but WHETHER it does is decided by that target's
/// <c>Inputs</c>/<c>Outputs</c> up-to-date check; the Clean-time delete that used to take its output
/// away, and with it the up-to-date check, is gone. This test therefore makes no claim about the file
/// having been bundled from the current sources before it ran.
/// </para>
/// </summary>
public class CssBundleCalcWhitespaceTests
{
    /// <summary>
    /// Below the count the bundle has today. It exists so the guard cannot pass by measuring NOTHING:
    /// if a refactor removed every <c>calc()</c> addition from the stylesheets, "no violations" would
    /// be vacuously true and this floor is what turns that into a failure.
    /// </summary>
    private const int MinimumAdditiveOperators = 30;

    private static DirectoryInfo RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current;
            }

            current = current.Parent!;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx.");
    }

    private static string BundlePath() =>
        Path.Combine(RepositoryRoot().FullName, "src", "Tempo.Blazor", "wwwroot", "css",
            "tempo-blazor.bundled.css");

    private static string ReadBundle()
    {
        var path = BundlePath();
        File.Exists(path).Should().BeTrue($"{path} is the stylesheet consumers reference");
        return File.ReadAllText(path);
    }

    [Fact]
    public void BundledStylesheetSpacesEveryAdditiveCalcOperator()
    {
        var violations = CalcWhitespaceScanner.Violations(ReadBundle());

        var report = new StringBuilder();
        foreach (var violation in violations)
        {
            report.Append('\n').Append(violation);
        }

        violations.Should().BeEmpty(
            "css-values-3 §8.1 requires whitespace around '+' and '-' inside calc(), and a browser "
            + "drops the entire declaration without it. The bundler's minifier is the usual cause — "
            + "check that '+' and '~' are absent from its structural-character class."
            + report);
    }

    [Fact]
    public void BundledStylesheetStillContainsAdditiveCalcOperators()
    {
        var operators = CalcWhitespaceScanner.AdditiveOperators(ReadBundle());

        operators.Should().HaveCountGreaterThanOrEqualTo(MinimumAdditiveOperators,
            "a bundle without calc() additions would make the whitespace guard vacuously green");
    }

    /// <summary>Shapes that MUST be reported. Losing any of them silently re-opens the defect.</summary>
    [Theory]
    [InlineData(".a{width:calc(100%+8px)}", 1)]
    [InlineData(".a{top:calc(100%+4px)}", 1)]
    [InlineData(".a{width:calc(100%-8px)}", 1)]
    [InlineData(".a{width:calc(100px-3em)}", 1)]
    [InlineData(".a{width:calc(var(--a)-var(--b))}", 1)]
    [InlineData(".a{width:calc(var(--tm-space-3)+1.5rem)}", 1)]
    [InlineData(".a{width:calc(var(--tm-stepper-indicator-size,40px)+var(--tm-space-6))}", 1)]
    [InlineData(".a{width:calc((var(--tm-space-20) * 2)+var(--tm-space-10))}", 1)]
    [InlineData(".a{width:calc(100%+2 * var(--tm-space-4))}", 1)]
    [InlineData(".a{width:calc(calc(1px+2px) + 3px)}", 1)]
    [InlineData(".a{p:calc(var(--tm-space-2)+env(safe-area-inset-bottom,0px))}", 1)]
    [InlineData(".a{width:calc(100% +8px)}", 1)]
    [InlineData(".a{width:calc(100%+ 8px)}", 1)]
    [InlineData(".a{width:calc(1px+2px+3px)}", 2)]
    [InlineData(".a{width:CALC(100%+2px)}", 1)]                        // function names are case-insensitive
    [InlineData(".a{width:min(100%+2px, 40rem)}", 1)]                  // css-values-4 §10: same rule
    [InlineData(".a{width:max(50%+1rem, 10rem)}", 1)]
    [InlineData(".a{width:clamp(1rem, 50%+2px, 40rem)}", 1)]
    [InlineData(".a{width:hypot(1px+2px, 3px)}", 1)]
    [InlineData(".a{width:MIN(100%-2px, 40rem)}", 1)]
    public void ScannerReportsOperatorsWithoutSurroundingWhitespace(string css, int expected) =>
        CalcWhitespaceScanner.Violations(css).Should().HaveCount(expected);

    /// <summary>
    /// Shapes that must NOT be reported. These are the false positives a regex-shaped guard produces,
    /// and a guard that fires on them gets its threshold relaxed until it stops guarding anything.
    /// </summary>
    [Theory]
    [InlineData(".a{width:calc(100% + 8px)}")]
    [InlineData(".a{width:calc(100% - 8px)}")]
    [InlineData(".a{width:calc(100px - 3em)}")]
    [InlineData(".a{width:calc(var(--tm-space-1-5) + 1px)}")]                 // hyphen inside an identifier
    [InlineData(".a{width:calc(var(--tm-space-1-5) * 2)}")]
    [InlineData(".a{p:calc(var(--tm-space-2) + env(safe-area-inset-bottom, 0px))}")]
    [InlineData(".a{width:calc(-1 * var(--tm-space-4))}")]                    // leading unary sign
    [InlineData(".a{width:calc(1px + -2px)}")]                                // unary after an operator
    [InlineData(".a{width:calc(var(--x, -2px) + 1px)}")]                      // unary after a comma
    [InlineData(".a{width:calc(1e-5 * 1px)}")]                                // exponent sign
    [InlineData(".a{width:calc(1e5 - 1px)}")]
    [InlineData(".a{width:calc((var(--tm-space-20) * 2) + var(--tm-space-10))}")]
    [InlineData(".a{width:calc(min(100%, 50vw) + 2px)}")]
    [InlineData(".a{width:calc(100% + var(--x, calc(2px + 1px)))}")]          // nested calc
    [InlineData(".a{grid-template-columns:repeat(2, calc(50% - 4px))}")]
    [InlineData(".a{margin:-8px;grid-area:a-1;width:-webkit-fill-available}")] // no calc() at all
    [InlineData(".a + .b{margin:0}.c ~ .d{margin:0}")]                        // combinators, not arithmetic
    [InlineData(".a{width:calc(100% + 4px)}/* calc(1%+2px) lives in a comment */")]
    [InlineData(".a[data-x=\"calc(1%+2px)\"]{margin:0}")]                    // selector string, not maths
    [InlineData(".a{content:\"1+2\";width:calc(100% + 4px)}")]              // content string
    [InlineData(".a{content:'calc(3%+4px)'}")]
    [InlineData(".a{width:min(100% + 2px, 40rem)}")]
    [InlineData(".a{width:clamp(1rem, 50% + 2px, 40rem)}")]
    [InlineData(".a{font-size:1rem;margin:0 -1rem}")]                       // `rem` the unit, not the function
    [InlineData(".a{width:calc(2rem - 1rem)}")]
    public void ScannerAcceptsLegitimateShapes(string css) =>
        CalcWhitespaceScanner.Violations(css).Should().BeEmpty();
}

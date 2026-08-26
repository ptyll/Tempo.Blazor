using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Guards a CSS rule that is easy to state and easy to forget: a custom property substitutes its
/// <c>var()</c> at the element where it is DECLARED, not where it is used. A token declared in
/// <c>:root</c> therefore computes with the LIGHT values and inherits that finished colour downwards —
/// a descendant that overrides the referenced token cannot change it retroactively.
/// <para>
/// That only matters because both of Tempo's theming switches are documented as usable "on a parent
/// element", and the demo really does put <c>data-theme</c> on a layout wrapper rather than on
/// <c>&lt;html&gt;</c>. Consolidating the three "ink on a primary fill" tokens into one alias chain
/// declared solely in <c>:root</c> looked correct in every ratio test — <see cref="ThemeCss.TokenGraph"/>
/// layers the two files and resolves, which models the theme sitting ON the declaring element — and it
/// rendered the filled primary button's label WHITE in the demo's dark theme: 2.54:1, the exact defect
/// 2.5.4 had fixed. Only a rendered before/after pair showed it.
/// </para>
/// <para>
/// The remedy is to repeat the same EXPRESSION in the dark block, which is not a second definition: both
/// declarations still resolve through one source. The debt list below is what already shipped this way;
/// it is frozen so nothing new can join it, because every entry is a token that silently keeps its light
/// value under a descendant-scoped dark theme.
/// </para>
/// </summary>
public class ThemeScopeInheritanceTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex SingleVar =
        new(@"^var\(\s*(--tm-[\w-]+)\s*\)$", RegexOptions.Compiled, RegexTimeout);

    /// <summary>
    /// Tokens that already had this shape before the consolidation. They are NOT approved — each one keeps
    /// its light value when the theme is scoped on a descendant — they are recorded so the guard can fail on
    /// anything NEW without demanding 29 unrelated fixes in one phase. Shrinking this list is a carry-forward.
    /// </summary>
    private static readonly HashSet<string> KnownDebt = new(StringComparer.Ordinal)
    {
        "--tm-border-color-focus", "--tm-text-link", "--tm-surface", "--tm-surface-secondary",
        "--tm-color-surface-secondary", "--tm-bg-elevated", "--tm-bg-primary", "--tm-bg-tertiary",
        "--tm-border-default", "--tm-border-hover", "--tm-color-bg", "--tm-color-danger-soft",
        "--tm-color-error", "--tm-color-neutral-900", "--tm-color-primary-bg", "--tm-color-primary-bg-subtle",
        "--tm-color-primary-soft", "--tm-color-success-soft", "--tm-color-surface-muted",
        "--tm-color-surface-subtle", "--tm-color-text-disabled", "--tm-color-text-secondary",
        "--tm-color-text-subtle", "--tm-color-warning-bg-subtle", "--tm-color-warning-soft",
        "--tm-surface-100", "--tm-surface-200", "--tm-text-muted", "--tm-text-placeholder",
    };

    /// <summary>Follows a chain of pure <c>var(--x)</c> aliases to the literal at its end.</summary>
    private static string Resolve(string token, IReadOnlyDictionary<string, string> tokens,
        HashSet<string>? seen = null)
    {
        seen ??= new HashSet<string>(StringComparer.Ordinal);
        if (!seen.Add(token) || !tokens.TryGetValue(token, out var value))
        {
            return token;
        }

        var alias = SingleVar.Match(value);
        return alias.Success ? Resolve(alias.Groups[1].Value, tokens, seen) : value;
    }

    /// <summary>
    /// Tokens declared only in <c>:root</c> whose value would differ under the dark theme — i.e. exactly the
    /// ones a descendant-scoped theme cannot reach.
    /// </summary>
    private static List<string> TokensThatCannotFollowADescendantScopedTheme()
    {
        var light = ThemeCss.Declarations(ThemeCss.CssPath("tokens.css"));
        var dark = ThemeCss.Declarations(ThemeCss.CssPath("tokens-dark.css"));
        var merged = new Dictionary<string, string>(light, StringComparer.Ordinal);
        foreach (var (name, value) in dark)
        {
            merged[name] = value;
        }

        return light.Keys
            .Where(name => !dark.ContainsKey(name))
            .Where(name => !string.Equals(Resolve(name, light), Resolve(name, merged), StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    [Fact]
    public void NoNewToken_SilentlyKeepsItsLightValue_UnderADescendantScopedDarkTheme()
    {
        var offenders = TokensThatCannotFollowADescendantScopedTheme();

        offenders.Should().OnlyContain(token => KnownDebt.Contains(token),
            "a token declared only in :root computes its var() with the LIGHT values and inherits that "
            + "finished colour, so a theme switched on a parent element never reaches it — repeat the same "
            + "expression in tokens-dark.css instead of relying on the referenced token being overridden");
    }

    /// <summary>
    /// The three tokens this line consolidated must NOT be in the debt list: they carry the WCAG fix, and the
    /// ratio guards cannot see this failure mode at all (they layer the two files, which models the theme
    /// sitting on the declaring element).
    /// </summary>
    [Theory]
    [InlineData("--tm-color-on-primary")]
    [InlineData("--tm-color-primary-contrast")]
    [InlineData("--tm-control-glyph-color")]
    [InlineData("--tm-control-hover-fill")]
    [InlineData("--tm-border-color-control")]
    [InlineData("--tm-sort-indicator-idle")]
    [InlineData("--tm-sort-indicator-active")]
    public void ControlAndInkTokens_AreDeclaredInTheDarkBlock(string token)
        => ThemeCss.Declarations(ThemeCss.CssPath("tokens-dark.css")).Should().ContainKey(token,
            "this token carries a contrast fix, so it must reach a consumer that switches the theme with "
            + "[data-theme=\"dark\"] or .tm-dark on a parent element, not only on <html>");

    /// <summary>
    /// Non-vacuous gate: the sweep has to be looking at a populated token file. Without it, a renamed path
    /// or an empty parse would make both assertions above permanently and silently green.
    /// </summary>
    [Fact]
    public void TheSweep_ActuallyReadsBothTokenFiles()
    {
        ThemeCss.Declarations(ThemeCss.CssPath("tokens.css")).Should().HaveCountGreaterThan(150);
        ThemeCss.Declarations(ThemeCss.CssPath("tokens-dark.css")).Should().HaveCountGreaterThan(50);
        KnownDebt.Should().NotBeEmpty("an empty debt list would mean the sweep found nothing to compare");
    }
}

using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Guards the 2.12.0 deprecation of <c>--tm-color-primary-light</c> (Fáze 8 carry-forward, plan
/// 98c80bc3): the alias resolves to <c>--tm-color-primary-300</c>, which as a tint behind text
/// reaches only ~2.87:1 — a contrast trap, not a subtle background. The token stays declared
/// (public since 2.11.0, so existing consumers keep rendering), but it must stay marked DEPRECATED
/// at its declaration site and no library stylesheet may reach for it again.
/// </summary>
public class DeprecatedTokenGuardTests
{
    private const string DeprecatedToken = "--tm-color-primary-light";

    [Fact]
    public void TheDeprecatedAlias_IsMarkedAtItsDeclarationSite()
    {
        var tokens = File.ReadAllText(ThemeCss.CssPath("tokens.css"));

        tokens.Should().Contain(DeprecatedToken,
            "the token stays declared — removing it would break consumers since 2.11.0");

        // The DEPRECATED marker comment must sit next to the declaration, not drift away.
        var declarationAt = tokens.IndexOf(DeprecatedToken + ":", StringComparison.Ordinal);
        declarationAt.Should().BeGreaterThanOrEqualTo(0, "the alias must be declared in tokens.css");
        var start = Math.Max(0, declarationAt - 400);
        var end = Math.Min(tokens.Length, declarationAt + DeprecatedToken.Length + 200);
        var window = tokens[start..end];
        window.Should().Contain("DEPRECATED",
            "the deprecation note must stay attached to the --tm-color-primary-light declaration");
    }

    [Fact]
    public void NoLibraryStylesheet_UsesTheDeprecatedAlias()
    {
        var cssRoot = Path.GetDirectoryName(ThemeCss.CssPath("tokens.css"))!;
        var offenders = Directory.EnumerateFiles(cssRoot, "*.css", SearchOption.AllDirectories)
            .Where(file => !Path.GetFileName(file).Equals("tokens.css", StringComparison.OrdinalIgnoreCase))
            .Select(file => (File: file, Text: File.ReadAllText(file)))
            .Where(pair => pair.Text.Contains($"var({DeprecatedToken}", StringComparison.Ordinal))
            .Select(pair => Path.GetFileName(pair.File))
            .ToList();

        offenders.Should().BeEmpty(
            "no component stylesheet may read --tm-color-primary-light — new code uses --tm-color-primary-subtle");
    }
}
